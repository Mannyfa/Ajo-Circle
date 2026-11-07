using AjoCircle.Application.Contracts;
using AjoCircle.Domain.Groups;
using AjoCircle.Domain.Notifications;
using AjoCircle.Domain.Records;
using Microsoft.EntityFrameworkCore;

namespace AjoCircle.Application;

/// <summary>
/// Disputes on organizer-reported records (brief §4.7). Only the member a record is about can raise one; only that
/// member and the group's organizer can see or reply to it. Other members never see it.
/// </summary>
public sealed class DisputeOptions
{
    public const string Section = "Disputes";
    /// <summary>How long the organizer has to respond before either side can escalate (owner decision 6).</summary>
    public int EscalationAfterDays { get; init; } = 7;
    public TimeSpan ResponseWindow => TimeSpan.FromDays(EscalationAfterDays);
}

public sealed class DisputeService(IAppDbContext db, GroupAccess access, IAppClock clock, AuditWriter audit, Notifier notifier,
    OperationsAccess ops, DisputeOptions options)
{
    public async Task<Guid> RaiseAsync(RecordKind kind, Guid recordId, RaiseDisputeRequest req, CancellationToken ct)
    {
        var groupId = await RecordGroupAsync(kind, recordId, ct) ?? throw new NotFoundException();
        await using var tx = await db.BeginTransactionAsync(ct);
        var v = await access.ViewAsync(groupId, ct, lockGroup: true);
        // Records keep mattering after a group ends: releases and refunds are paid after completion or cancellation.
        if (v.Group.Status is not (GroupStatus.Active or GroupStatus.Completed or GroupStatus.Cancelled))
            throw new Domain.Common.DomainConflictException("group_not_active", "Disputes can only be raised once the group has started.");

        ExternalRecord record = kind == RecordKind.ContributionRecord
            ? await db.ContributionRecords.SingleAsync(r => r.Id == recordId, ct)
            : await db.PayoutRecords.SingleAsync(r => r.Id == recordId, ct);
        RecordService.EnsureSubject(v, record);

        var upheld = await db.Disputes.AnyAsync(d => d.RecordId == recordId && d.Resolution == DisputeResolution.UpheldByOperations, ct);
        var dispute = Dispute.Open(record, kind, access.Me, req.Category, req.Description, clock.UtcNow, upheld);
        db.Disputes.Add(dispute);
        await SyncParentAsync(record, ct);
        audit.Write(access.Me, "dispute.opened", "dispute", dispute.Id, groupId,
            new { dispute.Reference, recordType = RecordService.RecordKindName(kind), recordId, category = dispute.Category.ToString() }); // description stays private to the thread
        notifier.Add(v.Group.OrganizerId, NotificationType.DisputeRaised, groupId,
            $"{await notifier.NameOfAsync(access.Me, ct)} raised a dispute in {v.Group.Name}",
            $"{dispute.Reference}: {CategoryText(dispute.Category)}. Reply on the dispute, or correct the record if it's wrong.",
            $"/app/disputes/{dispute.Id}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return dispute.Id;
    }

    public async Task<IReadOnlyList<DisputeSummaryDto>> ListAsync(DisputeStatus? status, CancellationToken ct)
    {
        var me = access.Me;
        var q = from d in db.Disputes
                join g in db.Groups on d.GroupId equals g.Id
                where d.RaisedBy == me || g.OrganizerId == me
                select new { d, g };
        if (status is { } s) q = q.Where(x => x.d.Status == s);
        var rows = await q.OrderByDescending(x => x.d.RaisedAt).Take(200).AsNoTracking().ToListAsync(ct);
        var labels = await LabelsAsync(rows.Select(r => r.d).ToList(), ct);
        var names = await access.NamesAsync(rows.Select(r => (Guid?)r.d.RaisedBy), ct);
        return rows.Select(r => Summary(r.d, r.g, labels, names, me)).ToList();
    }

    public async Task<DisputeDetailDto> GetAsync(Guid disputeId, CancellationToken ct)
    {
        var (d, g, isOrganizer) = await VisibleAsync(disputeId, ct);
        return await DetailAsync(d, g, access.Me, isOrganizer, asOperations: false, ct);
    }

    private async Task<DisputeDetailDto> DetailAsync(Dispute d, Group g, Guid me, bool isOrganizer, bool asOperations, CancellationToken ct)
    {
        ExternalRecord record = d.RecordType == RecordKind.ContributionRecord
            ? await db.ContributionRecords.AsNoTracking().SingleAsync(r => r.Id == d.RecordId, ct)
            : await db.PayoutRecords.AsNoTracking().SingleAsync(r => r.Id == d.RecordId, ct);
        var messages = await db.DisputeMessages.AsNoTracking().Where(m => m.DisputeId == d.Id).OrderBy(m => m.CreatedAt).Take(500).ToListAsync(ct);
        var names = await access.NamesAsync(messages.Select(m => (Guid?)m.AuthorId)
            .Concat([d.RaisedBy, d.ResolvedBy, d.EscalatedBy, record.RecordedBy, record.ConfirmedBy, record.ReversedBy]), ct);
        var labels = await LabelsAsync([d], ct);

        var now = clock.UtcNow;
        var canEscalate = !asOperations && d.Status == DisputeStatus.Open && now >= d.EscalationAllowedFrom(options.ResponseWindow);
        var actions = new List<string>();
        if (asOperations)
        {
            if (d.Status == DisputeStatus.Escalated) actions.Add("reply");
            if (d.Status == DisputeStatus.Escalated && d.Decision is null) actions.Add("decide");
        }
        else if (d.Status != DisputeStatus.Resolved)
        {
            actions.Add("reply");
            if (canEscalate) actions.Add("escalate");
            if (d.RaisedBy == me) actions.Add("withdraw");
            var writable = record is PayoutRecord pr
                ? RecordRules.CanRecordPayout(g, await db.Payouts.AsNoTracking().SingleAsync(p => p.Id == pr.PayoutId, ct))
                : g.Status == GroupStatus.Active;
            if (isOrganizer && writable && record.IsActive) actions.Add("correct");
        }
        return new DisputeDetailDto(Summary(d, g, labels, names, me), d.Description, d.ResolutionNote,
            d.ResolvedBy is { } rb ? names.GetValueOrDefault(rb) : null, d.CorrectiveRecordId,
            RecordService.ToDto(record, names, d.Status != DisputeStatus.Resolved ? d.Id : null, []),
            messages.Select(m => new DisputeMessageDto(m.Id, names.GetValueOrDefault(m.AuthorId, "Unknown"), m.AuthorId == me,
                m.AuthorId == g.OrganizerId, m.Body, m.CreatedAt, IsOperations: m.AuthorId != g.OrganizerId && m.AuthorId != d.RaisedBy)).ToList(),
            actions,
            new EscalationInfoDto(canEscalate, d.EscalationAllowedFrom(options.ResponseWindow), d.EscalatedAt,
                d.EscalatedBy is { } eb ? names.GetValueOrDefault(eb) : null, d.Decision, d.DecisionNote, d.DecidedAt));
    }

    /// <summary>
    /// Either party hands the dispute to the operations team once the organizer has had the response window
    /// (owner decision 6). The thread stays visible to both parties.
    /// </summary>
    public async Task EscalateAsync(Guid disputeId, CancellationToken ct)
    {
        var (peek, g, _) = await VisibleAsync(disputeId, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockGroupAsync(peek.GroupId, ct);
        var d = await db.Disputes.SingleAsync(x => x.Id == disputeId, ct);
        var me = access.Me;
        d.Escalate(me, me == d.RaisedBy || me == g.OrganizerId, options.ResponseWindow, clock.UtcNow);
        audit.Write(me, "dispute.escalated", "dispute", d.Id, d.GroupId, new { d.Reference });
        var other = me == d.RaisedBy ? g.OrganizerId : d.RaisedBy;
        notifier.Add(other, NotificationType.DisputeEscalated, g.Id, $"Dispute {d.Reference} was escalated to the operations team",
            "An investigator will look at the record and the conversation, then decide whether the record stands or needs correcting. You can keep replying on the dispute.",
            $"/app/disputes/{d.Id}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ---------- Operations side ----------

    public async Task<IReadOnlyList<DisputeSummaryDto>> OperationsQueueAsync(DisputeStatus? status, CancellationToken ct)
    {
        var staff = await ops.OperatorAsync(ct);
        var q = from d in db.Disputes join g in db.Groups on d.GroupId equals g.Id where d.EscalatedAt != null select new { d, g };
        if (status is { } s) q = q.Where(x => x.d.Status == s);
        var rows = await q.OrderBy(x => x.d.EscalatedAt).Take(200).AsNoTracking().ToListAsync(ct);
        var labels = await LabelsAsync(rows.Select(r => r.d).ToList(), ct);
        var names = await access.NamesAsync(rows.Select(r => (Guid?)r.d.RaisedBy), ct);
        return rows.Select(r => Summary(r.d, r.g, labels, names, staff.Id)).ToList();
    }

    public async Task<DisputeDetailDto> OperationsDetailAsync(Guid disputeId, CancellationToken ct)
    {
        var (staff, d, g) = await OperationsCaseAsync(disputeId, ct);
        audit.Write(staff.Id, "operations.dispute_viewed", "dispute", d.Id, g.Id, new { d.Reference });
        await db.SaveChangesAsync(ct);
        return await DetailAsync(d, g, staff.Id, isOrganizer: false, asOperations: true, ct);
    }

    public async Task OperationsReplyAsync(Guid disputeId, DisputeMessageRequest req, CancellationToken ct)
    {
        var (staff, d, g) = await OperationsCaseAsync(disputeId, ct);
        if (d.Status != DisputeStatus.Escalated) throw new Domain.Common.DomainConflictException("dispute_not_escalated", "Operations can only reply on escalated disputes.");
        db.DisputeMessages.Add(DisputeMessage.Create(d, staff.Id, req.Body, clock.UtcNow));
        audit.Write(staff.Id, "operations.dispute_message_added", "dispute", d.Id, g.Id, new { d.Reference });
        foreach (var uid in new[] { d.RaisedBy, g.OrganizerId }.Distinct())
            notifier.Add(uid, NotificationType.DisputeReply, g.Id, $"The operations team replied on dispute {d.Reference}", Clip(req.Body), $"/app/disputes/{d.Id}");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The investigator decides the outcome only (owner decision 5); any correction is the organizer's to make.</summary>
    public async Task DecideAsync(Guid disputeId, OperationsDecisionRequest req, CancellationToken ct)
    {
        var (staff, peek, g) = await OperationsCaseAsync(disputeId, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockGroupAsync(peek.GroupId, ct);
        var d = await db.Disputes.SingleAsync(x => x.Id == disputeId, ct);
        ExternalRecord record = d.RecordType == RecordKind.ContributionRecord
            ? await db.ContributionRecords.SingleAsync(r => r.Id == d.RecordId, ct)
            : await db.PayoutRecords.SingleAsync(r => r.Id == d.RecordId, ct);
        d.Decide(staff.Id, req.Decision, req.Note, record, clock.UtcNow);
        await SyncParentAsync(record, ct);
        audit.Write(staff.Id, "operations.dispute_decided", "dispute", d.Id, g.Id, new { d.Reference, decision = req.Decision.ToString() });
        var (title, body) = req.Decision == OperationsDecision.RecordUpheld
            ? ($"Operations upheld the record in dispute {d.Reference}", $"The record stands as the organizer reported it, and the dispute is closed. Finding: “{Clip(d.DecisionNote!)}”")
            : ($"Operations found the record in dispute {d.Reference} needs correcting", $"The organizer needs to correct or reverse the record. Finding: “{Clip(d.DecisionNote!)}”");
        foreach (var uid in new[] { d.RaisedBy, g.OrganizerId }.Distinct())
            notifier.Add(uid, NotificationType.DisputeDecided, g.Id, title, body, $"/app/disputes/{d.Id}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<(Domain.Identity.User Staff, Dispute Dispute, Group Group)> OperationsCaseAsync(Guid disputeId, CancellationToken ct)
    {
        var staff = await ops.OperatorAsync(ct);
        var row = await (from d in db.Disputes join g in db.Groups on d.GroupId equals g.Id where d.Id == disputeId select new { d, g })
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (row is null || row.d.EscalatedAt is null) throw new NotFoundException(); // operations only see escalated cases
        await ops.EnsureNoConflictAsync(staff, row.g.Id, ct);
        return (staff, row.d, row.g);
    }

    private static string Clip(string s) => s.Length > 200 ? s[..199] + "…" : s;

    public async Task AddMessageAsync(Guid disputeId, DisputeMessageRequest req, CancellationToken ct)
    {
        var (d, g, _) = await VisibleAsync(disputeId, ct);
        var msg = DisputeMessage.Create(d, access.Me, req.Body, clock.UtcNow);
        db.DisputeMessages.Add(msg);
        audit.Write(access.Me, "dispute.message_added", "dispute", d.Id, g.Id, new { d.Reference });
        var other = access.Me == d.RaisedBy ? g.OrganizerId : d.RaisedBy;
        if (other != access.Me)
            notifier.Add(other, NotificationType.DisputeReply, g.Id, $"New reply on dispute {d.Reference}",
                $"{await notifier.NameOfAsync(access.Me, ct)}: “{(msg.Body.Length > 200 ? msg.Body[..199] + "…" : msg.Body)}”", $"/app/disputes/{d.Id}");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The member who raised the dispute accepts the record as it stands. The organizer cannot do this for them.</summary>
    public async Task WithdrawAsync(Guid disputeId, WithdrawDisputeRequest req, CancellationToken ct)
    {
        var (peek, _, _) = await VisibleAsync(disputeId, ct);
        if (peek.RaisedBy != access.Me) throw new ForbiddenException("Only the member who raised this dispute can withdraw it.");

        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockGroupAsync(peek.GroupId, ct);
        var d = await db.Disputes.SingleAsync(x => x.Id == disputeId, ct);
        ExternalRecord record = d.RecordType == RecordKind.ContributionRecord
            ? await db.ContributionRecords.SingleAsync(r => r.Id == d.RecordId, ct)
            : await db.PayoutRecords.SingleAsync(r => r.Id == d.RecordId, ct);
        d.Resolve(access.Me, DisputeResolution.WithdrawnByMember, req.Note, null, clock.UtcNow);
        record.AcceptAfterDispute(access.Me, clock.UtcNow);
        await SyncParentAsync(record, ct);
        audit.Write(access.Me, "dispute.resolved", "dispute", d.Id, d.GroupId, new { d.Reference, resolution = d.Resolution.ToString() });
        var organizerId = await db.Groups.Where(x => x.Id == d.GroupId).Select(x => x.OrganizerId).SingleAsync(ct);
        notifier.Add(organizerId, NotificationType.DisputeResolved, d.GroupId,
            $"{await notifier.NameOfAsync(access.Me, ct)} withdrew dispute {d.Reference}",
            "The member accepted the record, so it is now confirmed by them." + (d.ResolutionNote is null ? "" : $" Their note: “{d.ResolutionNote}”."),
            $"/app/disputes/{d.Id}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ---------- helpers ----------

    internal static string CategoryText(DisputeCategory c) => c switch
    {
        DisputeCategory.DidNotHappen => "says this payment didn't happen",
        DisputeCategory.WrongAmount => "says the amount is wrong",
        DisputeCategory.WrongDate => "says the date is wrong",
        _ => "reported a problem",
    };

    private async Task<(Dispute, Group, bool IsOrganizer)> VisibleAsync(Guid disputeId, CancellationToken ct)
    {
        var me = access.Me;
        var row = await (from d in db.Disputes join g in db.Groups on d.GroupId equals g.Id where d.Id == disputeId select new { d, g })
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (row is null || (row.d.RaisedBy != me && row.g.OrganizerId != me)) throw new NotFoundException();
        return (row.d, row.g, row.g.OrganizerId == me);
    }

    private async Task<Guid?> RecordGroupAsync(RecordKind kind, Guid recordId, CancellationToken ct) => kind == RecordKind.ContributionRecord
        ? await db.ContributionRecords.Where(r => r.Id == recordId).Select(r => (Guid?)r.GroupId).SingleOrDefaultAsync(ct)
        : await db.PayoutRecords.Where(r => r.Id == recordId).Select(r => (Guid?)r.GroupId).SingleOrDefaultAsync(ct);

    private async Task SyncParentAsync(ExternalRecord record, CancellationToken ct)
    {
        switch (record)
        {
            case ContributionRecord c:
                (await db.ContributionObligations.SingleAsync(o => o.Id == c.ObligationId, ct)).SyncWithRecord(c.Status);
                break;
            case PayoutRecord p:
                (await db.Payouts.SingleAsync(x => x.Id == p.PayoutId, ct)).SyncWithRecord(p.Status);
                break;
        }
    }

    /// <summary>"Round 2 contribution · Tunde" and the obligation/payout id, for each dispute.</summary>
    private async Task<Dictionary<Guid, (Guid SubjectId, string Label)>> LabelsAsync(List<Dispute> disputes, CancellationToken ct)
    {
        var cIds = disputes.Where(d => d.RecordType == RecordKind.ContributionRecord).Select(d => d.RecordId).ToList();
        var pIds = disputes.Where(d => d.RecordType == RecordKind.PayoutRecord).Select(d => d.RecordId).ToList();
        var contributions = await (from r in db.ContributionRecords
                                   join o in db.ContributionObligations on r.ObligationId equals o.Id
                                   join rd in db.SchemeRounds on o.RoundId equals rd.Id
                                   join m in db.GroupMemberships on o.MembershipId equals m.Id
                                   join u in db.Users on m.UserId equals u.Id
                                   where cIds.Contains(r.Id)
                                   select new { r.Id, SubjectId = o.Id, rd.RoundNumber, u.DisplayName }).ToListAsync(ct);
        var payouts = await (from r in db.PayoutRecords
                             join p in db.Payouts on r.PayoutId equals p.Id
                             join rd in db.SchemeRounds on p.RoundId equals (Guid?)rd.Id into rr
                             from rd in rr.DefaultIfEmpty()
                             join m in db.GroupMemberships on p.RecipientMembershipId equals m.Id
                             join u in db.Users on m.UserId equals u.Id
                             where pIds.Contains(r.Id)
                             select new { r.Id, SubjectId = p.Id, p.Kind, RoundNumber = (int?)rd.RoundNumber, u.DisplayName }).ToListAsync(ct);
        var byRecord = contributions.ToDictionary(x => x.Id, x => (x.SubjectId, $"Round {x.RoundNumber} contribution · {x.DisplayName}"));
        foreach (var x in payouts) byRecord[x.Id] = (x.SubjectId, $"{RecordService.Capitalize(RecordService.PayoutLabel(x.Kind, x.RoundNumber))} · {x.DisplayName}");
        return disputes.ToDictionary(d => d.Id, d => byRecord[d.RecordId]);
    }

    private static DisputeSummaryDto Summary(Dispute d, Group g, Dictionary<Guid, (Guid SubjectId, string Label)> labels, IReadOnlyDictionary<Guid, string> names, Guid me)
    {
        var (subjectId, label) = labels[d.Id];
        return new DisputeSummaryDto(d.Id, d.Reference, g.Id, g.Name, d.RecordType, d.RecordId, subjectId, label, d.Category, d.Status,
            d.Resolution, names.GetValueOrDefault(d.RaisedBy, "Unknown"), d.RaisedBy == me, d.RaisedAt, d.ResolvedAt);
    }
}
