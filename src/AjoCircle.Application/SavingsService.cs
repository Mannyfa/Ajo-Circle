using AjoCircle.Application.Contracts;
using AjoCircle.Domain.Common;
using AjoCircle.Domain.Groups;
using AjoCircle.Domain.Notifications;
using AjoCircle.Domain.Records;
using AjoCircle.Domain.Schemes;
using Microsoft.EntityFrameworkCore;

namespace AjoCircle.Application;

/// <summary>Read-side arithmetic for fixed-term groups. "Recorded" always means organizer records still standing.</summary>
public sealed class SavingsLedger(IAppDbContext db, IAppClock clock)
{
    private static readonly RecordStatus[] Standing = [RecordStatus.RecordedByOrganizer, RecordStatus.ConfirmedByMember];

    /// <summary>Recorded contributions per membership (records that are neither reversed nor disputed).</summary>
    public async Task<Dictionary<Guid, long>> RecordedByMemberAsync(Guid groupId, CancellationToken ct) =>
        await db.ContributionRecords.Where(r => r.GroupId == groupId && Standing.Contains(r.Status))
            .GroupBy(r => r.SubjectMembershipId).Select(x => new { x.Key, Total = x.Sum(r => r.AmountMinor) })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);

    /// <summary>Money can't be settled while anyone is disputing a record — the amounts might change.</summary>
    public async Task EnsureNoOpenDisputesAsync(Guid groupId, CancellationToken ct, Guid? onlyMembership = null)
    {
        var q = db.Disputes.Where(d => d.GroupId == groupId && d.Status != DisputeStatus.Resolved);
        if (onlyMembership is { } mid)
        {
            var records = db.ContributionRecords.Where(r => r.SubjectMembershipId == mid).Select(r => r.Id)
                .Concat(db.PayoutRecords.Where(r => r.SubjectMembershipId == mid).Select(r => r.Id));
            q = q.Where(d => records.Contains(d.RecordId));
        }
        if (await q.AnyAsync(ct))
            throw new DomainConflictException("disputes_open", onlyMembership is null
                ? "Resolve the open disputes in this group first; the amounts owed back depend on them."
                : "This member has an open dispute. Resolve it first; the refund depends on it.");
    }

    public async Task<SavingsProgressDto> ProgressAsync(Group g, FixedTermSchemeRules rules, GroupMembership? forMember, CancellationToken ct)
    {
        var today = clock.LagosToday;
        var obligations = db.ContributionObligations.Where(o => o.GroupId == g.Id && o.Status != ObligationStatus.Cancelled);
        var records = db.ContributionRecords.Where(r => r.GroupId == g.Id && Standing.Contains(r.Status));
        var owed = db.Payouts.Where(p => p.GroupId == g.Id && p.Kind != PayoutKind.RotatingPayout);
        var owedRecords = from r in db.PayoutRecords join p in owed on r.PayoutId equals p.Id where Standing.Contains(r.Status) select r;
        int members;
        if (forMember is not null)
        {
            var mid = forMember.Id;
            obligations = obligations.Where(o => o.MembershipId == mid);
            records = records.Where(r => r.SubjectMembershipId == mid);
            owed = owed.Where(p => p.RecipientMembershipId == mid);
            owedRecords = owedRecords.Where(r => r.SubjectMembershipId == mid);
            members = 1;
        }
        else members = await db.GroupMemberships.CountAsync(m => m.GroupId == g.Id && m.Status == MembershipStatus.Active, ct);

        var target = rules.Contribution.Times(rules.Periods).Times(members).AmountMinor;
        var scheduled = await obligations.SumAsync(o => (long?)o.AmountMinor, ct) ?? 0;
        var recorded = await records.SumAsync(r => (long?)r.AmountMinor, ct) ?? 0;
        var notYet = await obligations.Where(o => o.Status == ObligationStatus.Due && o.DueDate <= today).SumAsync(o => (long?)o.AmountMinor, ct) ?? 0;
        var owedBack = await owed.SumAsync(p => (long?)p.AmountMinor, ct) ?? 0;
        var owedBackRecorded = await owedRecords.SumAsync(r => (long?)r.AmountMinor, ct) ?? 0;
        MoneyDto M(long v) => new(v, g.Currency);
        return new SavingsProgressDto(forMember is not null, members, M(target), M(scheduled), M(recorded), M(notYet), M(owedBack), M(owedBackRecorded));
    }
}

