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
public sealed class DisputeService(IAppDbContext db, GroupAccess access, IAppClock clock, AuditWriter audit, Notifier notifier)
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

        var dispute = Dispute.Open(record, kind, access.Me, req.Category, req.Description, clock.UtcNow);
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
        var me = access.Me;
        ExternalRecord record = d.RecordType == RecordKind.ContributionRecord
            ? await db.ContributionRecords.AsNoTracking().SingleAsync(r => r.Id == d.RecordId, ct)
            : await db.PayoutRecords.AsNoTracking().SingleAsync(r => r.Id == d.RecordId, ct);
        var messages = await db.DisputeMessages.AsNoTracking().Where(m => m.DisputeId == d.Id).OrderBy(m => m.CreatedAt).Take(500).ToListAsync(ct);
        var names = await access.NamesAsync(messages.Select(m => (Guid?)m.AuthorId)
            .Concat([d.RaisedBy, d.ResolvedBy, record.RecordedBy, record.ConfirmedBy, record.ReversedBy]), ct);
        var labels = await LabelsAsync([d], ct);

        var actions = new List<string>();
        if (d.Status == DisputeStatus.Open)
        {
            actions.Add("reply");
            if (d.RaisedBy == me) actions.Add("withdraw");
            var writable = record is PayoutRecord pr
                ? RecordRules.CanRecordPayout(g, await db.Payouts.AsNoTracking().SingleAsync(p => p.Id == pr.PayoutId, ct))
                : g.Status == GroupStatus.Active;
            if (isOrganizer && writable && record.IsActive) actions.Add("correct");
        }
        return new DisputeDetailDto(Summary(d, g, labels, names, me), d.Description, d.ResolutionNote,
            d.ResolvedBy is { } rb ? names.GetValueOrDefault(rb) : null, d.CorrectiveRecordId,
            RecordService.ToDto(record, names, d.Status == DisputeStatus.Open ? d.Id : null, []),
            messages.Select(m => new DisputeMessageDto(m.Id, names.GetValueOrDefault(m.AuthorId, "Unknown"), m.AuthorId == me,
                m.AuthorId == g.OrganizerId, m.Body, m.CreatedAt)).ToList(), actions);
    }

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
