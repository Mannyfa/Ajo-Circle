using System.Text.Json;
using AjoCircle.Application.Contracts;
using AjoCircle.Domain.Common;
using AjoCircle.Domain.Groups;
using AjoCircle.Domain.Notifications;
using AjoCircle.Domain.Records;
using Microsoft.EntityFrameworkCore;

namespace AjoCircle.Application;

/// <summary>Who the caller is in a group. Resolved on the server for every request (brief §3.1).</summary>
public sealed record GroupViewer(Group Group, bool IsOrganizer, GroupMembership? Membership)
{
    public bool IsMember(Guid membershipId) => Membership?.Id == membershipId;
}

public sealed class GroupAccess(IAppDbContext db, ICurrentUser currentUser)
{
    public Guid Me => currentUser.UserId ?? throw new UnauthenticatedException();

    /// <summary>Organizer or active member; anyone else gets 404 so IDs can't be probed.</summary>
    public async Task<GroupViewer> ViewAsync(Guid groupId, CancellationToken ct, bool lockGroup = false)
    {
        var me = Me;
        var g = lockGroup ? await db.LockGroupAsync(groupId, ct) : await db.Groups.AsNoTracking().SingleOrDefaultAsync(x => x.Id == groupId, ct);
        if (g is null) throw new NotFoundException();
        var membership = await db.GroupMemberships.AsNoTracking()
            .SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == me && (m.Status == MembershipStatus.Active || m.Status == MembershipStatus.Exited), ct);
        if (!g.IsOrganizer(me) && membership is null) throw new NotFoundException();
        return new GroupViewer(g, g.IsOrganizer(me), membership);
    }

    public async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        return await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }
}

