using AjoCircle.Application.Contracts;
using AjoCircle.Domain.Common;
using AjoCircle.Domain.Groups;
using AjoCircle.Domain.Notifications;
using AjoCircle.Domain.Schemes;
using Microsoft.EntityFrameworkCore;

namespace AjoCircle.Application;

public sealed class InvitationService(IAppDbContext db, ICurrentUser currentUser, IAppClock clock, AuditWriter audit, GroupService groups, Notifier notifier,
    IdentityGate gate)
{
    private Guid Me => currentUser.UserId ?? throw new UnauthenticatedException();

    private async Task<Group> OrganizerGroupAsync(Guid groupId, CancellationToken ct)
    {
        var g = await db.Groups.SingleOrDefaultAsync(x => x.Id == groupId, ct);
        if (g is null || !g.IsOrganizer(Me)) throw new NotFoundException();
        return g;
    }

    public async Task<CreatedInvitationDto> CreateAsync(Guid groupId, CreateInvitationRequest req, CancellationToken ct)
    {
        var g = await OrganizerGroupAsync(groupId, ct);
        await gate.RequireAsync(Me, Domain.Identity.IdentityLevel.Organizer, ct);
        if (g.Status != GroupStatus.OpenForJoining)
            throw new DomainConflictException("group_not_open", "Publish the group before inviting members.");
        var (inv, raw) = GroupInvitation.Create(g.Id, Me, req.ValidityDays ?? GroupInvitation.DefaultValidityDays, req.MaxUses, clock.UtcNow);
        db.GroupInvitations.Add(inv);
        audit.Write(Me, "invitation.created", "invitation", inv.Id, g.Id, new { inv.ExpiresAt, inv.MaxUses }); // never log the token
        await db.SaveChangesAsync(ct);
        return new CreatedInvitationDto(inv.Id, raw, inv.ExpiresAt, inv.MaxUses);
    }

    public async Task<IReadOnlyList<InvitationDto>> ListAsync(Guid groupId, CancellationToken ct)
    {
        var g = await OrganizerGroupAsync(groupId, ct);
        var now = clock.UtcNow;
        var list = await db.GroupInvitations.AsNoTracking().Where(i => i.GroupId == g.Id).OrderByDescending(i => i.CreatedAt).ToListAsync(ct);
        return list.Select(i => new InvitationDto(i.Id, i.CreatedAt, i.ExpiresAt, i.MaxUses, i.UseCount, i.RevokedAt, i.UnusableReason(now) ?? "active")).ToList();
    }

    public async Task RevokeAsync(Guid groupId, Guid invitationId, CancellationToken ct)
    {
        var g = await OrganizerGroupAsync(groupId, ct);
        var inv = await db.GroupInvitations.SingleOrDefaultAsync(i => i.Id == invitationId && i.GroupId == g.Id, ct) ?? throw new NotFoundException();
        inv.Revoke(Me, clock.UtcNow);
        audit.Write(Me, "invitation.revoked", "invitation", inv.Id, g.Id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Anonymous-safe view. Unknown tokens and unusable invitations look alike apart from a generic reason.</summary>
    public async Task<PublicInvitationDto> PublicViewAsync(string token, CancellationToken ct)
    {
        var (inv, g) = await FindAsync(token, ct);
        var version = await db.SchemeVersions.AsNoTracking().SingleAsync(v => v.Id == g.CurrentSchemeVersionId, ct);
        var capacity = g.Basics(version.RulesJson).MemberCount;
        var organizer = await db.Users.Where(u => u.Id == g.OrganizerId).Select(u => u.DisplayName).SingleAsync(ct);
        var active = await db.GroupMemberships.CountAsync(m => m.GroupId == g.Id && m.Status == MembershipStatus.Active, ct);

        string? reason = inv.UnusableReason(clock.UtcNow) switch
        {
            "expired" => "This invitation has expired. Ask the organizer for a new link.",
            "revoked" => "This invitation is no longer valid. Ask the organizer for a new link.",
            "used_up" => "This invitation has already been used.",
            _ => g.Status != GroupStatus.OpenForJoining ? "This group is no longer accepting members."
               : active >= capacity ? "This group is full." : null,
        };
        if (reason is null && currentUser.UserId is { } uid &&
            await db.GroupMemberships.AnyAsync(m => m.GroupId == g.Id && m.UserId == uid && m.Status == MembershipStatus.Active, ct))
            reason = "You are already a member of this group.";

        if (g.IsFixedTerm)
        {
            var f = SchemeJson.DeserializeFixedTerm(version.RulesJson);
            return new PublicInvitationDto(g.Name, organizer, g.SchemeType, version.Id, version.Version, null, null,
                Math.Max(0, capacity - active), reason is null, reason, FixedTermRulesDto.From(f), FixedTermSummaryDto.From(FixedTermCalculator.Summarize(f)));
        }
        var rules = SchemeJson.Deserialize(version.RulesJson);
        return new PublicInvitationDto(g.Name, organizer, g.SchemeType, version.Id, version.Version, RotatingRulesDto.From(rules),
            SchemeSummaryDto.From(RotatingSchemeCalculator.Summarize(rules)), Math.Max(0, capacity - active), reason is null, reason);
    }

    /// <summary>
    /// Joins under a row lock on the group so two people cannot take the last place. The caller must accept the exact
    /// current terms version; a stale version is rejected so they re-read the terms (brief §4.4).
    /// </summary>
    public async Task<JoinResultDto> JoinAsync(string token, JoinGroupRequest req, CancellationToken ct)
    {
        var me = Me;
        await gate.RequireAsync(me, Domain.Identity.IdentityLevel.Member, ct); // owner decision 7: phone + ID before joining

        await using var tx = await db.BeginTransactionAsync(ct);
        var (inv, _) = await FindAsync(token, ct, track: true);
        var g = await db.LockGroupAsync(inv.GroupId, ct) ?? throw new NotFoundException();
        var now = clock.UtcNow;

        if (inv.UnusableReason(now) is { } bad)
            throw new DomainConflictException("invitation_" + bad, "This invitation can no longer be used.");
        if (g.CurrentSchemeVersionId != req.AcceptedSchemeVersionId)
            throw new DomainConflictException("terms_changed", "The group's terms have changed. Review the latest terms before joining.");
        if (await db.GroupMemberships.AnyAsync(m => m.GroupId == g.Id && m.UserId == me, ct))
            throw new DomainConflictException("already_member", "You are already a member of this group.");

        var capacity = g.Basics((await db.SchemeVersions.SingleAsync(v => v.Id == g.CurrentSchemeVersionId, ct)).RulesJson).MemberCount;
        var activeMembers = await db.GroupMemberships.Where(m => m.GroupId == g.Id && m.Status == MembershipStatus.Active).ToListAsync(ct);

        var seq = g.AdmitMember(activeMembers.Count, capacity, now);
        inv.RecordUse(now);
        var membership = GroupMembership.Create(g.Id, me, seq, req.AcceptedSchemeVersionId, inv.Id, now);
        db.GroupMemberships.Add(membership);
        audit.Write(me, "membership.joined", "membership", membership.Id, g.Id,
            new { acceptedSchemeVersionId = req.AcceptedSchemeVersionId, joinSequence = seq, invitationId = inv.Id });

        if (g.OrganizerId != me)
        {
            var joiner = await notifier.NameOfAsync(me, ct);
            var full = g.Status == GroupStatus.ReadyToStart;
            notifier.Add(g.OrganizerId, full ? NotificationType.GroupReadyToStart : NotificationType.MemberJoined, g.Id,
                full ? $"{joiner} joined — {g.Name} is full and ready to start" : $"{joiner} joined {g.Name}",
                full ? $"All {capacity} members have joined and accepted the terms. Check the payout order, then start the group."
                     : $"{activeMembers.Count + 1} of {capacity} members have joined.",
                full ? $"/app/groups/{g.Id}" : $"/app/groups/{g.Id}/members");
        }

        if (g.Status == GroupStatus.ReadyToStart) // just became full
        {
            audit.Write(null, "group.enrolment_closed", "group", g.Id, g.Id, new { members = activeMembers.Count + 1, automatic = true }); // system action, not the joiner's
            await groups.AssignJoinOrderIfNeededAsync(g, [.. activeMembers, membership], null, ct);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new JoinResultDto(g.Id, membership.Id);
    }

    private async Task<(GroupInvitation, Group)> FindAsync(string token, CancellationToken ct, bool track = false)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) throw new NotFoundException("Invitation not found.");
        var hash = GroupInvitation.HashToken(token);
        var invQuery = track ? db.GroupInvitations : db.GroupInvitations.AsNoTracking();
        var inv = await invQuery.SingleOrDefaultAsync(i => i.TokenHash == hash, ct) ?? throw new NotFoundException("Invitation not found.");
        var g = await db.Groups.AsNoTracking().SingleAsync(x => x.Id == inv.GroupId, ct);
        if (g.CurrentSchemeVersionId is null) throw new NotFoundException("Invitation not found.");
        return (inv, g);
    }
}