/// <summary>
/// Fixed-term savings commands (brief §4.3, §7.3) under the owner's decisions: the organizer chooses the sharing rule
/// before anyone joins; leaving early or a cancellation returns what each member paid; the organizer holds the money,
/// so the app records what is owed back and the organizer records paying it.
/// </summary>
public sealed class SavingsService(IAppDbContext db, ICurrentUser currentUser, IAppClock clock, AuditWriter audit, Notifier notifier, SavingsLedger ledger,
    IdentityGate gate)
{
    private Guid Me => currentUser.UserId ?? throw new UnauthenticatedException();

    private async Task<(Group Group, FixedTermSchemeRules Rules)> LockedFixedTermAsync(Guid groupId, CancellationToken ct, bool organizerOnly = true)
    {
        var g = await db.LockGroupAsync(groupId, ct) ?? throw new NotFoundException();
        if (organizerOnly && !g.IsOrganizer(Me)) throw new NotFoundException();
        if (!g.IsFixedTerm) throw new DomainConflictException("scheme_mismatch", "This only applies to fixed-term savings groups.");
        var json = g.CurrentSchemeVersionId is { } vid
            ? await db.SchemeVersions.Where(v => v.Id == vid).Select(v => v.RulesJson).SingleAsync(ct)
            : g.DraftRulesJson!;
        return (g, SchemeJson.DeserializeFixedTerm(json));
    }

    public async Task<Guid> CreateAsync(CreateFixedTermGroupRequest req, CancellationToken ct)
    {
        var me = Me;
        await gate.RequireAsync(me, Domain.Identity.IdentityLevel.Organizer, ct);
        var g = Group.CreateFixedTermDraft(me, req.Name, req.Rules.ToDomain(), clock.UtcNow);
        db.Groups.Add(g);
        audit.Write(me, "group.created", "group", g.Id, g.Id, new { g.Name, scheme = g.SchemeType.ToString(), allocation = req.Rules.Allocation.ToString() });
        await db.SaveChangesAsync(ct);
        return g.Id;
    }

    public async Task UpdateDraftAsync(Guid groupId, CreateFixedTermGroupRequest req, CancellationToken ct)
    {
        var g = await db.Groups.SingleOrDefaultAsync(x => x.Id == groupId, ct);
        if (g is null || !g.IsOrganizer(Me)) throw new NotFoundException();
        g.UpdateFixedTermDraft(req.Name, req.Rules.ToDomain());
        audit.Write(Me, "group.draft_updated", "group", g.Id, g.Id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Organizer sets each member's share before the start, as the accepted terms say they will.</summary>
    public async Task AssignSharesAsync(Guid groupId, AssignSharesRequest req, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var (g, rules) = await LockedFixedTermAsync(groupId, ct);
        await gate.RequireAsync(Me, Domain.Identity.IdentityLevel.Organizer, ct);
        if (rules.Allocation != SavingsAllocation.OrganizerAssigned)
            throw new DomainConflictException("shares_fixed", "The accepted terms don't let the organizer assign shares.");
        if (g.Status != GroupStatus.ReadyToStart)
            throw new DomainConflictException("group_not_ready", "Shares can be set after enrolment closes and before the group starts.");

        var memberIds = await db.GroupMemberships.Where(m => m.GroupId == g.Id && m.Status == MembershipStatus.Active).Select(m => m.Id).ToListAsync(ct);
        var shares = (req.Shares ?? []).GroupBy(s => s.MembershipId).ToDictionary(x => x.Key, x => x.Sum(s => s.BasisPoints));
        if ((req.Shares ?? []).Count != shares.Count)
            throw new DomainValidationException(new Dictionary<string, string[]> { ["shares"] = ["A member appears more than once."] });
        AllocationShare.Validate(memberIds, shares);

        // Pre-start shares are configuration, not financial history; the audit log keeps the previous values.
        var existing = await db.AllocationShares.Where(s => s.GroupId == g.Id).ToListAsync(ct);
        var previous = existing.ToDictionary(s => s.MembershipId, s => s.BasisPoints);
        db.AllocationShares.RemoveRange(existing);
        await db.SaveChangesAsync(ct);
        foreach (var (mid, bp) in shares) db.AllocationShares.Add(AllocationShare.Create(g.Id, mid, bp, Me, clock.UtcNow));
        audit.Write(Me, "group.shares_assigned", "group", g.Id, g.Id, new { previous, shares });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// A member leaves early — by their own choice or recorded by the organizer. Their unpaid contributions are dropped and
    /// what they paid becomes a refund the organizer owes them (owner decision 2).
    /// </summary>
    public async Task ExitAsync(Guid groupId, Guid membershipId, ExitMemberRequest req, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var (g, _) = await LockedFixedTermAsync(groupId, ct, organizerOnly: false);
        var m = await db.GroupMemberships.SingleOrDefaultAsync(x => x.Id == membershipId && x.GroupId == g.Id, ct) ?? throw new NotFoundException();
        var me = Me;
        var self = m.UserId == me;
        if (!g.IsOrganizer(me) && !self)
        {
            var viewer = await db.GroupMemberships.AnyAsync(x => x.GroupId == g.Id && x.UserId == me && x.Status != MembershipStatus.Removed, ct);
            if (!viewer) throw new NotFoundException();
            throw new ForbiddenException("Only the organizer or the member themselves can record leaving.");
        }
        if (g.Status != GroupStatus.Active)
            throw new DomainConflictException("group_not_active", "Members can only leave early while the group is running.");
        await ledger.EnsureNoOpenDisputesAsync(g.Id, ct, m.Id);

        m.Exit(me, req.Reason, clock.UtcNow);
        var dropped = 0;
        foreach (var o in await db.ContributionObligations.Where(o => o.MembershipId == m.Id).ToListAsync(ct))
            if (o.CancelIfDue()) dropped++;

        var paid = (await ledger.RecordedByMemberAsync(g.Id, ct)).GetValueOrDefault(m.Id);
        Payout? refund = null;
        if (paid > 0)
        {
            refund = Payout.CreateOwedBack(g.Id, PayoutKind.ExitRefund, m.Id, Money.Ngn(paid), clock.LagosToday, clock.UtcNow);
            db.Payouts.Add(refund);
        }
        audit.Write(me, "membership.exited", "membership", m.Id, g.Id, new { bySelf = self, reason = m.ExitReason, droppedObligations = dropped, refundMinor = paid });

        var name = await notifier.NameOfAsync(m.UserId, ct);
        var owedText = paid > 0 ? $"{Notifier.Money(paid)} they paid is owed back to them by the organizer." : "Nothing was recorded as paid, so nothing is owed back.";
        if (!g.IsOrganizer(me))
            notifier.Add(g.OrganizerId, NotificationType.MemberExited, g.Id, $"{name} left {g.Name}",
                $"Reason: “{m.ExitReason}”. {owedText} Record the refund when you've paid it.",
                refund is null ? $"/app/groups/{g.Id}/members" : $"/app/groups/{g.Id}/payouts/{refund.Id}");
        if (!self)
            notifier.Add(m.UserId, NotificationType.MemberExited, g.Id, $"The organizer recorded that you left {g.Name}",
                $"Reason: “{m.ExitReason}”. " + (paid > 0 ? $"The {Notifier.Money(paid)} recorded as paid by you is owed back to you." : "Nothing was recorded as paid by you."),
                refund is null ? $"/app/groups/{g.Id}" : $"/app/groups/{g.Id}/payouts/{refund.Id}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Ends the term: the recorded total is divided among members still in the group by the accepted rule, and each
    /// member's release becomes money the organizer owes them. Contributions never recorded stay "Due" in the history.
    /// </summary>
    public async Task CompleteAsync(Guid groupId, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var (g, rules) = await LockedFixedTermAsync(groupId, ct);
        await gate.RequireAsync(Me, Domain.Identity.IdentityLevel.Organizer, ct);
        var lastDue = FixedTermCalculator.Summarize(rules).LastDueDate;
        if (g.Status == GroupStatus.Active && clock.LagosToday < lastDue)
            throw new DomainConflictException("term_not_finished", $"The term runs until the last contribution on {Notifier.Date(lastDue)}. Complete it after that date.");
        await ledger.EnsureNoOpenDisputesAsync(g.Id, ct);
        g.CompleteTerm(clock.UtcNow);

        var members = await db.GroupMemberships.Where(m => m.GroupId == g.Id && m.Status == MembershipStatus.Active).ToListAsync(ct);
        var recorded = await ledger.RecordedByMemberAsync(g.Id, ct);
        var shares = await db.AllocationShares.Where(s => s.GroupId == g.Id).ToDictionaryAsync(s => s.MembershipId, s => s.BasisPoints, ct);
        var release = FixedTermSettlement.Release(rules, members.Select(m => new MemberPaid(m.Id, m.JoinSequence, recorded.GetValueOrDefault(m.Id))).ToList(), shares);
        var releaseDate = FixedTermCalculator.Summarize(rules).ReleaseDate;
        var payoutDate = releaseDate < clock.LagosToday ? clock.LagosToday : releaseDate;

        foreach (var (mid, amount) in release)
        {
            var m = members.Single(x => x.Id == mid);
            Payout? p = null;
            if (amount > 0)
            {
                p = Payout.CreateOwedBack(g.Id, PayoutKind.Release, mid, Money.Ngn(amount), payoutDate, clock.UtcNow);
                db.Payouts.Add(p);
            }
            if (m.UserId != g.OrganizerId)
                notifier.Add(m.UserId, NotificationType.SavingsCompleted, g.Id, $"{g.Name} has finished its term",
                    amount > 0
                        ? $"Under the agreed rule, {Notifier.Money(amount)} is owed to you, due {Notifier.Date(payoutDate)}. The organizer holds the money and will record paying you."
                        : "Nothing was recorded as paid, so nothing is due to you.",
                    p is null ? $"/app/groups/{g.Id}" : $"/app/groups/{g.Id}/payouts/{p.Id}");
        }
        audit.Write(Me, "group.completed", "group", g.Id, g.Id,
            new { allocation = rules.Allocation.ToString(), totalMinor = release.Sum(r => r.AmountMinor), releases = release.Count(r => r.AmountMinor > 0) });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Called inside GroupService.CancelAsync for a running fixed-term group, before the status changes: drops unpaid
    /// contributions and makes what each member paid a refund the organizer owes them (owner decision 2).
    /// </summary>
    internal async Task<Dictionary<Guid, long>> SettleCancellationAsync(Group g, CancellationToken ct)
    {
        await ledger.EnsureNoOpenDisputesAsync(g.Id, ct);
        foreach (var o in await db.ContributionObligations.Where(o => o.GroupId == g.Id && o.Status == ObligationStatus.Due).ToListAsync(ct))
            o.CancelIfDue();
        var recorded = await ledger.RecordedByMemberAsync(g.Id, ct);
        var active = await db.GroupMemberships.Where(m => m.GroupId == g.Id && m.Status == MembershipStatus.Active).Select(m => m.Id).ToListAsync(ct);
        var refunds = new Dictionary<Guid, long>();
        foreach (var mid in active)
        {
            var paid = recorded.GetValueOrDefault(mid);
            if (paid <= 0) continue;
            db.Payouts.Add(Payout.CreateOwedBack(g.Id, PayoutKind.CancellationRefund, mid, Money.Ngn(paid), clock.LagosToday, clock.UtcNow));
            refunds[mid] = paid;
        }
        return refunds;
    }
}
