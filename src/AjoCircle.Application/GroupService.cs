using AjoCircle.Application.Contracts;
using AjoCircle.Domain.Common;
using AjoCircle.Domain.Groups;
using AjoCircle.Domain.Schemes;
using Microsoft.EntityFrameworkCore;

namespace AjoCircle.Application;

/// <summary>
/// Group use cases. Every method resolves the caller's relationship to the group (organizer / active member)
/// on the server. A group the caller cannot see returns 404, so IDs cannot be probed (brief §2, §3.1).
/// </summary>
public sealed class GroupService(IAppDbContext db, ICurrentUser currentUser, IAppClock clock, AuditWriter audit)
{
    private Guid Me => currentUser.UserId ?? throw new UnauthenticatedException();

    // ---------- Access helpers ----------

    private async Task<Group> OrganizerGroupAsync(Guid groupId, CancellationToken ct, bool track = true)
    {
        var q = track ? db.Groups : db.Groups.AsNoTracking();
        var g = await q.SingleOrDefaultAsync(x => x.Id == groupId, ct);
        if (g is null || !g.IsOrganizer(Me)) throw new NotFoundException();
        return g;
    }

    private async Task<(Group Group, ViewerRole Role, GroupMembership? Membership)> ViewableGroupAsync(Guid groupId, CancellationToken ct)
    {
        var me = Me;
        var g = await db.Groups.AsNoTracking().SingleOrDefaultAsync(x => x.Id == groupId, ct) ?? throw new NotFoundException();
        var membership = await db.GroupMemberships.AsNoTracking()
            .SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == me && m.Status == MembershipStatus.Active, ct);
        if (g.IsOrganizer(me)) return (g, ViewerRole.Organizer, membership);
        if (membership is not null) return (g, ViewerRole.Member, membership);
        throw new NotFoundException();
    }

    private async Task RequireVerifiedAsync(CancellationToken ct)
    {
        var me = Me;
        if (!await db.Users.AnyAsync(u => u.Id == me && u.ContactVerifiedAt != null, ct))
            throw new DomainException("contact_unverified", "Verify your email address before doing this.");
    }

    private async Task<RotatingSchemeRules> CurrentRulesAsync(Group g, CancellationToken ct)
    {
        if (g.CurrentSchemeVersionId is not { } vid) return g.GetDraftRules();
        var v = await db.SchemeVersions.AsNoTracking().SingleAsync(x => x.Id == vid, ct);
        return SchemeJson.Deserialize(v.RulesJson);
    }

    // ---------- Queries ----------

    public SchemePreviewDto Preview(RotatingRulesDto rules)
    {
        var domain = rules.ToDomain();
        domain.Validate(clock.LagosToday);
        return ToPreview(domain);
    }

    private static SchemePreviewDto ToPreview(RotatingSchemeRules r) => new(
        SchemeSummaryDto.From(RotatingSchemeCalculator.Summarize(r)),
        RotatingSchemeCalculator.BuildSchedule(r).Select(p => new RoundPlanDto(p.RoundNumber, p.ContributionDueDate, p.PayoutDate)).ToList());

    public async Task<IReadOnlyList<GroupListItemDto>> ListMineAsync(CancellationToken ct)
    {
        var me = Me;
        var memberGroupIds = db.GroupMemberships.Where(m => m.UserId == me && m.Status == MembershipStatus.Active).Select(m => m.GroupId);
        var groups = await db.Groups.AsNoTracking()
            .Where(g => g.OrganizerId == me || memberGroupIds.Contains(g.Id))
            .OrderByDescending(g => g.CreatedAt).Take(200).ToListAsync(ct);
        var ids = groups.Select(g => g.Id).ToList();
        var counts = await db.GroupMemberships.Where(m => ids.Contains(m.GroupId) && m.Status == MembershipStatus.Active)
            .GroupBy(m => m.GroupId).Select(x => new { x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var versionIds = groups.Where(g => g.CurrentSchemeVersionId != null).Select(g => g.CurrentSchemeVersionId!.Value).ToList();
        var versions = await db.SchemeVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);

        return groups.Select(g =>
        {
            var rules = g.CurrentSchemeVersionId is { } vid ? SchemeJson.Deserialize(versions[vid].RulesJson) : g.GetDraftRules();
            return new GroupListItemDto(g.Id, g.Name, g.SchemeType, g.Status, g.IsOrganizer(me) ? ViewerRole.Organizer : ViewerRole.Member,
                counts.GetValueOrDefault(g.Id), rules.MemberCount, new MoneyDto(rules.ContributionAmountMinor, rules.Currency),
                rules.Frequency, rules.StartDate);
        }).ToList();
    }

    public async Task<GroupDetailDto> GetAsync(Guid groupId, CancellationToken ct)
    {
        var (g, role, membership) = await ViewableGroupAsync(groupId, ct);
        var rules = await CurrentRulesAsync(g, ct);
        var organizerName = await db.Users.Where(u => u.Id == g.OrganizerId).Select(u => u.DisplayName).SingleAsync(ct);
        var activeCount = await db.GroupMemberships.CountAsync(m => m.GroupId == g.Id && m.Status == MembershipStatus.Active, ct);
        var version = g.CurrentSchemeVersionId is { } vid
            ? await db.SchemeVersions.AsNoTracking().SingleAsync(v => v.Id == vid, ct) : null;

        MyMembershipDto? mine = null;
        if (membership is not null)
        {
            var pos = await db.PayoutPositions.Where(p => p.MembershipId == membership.Id).Select(p => (int?)p.Position).SingleOrDefaultAsync(ct);
            var acceptedVersion = await db.SchemeVersions.Where(v => v.Id == membership.AcceptedSchemeVersionId).Select(v => v.Version).SingleAsync(ct);
            mine = new MyMembershipDto(membership.Id, pos, acceptedVersion, membership.JoinedAt);
        }

        return new GroupDetailDto(g.Id, g.Name, g.SchemeType, g.Status, role, organizerName, RotatingRulesDto.From(rules),
            SchemeSummaryDto.From(RotatingSchemeCalculator.Summarize(rules)), version?.Version, version?.Id, activeCount,
            g.CreatedAt, g.PublishedAt, g.StartedAt, g.CancelledAt, g.CancellationReason, // members are told why a group was cancelled (brief §14.1)
            mine, role == ViewerRole.Organizer ? AllowedActions(g, rules, activeCount) : []);
    }

    private static IReadOnlyList<string> AllowedActions(Group g, RotatingSchemeRules rules, int activeCount) => g.Status switch
    {
        GroupStatus.Draft => ["edit", "publish", "cancel"],
        GroupStatus.OpenForJoining => activeCount >= RotatingSchemeRules.MinMembers
            ? ["invite", "closeEnrolment", "cancel"] : ["invite", "cancel"],
        GroupStatus.ReadyToStart => rules.PayoutOrder == PayoutOrderMethod.OrganizerAssigned
            ? ["assignPayoutOrder", "start", "cancel"] : ["start", "cancel"],
        _ => [],
    };

    public async Task<SchemePreviewDto> SchedulePreviewAsync(Guid groupId, CancellationToken ct)
    {
        var (g, _, _) = await ViewableGroupAsync(groupId, ct);
        return ToPreview(await CurrentRulesAsync(g, ct));
    }

    public async Task<IReadOnlyList<MemberDto>> MembersAsync(Guid groupId, CancellationToken ct)
    {
        var (g, role, _) = await ViewableGroupAsync(groupId, ct);
        var me = Me;
        var rows = await (from m in db.GroupMemberships
                          join u in db.Users on m.UserId equals u.Id
                          join v in db.SchemeVersions on m.AcceptedSchemeVersionId equals v.Id
                          join p in db.PayoutPositions on m.Id equals p.MembershipId into pp
                          from p in pp.DefaultIfEmpty()
                          where m.GroupId == g.Id && m.Status == MembershipStatus.Active
                          orderby m.JoinSequence
                          select new { m, u.DisplayName, u.Email, v.Version, Position = (int?)p.Position }).ToListAsync(ct);
        // Members never see other members' contact details.
        return rows.Select(r => new MemberDto(r.m.Id, r.DisplayName, role == ViewerRole.Organizer ? r.Email : null,
            r.m.JoinSequence, r.Position, r.Version, r.m.JoinedAt, r.m.UserId == me)).ToList();
    }

    public async Task<IReadOnlyList<SchemeVersionDto>> SchemeVersionsAsync(Guid groupId, CancellationToken ct)
    {
        var (g, _, _) = await ViewableGroupAsync(groupId, ct);
        var versions = await db.SchemeVersions.AsNoTracking().Where(v => v.GroupId == g.Id).OrderBy(v => v.Version).ToListAsync(ct);
        return versions.Select(v => new SchemeVersionDto(v.Id, v.Version, RotatingRulesDto.From(SchemeJson.Deserialize(v.RulesJson)), v.CreatedAt)).ToList();
    }

    public async Task<IReadOnlyList<RoundDto>> RoundsAsync(Guid groupId, CancellationToken ct)
    {
        var (g, _, _) = await ViewableGroupAsync(groupId, ct);
        var rows = await (from r in db.SchemeRounds
                          join p in db.Payouts on r.Id equals p.RoundId
                          join m in db.GroupMemberships on p.RecipientMembershipId equals m.Id
                          join u in db.Users on m.UserId equals u.Id
                          where r.GroupId == g.Id
                          orderby r.RoundNumber
                          select new { r, p, u.DisplayName }).ToListAsync(ct);
        return rows.Select(x => new RoundDto(x.r.Id, x.r.RoundNumber, x.r.ContributionDueDate, x.r.PayoutDate, x.r.Status,
            x.DisplayName, new MoneyDto(x.p.AmountMinor, x.p.Currency), x.p.Status)).ToList();
    }

    public async Task<PagedResult<ObligationDto>> ContributionsAsync(Guid groupId, int page, int pageSize, CancellationToken ct)
    {
        var (g, role, membership) = await ViewableGroupAsync(groupId, ct);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = from o in db.ContributionObligations
                join r in db.SchemeRounds on o.RoundId equals r.Id
                join m in db.GroupMemberships on o.MembershipId equals m.Id
                join u in db.Users on m.UserId equals u.Id
                where o.GroupId == g.Id
                select new { o, r.RoundNumber, u.DisplayName, m.JoinSequence };
        if (role == ViewerRole.Member) // members see only their own obligations
        {
            var mid = membership!.Id;
            q = q.Where(x => x.o.MembershipId == mid);
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(x => x.RoundNumber).ThenBy(x => x.JoinSequence).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<ObligationDto>(rows.Select(x => new ObligationDto(x.o.Id, x.RoundNumber, x.DisplayName, x.o.MembershipId,
            new MoneyDto(x.o.AmountMinor, x.o.Currency), x.o.DueDate, x.o.Status)).ToList(), page, pageSize, total);
    }

    public async Task<PagedResult<AuditLogDto>> AuditLogsAsync(Guid groupId, int page, int pageSize, CancellationToken ct)
    {
        var g = await OrganizerGroupAsync(groupId, ct, track: false);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var q = from a in db.AuditLogs
                join u in db.Users on a.ActorId equals u.Id into uu
                from u in uu.DefaultIfEmpty()
                where a.GroupId == g.Id
                select new { a, Name = u != null ? u.DisplayName : null };
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.a.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<AuditLogDto>(rows.Select(x => new AuditLogDto(x.a.Id, x.a.Action, x.a.EntityType, x.a.EntityId, x.Name, x.a.Metadata, x.a.CreatedAt)).ToList(),
            page, pageSize, total);
    }

    // ---------- Commands ----------

    public async Task<Guid> CreateRotatingAsync(CreateRotatingGroupRequest req, CancellationToken ct)
    {
        await RequireVerifiedAsync(ct);
        var g = Group.CreateRotatingDraft(Me, req.Name, req.Rules.ToDomain(), clock.UtcNow);
        db.Groups.Add(g);
        audit.Write(Me, "group.created", "group", g.Id, g.Id, new { g.Name, scheme = g.SchemeType.ToString() });
        await db.SaveChangesAsync(ct);
        return g.Id;
    }

    public async Task UpdateDraftAsync(Guid groupId, UpdateDraftGroupRequest req, CancellationToken ct)
    {
        var g = await OrganizerGroupAsync(groupId, ct);
        g.UpdateDraft(req.Name, req.Rules.ToDomain());
        audit.Write(Me, "group.draft_updated", "group", g.Id, g.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task PublishAsync(Guid groupId, CancellationToken ct)
    {
        await RequireVerifiedAsync(ct);
        var g = await OrganizerGroupAsync(groupId, ct);
        var version = g.Publish(Me, clock.LagosToday, clock.UtcNow);
        db.SchemeVersions.Add(version);
        audit.Write(Me, "group.published", "group", g.Id, g.Id, new { schemeVersion = version.Version, schemeVersionId = version.Id });
        await db.SaveChangesAsync(ct);
    }

    public async Task CloseEnrolmentAsync(Guid groupId, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var g = await db.LockGroupAsync(groupId, ct);
        if (g is null || !g.IsOrganizer(Me)) throw new NotFoundException();
        var members = await db.GroupMemberships.Where(m => m.GroupId == g.Id && m.Status == MembershipStatus.Active).ToListAsync(ct);
        g.CloseEnrolment(members.Count, clock.UtcNow);
        await AssignJoinOrderIfNeededAsync(g, members, Me, ct);
        audit.Write(Me, "group.enrolment_closed", "group", g.Id, g.Id, new { members = members.Count });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>When enrolment closes under the JoinOrder method, positions follow join sequence automatically.</summary>
    /// <param name="actorId">The organizer when closing manually; null when the system closes enrolment because the group filled up.</param>
    internal async Task AssignJoinOrderIfNeededAsync(Group g, IReadOnlyCollection<GroupMembership> activeMembers, Guid? actorId, CancellationToken ct)
    {
        var rules = await CurrentRulesAsync(g, ct);
        if (rules.PayoutOrder != PayoutOrderMethod.JoinOrder) return;
        var order = PayoutOrderPlanner.JoinOrder(activeMembers);
        for (var i = 0; i < order.Count; i++)
            db.PayoutPositions.Add(PayoutPosition.Create(g.Id, order[i], i + 1, actorId ?? g.OrganizerId, clock.UtcNow));
        audit.Write(actorId, "group.payout_order_assigned", "group", g.Id, g.Id, new { method = "JoinOrder", automatic = actorId is null, order });
    }

    public async Task AssignPayoutOrderAsync(Guid groupId, AssignPayoutOrderRequest req, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var g = await db.LockGroupAsync(groupId, ct);
        if (g is null || !g.IsOrganizer(Me)) throw new NotFoundException();
        if (g.Status != GroupStatus.ReadyToStart)
            throw new DomainConflictException("group_not_ready", "Payout order can only be set after enrolment closes and before the group starts.");
        var rules = await CurrentRulesAsync(g, ct);
        if (rules.PayoutOrder != PayoutOrderMethod.OrganizerAssigned)
            throw new DomainConflictException("payout_order_fixed", "The accepted terms say payout order follows join order, so it cannot be reassigned.");

        var memberIds = await db.GroupMemberships.Where(m => m.GroupId == g.Id && m.Status == MembershipStatus.Active).Select(m => m.Id).ToListAsync(ct);
        PayoutOrderPlanner.ValidateAssignment(memberIds, req.MembershipIds ?? []);

        // Pre-start positions are configuration, not financial history; the audit log keeps the previous order.
        var existing = await db.PayoutPositions.Where(p => p.GroupId == g.Id).OrderBy(p => p.Position).ToListAsync(ct);
        var previous = existing.Select(p => p.MembershipId).ToList();
        db.PayoutPositions.RemoveRange(existing);
        await db.SaveChangesAsync(ct); // free unique (group, position) slots before re-inserting
        for (var i = 0; i < req.MembershipIds!.Count; i++)
            db.PayoutPositions.Add(PayoutPosition.Create(g.Id, req.MembershipIds[i], i + 1, Me, clock.UtcNow));
        audit.Write(Me, "group.payout_order_assigned", "group", g.Id, g.Id, new { method = "OrganizerAssigned", previous, order = req.MembershipIds });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Validates and activates the group, creating rounds, obligations and scheduled payouts in one transaction.</summary>
    public async Task StartAsync(Guid groupId, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var g = await db.LockGroupAsync(groupId, ct);
        if (g is null || !g.IsOrganizer(Me)) throw new NotFoundException();
        if (g.CurrentSchemeVersionId is null) throw new DomainConflictException("group_not_ready", "Publish the group before starting it.");
        var version = await db.SchemeVersions.SingleAsync(v => v.Id == g.CurrentSchemeVersionId, ct);
        var members = await db.GroupMemberships.Where(m => m.GroupId == g.Id && m.Status == MembershipStatus.Active).ToListAsync(ct);
        var positions = await db.PayoutPositions.Where(p => p.GroupId == g.Id).ToListAsync(ct);

        var result = RotatingActivation.Activate(g, version, members, positions, clock.LagosToday, clock.UtcNow);
        db.SchemeRounds.AddRange(result.Rounds);
        db.ContributionObligations.AddRange(result.Obligations);
        db.Payouts.AddRange(result.Payouts);
        audit.Write(Me, "group.started", "group", g.Id, g.Id,
            new { rounds = result.Rounds.Count, obligations = result.Obligations.Count, schemeVersion = version.Version });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task CancelAsync(Guid groupId, CancelGroupRequest req, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var g = await db.LockGroupAsync(groupId, ct);
        if (g is null || !g.IsOrganizer(Me)) throw new NotFoundException();
        g.Cancel(req.Reason, clock.UtcNow);
        // Outstanding invitations stop working immediately.
        var open = await db.GroupInvitations.Where(i => i.GroupId == g.Id && i.RevokedAt == null).ToListAsync(ct);
        foreach (var inv in open) inv.Revoke(Me, clock.UtcNow);
        audit.Write(Me, "group.cancelled", "group", g.Id, g.Id, new { reason = g.CancellationReason });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