/// <summary>
/// Organizer-reported contribution and payout records, member confirmation and corrections (brief §4.5, §4.6).
/// The app never moves money: every write here is a statement about something that happened elsewhere.
/// All writes take the group row lock so concurrent submissions serialize; partial unique indexes back this up.
/// </summary>
public sealed class RecordService(IAppDbContext db, GroupAccess access, IAppClock clock, AuditWriter audit, Notifier notifier, IdentityGate gate)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------- Contributions ----------

    public async Task<ObligationDetailDto> ContributionAsync(Guid groupId, Guid obligationId, CancellationToken ct)
    {
        var v = await access.ViewAsync(groupId, ct);
        var o = await db.ContributionObligations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == obligationId && x.GroupId == groupId, ct);
        if (o is null || (!v.IsOrganizer && !v.IsMember(o.MembershipId))) throw new NotFoundException(); // members see only their own
        var round = await db.SchemeRounds.Where(r => r.Id == o.RoundId).Select(r => r.RoundNumber).SingleAsync(ct);
        var memberName = await MemberNameAsync(o.MembershipId, ct);
        var records = await db.ContributionRecords.AsNoTracking().Where(r => r.ObligationId == o.Id).OrderByDescending(r => r.RecordedAt).ToListAsync(ct);
        var disputes = await DisputesForAsync(records.Select(r => r.Id), ct);
        var isSubject = v.IsMember(o.MembershipId);
        var hasActive = records.Any(r => r.IsActive);
        var actions = v.IsOrganizer && v.Group.Status == GroupStatus.Active && !hasActive ? new[] { "record" } : [];

        return new ObligationDetailDto(o.Id, groupId, round, memberName, o.MembershipId, isSubject, new MoneyDto(o.AmountMinor, o.Currency),
            o.DueDate, o.Status, await ToDtosAsync(records, disputes, v, isSubject, v.Group.Status == GroupStatus.Active, ct),
            await SummariesAsync(disputes, v.Group, $"Round {round} contribution · {memberName}", o.Id, ct), actions);
    }

    public async Task<Guid> RecordContributionAsync(Guid groupId, Guid obligationId, RecordPaymentRequest req, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var v = await OrganizerLockedAsync(groupId, ct);
        var o = await db.ContributionObligations.SingleOrDefaultAsync(x => x.Id == obligationId && x.GroupId == groupId, ct) ?? throw new NotFoundException();
        var hasActive = await db.ContributionRecords.AnyAsync(r => r.ObligationId == o.Id && r.Status != RecordStatus.Reversed, ct);
        var record = ContributionRecord.Create(v.Group, o, hasActive, req.ToDomain(), access.Me, clock.LagosToday, clock.UtcNow);
        db.ContributionRecords.Add(record);
        o.SyncWithRecord(record.Status);
        audit.Write(access.Me, "contribution.recorded", "contribution_record", record.Id, groupId,
            new { obligationId = o.Id, record.AmountMinor, record.PaidOn, method = record.Method.ToString() }); // evidence reference stays out of the log
        var round = await RoundOfObligationAsync(o.Id, ct);
        await NotifySubjectAsync(v, record, NotificationType.ContributionRecorded, $"/app/groups/{groupId}/contributions/{o.Id}",
            org => $"{org} recorded a payment for your round {round} contribution in {v.Group.Name}",
            $"Recorded by organizer: {Notifier.Money(record.AmountMinor)} paid {Notifier.Date(record.PaidOn)}. Please confirm it, or report a problem if it's wrong. This is the organizer's report, not a bank confirmation.", ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return record.Id;
    }

    public async Task<Guid> ConfirmContributionAsync(Guid recordId, CancellationToken ct)
    {
        var groupId = await db.ContributionRecords.Where(r => r.Id == recordId).Select(r => (Guid?)r.GroupId).SingleOrDefaultAsync(ct) ?? throw new NotFoundException();
        await using var tx = await db.BeginTransactionAsync(ct);
        var v = await access.ViewAsync(groupId, ct, lockGroup: true);
        var record = await db.ContributionRecords.SingleAsync(r => r.Id == recordId, ct);
        EnsureSubject(v, record);
        var o = await db.ContributionObligations.SingleAsync(x => x.Id == record.ObligationId, ct);
        if (record.Status != RecordStatus.ConfirmedByMember)
        {
            record.Confirm(access.Me, clock.UtcNow);
            o.SyncWithRecord(record.Status);
            audit.Write(access.Me, "contribution.confirmed", "contribution_record", record.Id, groupId, new { obligationId = o.Id });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return o.Id;
    }

    public async Task<Guid> CorrectContributionAsync(Guid recordId, CorrectRecordRequest req, CancellationToken ct)
    {
        var groupId = await db.ContributionRecords.Where(r => r.Id == recordId).Select(r => (Guid?)r.GroupId).SingleOrDefaultAsync(ct) ?? throw new NotFoundException();
        await using var tx = await db.BeginTransactionAsync(ct);
        var v = await OrganizerLockedAsync(groupId, ct);
        var record = await db.ContributionRecords.SingleAsync(r => r.Id == recordId, ct);
        var o = await db.ContributionObligations.SingleAsync(x => x.Id == record.ObligationId, ct);
        var (replacement, dispute) = await CorrectAsync(v, record, RecordKind.ContributionRecord, req,
            () => ContributionRecord.Create(v.Group, o, false, req.Replacement!.ToDomain(), access.Me, clock.LagosToday, clock.UtcNow, replaces: record.Id),
            r => db.ContributionRecords.Add(r), o.SyncWithRecord, "contribution", ct);
        await NotifyCorrectionAsync(v, record, replacement, dispute, NotificationType.ContributionCorrected,
            $"/app/groups/{groupId}/contributions/{o.Id}", $"round {await RoundOfObligationAsync(o.Id, ct)} contribution", ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return o.Id;
    }

    public async Task<Guid> GroupOfObligationAsync(Guid obligationId, CancellationToken ct) =>
        await db.ContributionObligations.Where(o => o.Id == obligationId).Select(o => (Guid?)o.GroupId).SingleOrDefaultAsync(ct) ?? throw new NotFoundException();

    // ---------- Payouts ----------

    public async Task<PayoutDetailDto> PayoutAsync(Guid groupId, Guid payoutId, CancellationToken ct)
    {
        var v = await access.ViewAsync(groupId, ct);
        var p = await db.Payouts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == payoutId && x.GroupId == groupId, ct);
        // Evidence for someone else's payout is private: only the organizer and the recipient open the detail.
        if (p is null || (!v.IsOrganizer && !v.IsMember(p.RecipientMembershipId))) throw new NotFoundException();
        var round = await RoundOfPayoutAsync(p, ct);
        var name = await MemberNameAsync(p.RecipientMembershipId, ct);
        var records = await db.PayoutRecords.AsNoTracking().Where(r => r.PayoutId == p.Id).OrderByDescending(r => r.RecordedAt).ToListAsync(ct);
        var disputes = await DisputesForAsync(records.Select(r => r.Id), ct);
        var isSubject = v.IsMember(p.RecipientMembershipId);
        var writable = RecordRules.CanRecordPayout(v.Group, p);
        var actions = v.IsOrganizer && writable && !records.Any(r => r.IsActive) ? new[] { "record" } : [];

        return new PayoutDetailDto(p.Id, groupId, p.Kind, round, name, p.RecipientMembershipId, isSubject, new MoneyDto(p.AmountMinor, p.Currency),
            p.ScheduledDate, p.Status, await ToDtosAsync(records, disputes, v, isSubject, writable, ct),
            await SummariesAsync(disputes, v.Group, $"{Capitalize(PayoutLabel(p.Kind, round))} · {name}", p.Id, ct), actions);
    }

    public async Task<Guid> RecordPayoutAsync(Guid groupId, Guid payoutId, RecordPaymentRequest req, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var v = await OrganizerLockedAsync(groupId, ct);
        var p = await db.Payouts.SingleOrDefaultAsync(x => x.Id == payoutId && x.GroupId == groupId, ct) ?? throw new NotFoundException();
        var hasActive = await db.PayoutRecords.AnyAsync(r => r.PayoutId == p.Id && r.Status != RecordStatus.Reversed, ct);
        var record = PayoutRecord.Create(v.Group, p, hasActive, req.ToDomain(), access.Me, clock.LagosToday, clock.UtcNow);
        db.PayoutRecords.Add(record);
        p.SyncWithRecord(record.Status);
        audit.Write(access.Me, "payout.recorded", "payout_record", record.Id, groupId,
            new { payoutId = p.Id, record.AmountMinor, scheduledAmountMinor = p.AmountMinor, record.PaidOn, method = record.Method.ToString() });
        var label = PayoutLabel(p.Kind, await RoundOfPayoutAsync(p, ct));
        await NotifySubjectAsync(v, record, NotificationType.PayoutRecorded, $"/app/groups/{groupId}/payouts/{p.Id}",
            org => $"{org} recorded paying your {label} from {v.Group.Name}",
            $"Recorded by organizer: {Notifier.Money(record.AmountMinor)} paid to you {Notifier.Date(record.PaidOn)}. Please confirm you received it, or report a problem.", ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return record.Id;
    }

    public async Task<Guid> ConfirmPayoutAsync(Guid recordId, CancellationToken ct)
    {
        var groupId = await db.PayoutRecords.Where(r => r.Id == recordId).Select(r => (Guid?)r.GroupId).SingleOrDefaultAsync(ct) ?? throw new NotFoundException();
        await using var tx = await db.BeginTransactionAsync(ct);
        var v = await access.ViewAsync(groupId, ct, lockGroup: true);
        var record = await db.PayoutRecords.SingleAsync(r => r.Id == recordId, ct);
        EnsureSubject(v, record);
        var p = await db.Payouts.SingleAsync(x => x.Id == record.PayoutId, ct);
        if (record.Status != RecordStatus.ConfirmedByMember)
        {
            record.Confirm(access.Me, clock.UtcNow);
            p.SyncWithRecord(record.Status);
            audit.Write(access.Me, "payout.confirmed", "payout_record", record.Id, groupId, new { payoutId = p.Id });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return p.Id;
    }

    public async Task<Guid> CorrectPayoutAsync(Guid recordId, CorrectRecordRequest req, CancellationToken ct)
    {
        var groupId = await db.PayoutRecords.Where(r => r.Id == recordId).Select(r => (Guid?)r.GroupId).SingleOrDefaultAsync(ct) ?? throw new NotFoundException();
        await using var tx = await db.BeginTransactionAsync(ct);
        var v = await OrganizerLockedAsync(groupId, ct);
        var record = await db.PayoutRecords.SingleAsync(r => r.Id == recordId, ct);
        var p = await db.Payouts.SingleAsync(x => x.Id == record.PayoutId, ct);
        var (replacement, dispute) = await CorrectAsync(v, record, RecordKind.PayoutRecord, req,
            () => PayoutRecord.Create(v.Group, p, false, req.Replacement!.ToDomain(), access.Me, clock.LagosToday, clock.UtcNow, replaces: record.Id),
            r => db.PayoutRecords.Add(r), p.SyncWithRecord, "payout", ct);
        await NotifyCorrectionAsync(v, record, replacement, dispute, NotificationType.PayoutCorrected,
            $"/app/groups/{groupId}/payouts/{p.Id}", PayoutLabel(p.Kind, await RoundOfPayoutAsync(p, ct)), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return p.Id;
    }

    // ---------- Shared ----------

    private async Task<GroupViewer> OrganizerLockedAsync(Guid groupId, CancellationToken ct)
    {
        var v = await access.ViewAsync(groupId, ct, lockGroup: true);
        if (!v.IsOrganizer) throw new ForbiddenException("Only the organizer can record or correct payments.");
        await gate.RequireAsync(access.Me, Domain.Identity.IdentityLevel.Organizer, ct);
        return v;
    }

    /// <summary>Only the member a record is about can confirm or dispute it.</summary>
    internal static void EnsureSubject(GroupViewer v, ExternalRecord record)
    {
        if (v.IsMember(record.SubjectMembershipId)) return;
        throw new ForbiddenException("Only the member this record is about can confirm or dispute it.");
    }

    /// <summary>
    /// Reverse-and-replace: the original keeps its values and is marked Reversed with a reason; a replacement (if any) is a
    /// new record linked back to it; an amendment row ties them together. An open dispute on the original is resolved
    /// in the member's favour, because the organizer changed the record.
    /// </summary>
    private async Task<(TRecord? Replacement, Dispute? ResolvedDispute)> CorrectAsync<TRecord>(GroupViewer v, TRecord record, RecordKind kind, CorrectRecordRequest req,
        Func<TRecord> createReplacement, Action<TRecord> add, Action<RecordStatus?> sync, string auditPrefix, CancellationToken ct)
        where TRecord : ExternalRecord
    {
        if (record is PayoutRecord pr) RecordRules.EnsureCanRecordPayout(v.Group, await db.Payouts.SingleAsync(x => x.Id == pr.PayoutId, ct));
        else RecordRules.EnsureGroupAcceptsRecords(v.Group);
        var oldValues = JsonSerializer.Serialize(record.Snapshot(), Json);
        record.Reverse(access.Me, req.Reason, clock.UtcNow);
        sync(null);
        await db.SaveChangesAsync(ct); // free the "one active record" slot before inserting the replacement

        TRecord? replacement = null;
        if (req.Replacement is not null)
        {
            replacement = createReplacement();
            add(replacement);
            sync(replacement.Status);
        }

        db.RecordAmendments.Add(RecordAmendment.Create(v.Group.Id, kind, record.Id, replacement?.Id, req.Reason, oldValues,
            replacement is null ? null : JsonSerializer.Serialize(replacement.Snapshot(), Json), access.Me, clock.UtcNow));

        var open = await db.Disputes.SingleOrDefaultAsync(d => d.RecordId == record.Id && d.Status != DisputeStatus.Resolved, ct);
        if (open is not null)
        {
            open.Resolve(access.Me, replacement is null ? DisputeResolution.RecordReversed : DisputeResolution.RecordCorrected,
                req.Reason, replacement?.Id, clock.UtcNow);
            audit.Write(access.Me, "dispute.resolved", "dispute", open.Id, v.Group.Id, new { open.Reference, resolution = open.Resolution.ToString() });
        }

        audit.Write(access.Me, replacement is null ? $"{auditPrefix}.reversed" : $"{auditPrefix}.corrected", RecordKindName(kind), record.Id, v.Group.Id,
            new { replacementRecordId = replacement?.Id, reason = record.ReversalReason, previousAmountMinor = record.AmountMinor, newAmountMinor = replacement?.AmountMinor });
        await db.SaveChangesAsync(ct);
        return (replacement, open);
    }

    private Task<int> RoundOfObligationAsync(Guid obligationId, CancellationToken ct) =>
        (from o in db.ContributionObligations join r in db.SchemeRounds on o.RoundId equals r.Id where o.Id == obligationId select r.RoundNumber).SingleAsync(ct);

    private async Task<int?> RoundOfPayoutAsync(Payout p, CancellationToken ct) => p.RoundId is { } rid
        ? await db.SchemeRounds.Where(r => r.Id == rid).Select(r => r.RoundNumber).SingleAsync(ct)
        : null;

    /// <summary>"round 2 payout", "savings release", "refund for leaving early", "cancellation refund".</summary>
    internal static string PayoutLabel(PayoutKind kind, int? round) => kind switch
    {
        PayoutKind.RotatingPayout => $"round {round} payout",
        PayoutKind.Release => "savings release",
        PayoutKind.ExitRefund => "refund for leaving early",
        PayoutKind.CancellationRefund => "cancellation refund",
        _ => "payout",
    };

    internal static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Tells the member a record is about. Nobody is notified about their own action.</summary>
    private async Task NotifySubjectAsync(GroupViewer v, ExternalRecord record, NotificationType type, string link,
        Func<string, string> title, string body, CancellationToken ct)
    {
        var subject = await notifier.UserOfMembershipAsync(record.SubjectMembershipId, ct);
        if (subject == access.Me) return;
        notifier.Add(subject, type, v.Group.Id, title(await notifier.NameOfAsync(access.Me, ct)), body, link);
    }

    private Task NotifyCorrectionAsync(GroupViewer v, ExternalRecord original, ExternalRecord? replacement, Dispute? resolved,
        NotificationType type, string link, string label, CancellationToken ct)
    {
        var what = replacement is null ? "reversed" : "corrected";
        var body = (replacement is null
                ? $"The record of {Notifier.Money(original.AmountMinor)} was reversed, so nothing is recorded for this {label} now."
                : $"The old record was reversed and replaced: {Notifier.Money(replacement.AmountMinor)} paid {Notifier.Date(replacement.PaidOn)}. Please check and confirm it.")
            + $" Reason: “{original.ReversalReason}”."
            + (resolved is null ? "" : $" Your dispute {resolved.Reference} is now resolved.");
        return NotifySubjectAsync(v, original, type, link, org => $"{org} {what} the record for your {label} in {v.Group.Name}", body, ct);
    }

    internal static string RecordKindName(RecordKind k) => k == RecordKind.ContributionRecord ? "contribution_record" : "payout_record";

    private Task<string> MemberNameAsync(Guid membershipId, CancellationToken ct) =>
        (from m in db.GroupMemberships join u in db.Users on m.UserId equals u.Id where m.Id == membershipId select u.DisplayName).SingleAsync(ct);

    private Task<List<Dispute>> DisputesForAsync(IEnumerable<Guid> recordIds, CancellationToken ct)
    {
        var ids = recordIds.ToList();
        return db.Disputes.AsNoTracking().Where(d => ids.Contains(d.RecordId)).OrderByDescending(d => d.RaisedAt).ToListAsync(ct);
    }

    private async Task<IReadOnlyList<RecordDto>> ToDtosAsync<TRecord>(List<TRecord> records, List<Dispute> disputes, GroupViewer v, bool isSubject,
        bool writable, CancellationToken ct)
        where TRecord : ExternalRecord
    {
        var names = await access.NamesAsync(records.SelectMany(r => new[] { (Guid?)r.RecordedBy, r.ConfirmedBy, r.ReversedBy }), ct);
        return records.Select(r =>
        {
            var open = disputes.FirstOrDefault(d => d.RecordId == r.Id && d.Status != DisputeStatus.Resolved);
            var actions = new List<string>();
            if (v.IsOrganizer && writable && r.IsActive) actions.Add("correct");
            var mine = r.IsOwnRecord(access.Me); // an organizer who is also the subject can't confirm or dispute their own entry
            if (isSubject && !mine && r.Status == RecordStatus.RecordedByOrganizer) actions.Add("confirm");
            if (isSubject && !mine && r.Status is RecordStatus.RecordedByOrganizer or RecordStatus.ConfirmedByMember) actions.Add("dispute");
            return ToDto(r, names, open?.Id, actions);
        }).ToList();
    }

    internal static RecordDto ToDto(ExternalRecord r, IReadOnlyDictionary<Guid, string> names, Guid? openDisputeId, IReadOnlyList<string> actions) => new(
        r.Id, new MoneyDto(r.AmountMinor, r.Currency), r.PaidOn, r.Method, r.EvidenceReference, r.Note, r.Status,
        names.GetValueOrDefault(r.RecordedBy, "Unknown"), r.RecordedAt,
        r.ConfirmedBy is { } c ? names.GetValueOrDefault(c) : null, r.ConfirmedAt,
        r.ReversedBy is { } rb ? names.GetValueOrDefault(rb) : null, r.ReversedAt, r.ReversalReason, r.ReplacesRecordId, openDisputeId, actions);

    private async Task<IReadOnlyList<DisputeSummaryDto>> SummariesAsync(List<Dispute> disputes, Group g, string label, Guid subjectId, CancellationToken ct)
    {
        var names = await access.NamesAsync(disputes.Select(d => (Guid?)d.RaisedBy), ct);
        var me = access.Me;
        return disputes.Select(d => new DisputeSummaryDto(d.Id, d.Reference, g.Id, g.Name, d.RecordType, d.RecordId, subjectId, label, d.Category,
            d.Status, d.Resolution, names.GetValueOrDefault(d.RaisedBy, "Unknown"), d.RaisedBy == me, d.RaisedAt, d.ResolvedAt)).ToList();
    }
}
