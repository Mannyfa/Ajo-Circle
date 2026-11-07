using AjoCircle.Domain.Common;
using AjoCircle.Domain.Groups;

namespace AjoCircle.Domain.Records;

/// <summary>How money moved outside the app. Free-text "how" belongs in the note, never bank credentials.</summary>
public enum PaymentMethod { Cash, BankTransfer, PosOrMobileMoney, Other }

/// <summary>
/// Status of an organizer-reported record (brief §6.2). Deliberately has no "verified" value: a manual entry can
/// never become provider-verified. <see cref="ConfirmedByMember"/> on a payout record means the recipient confirmed it.
/// </summary>
public enum RecordStatus { RecordedByOrganizer, ConfirmedByMember, Disputed, Reversed }

/// <summary>What the organizer says happened outside the app.</summary>
public sealed record ReportedPayment(long AmountMinor, DateOnly PaidOn, PaymentMethod Method, string? EvidenceReference, string? Note)
{
    public const int MaxEvidenceLength = 120;
    public const int MaxNoteLength = 500;
    public const int MaxDaysInPast = 366;

    /// <summary>Trims text and checks the fields that don't depend on what is owed.</summary>
    public ReportedPayment Normalize(DateOnly today)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string k, string m) { if (!errors.TryGetValue(k, out var l)) errors[k] = l = []; l.Add(m); }

        var evidence = string.IsNullOrWhiteSpace(EvidenceReference) ? null : EvidenceReference.Trim();
        var note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();
        if (AmountMinor <= 0) Add("amountMinor", "Enter an amount greater than zero.");
        if (AmountMinor % Currencies.NgnMinorUnitsPerMajor != 0) Add("amountMinor", "Use whole naira amounts.");
        if (PaidOn > today) Add("paidOn", "The payment date can't be in the future.");
        if (PaidOn < today.AddDays(-MaxDaysInPast)) Add("paidOn", "The payment date is more than a year ago.");
        if (!Enum.IsDefined(Method)) Add("method", "Choose how the money was paid.");
        if (evidence?.Length > MaxEvidenceLength) Add("evidenceReference", $"Keep the reference under {MaxEvidenceLength} characters.");
        if (note?.Length > MaxNoteLength) Add("note", $"Keep the note under {MaxNoteLength} characters.");
        DomainValidationException.ThrowIfAny(errors);
        return this with { EvidenceReference = evidence, Note = note };
    }
}

/// <summary>
/// Shared shape of an organizer-reported record. Amount, date, method, evidence and authorship are immutable once
/// saved; only the status can move, and a reversed record is frozen. A database trigger enforces the same.
/// Corrections reverse the original and add a new linked record (brief §4.5, §11.4).
/// </summary>
public abstract class ExternalRecord
{
    public Guid Id { get; protected set; }
    public Guid GroupId { get; protected set; }
    /// <summary>The member the record is about: who paid (contribution) or who received (payout).</summary>
    public Guid SubjectMembershipId { get; protected set; }
    public long AmountMinor { get; protected set; }
    public string Currency { get; protected set; } = Currencies.Ngn;
    public DateOnly PaidOn { get; protected set; }
    public PaymentMethod Method { get; protected set; }
    public string? EvidenceReference { get; protected set; }
    public string? Note { get; protected set; }
    public Guid RecordedBy { get; protected set; }
    public DateTimeOffset RecordedAt { get; protected set; }
    /// <summary>The reversed record this one corrects, if any.</summary>
    public Guid? ReplacesRecordId { get; protected set; }

    public RecordStatus Status { get; protected set; }
    public Guid? ConfirmedBy { get; protected set; }
    public DateTimeOffset? ConfirmedAt { get; protected set; }
    public Guid? ReversedBy { get; protected set; }
    public DateTimeOffset? ReversedAt { get; protected set; }
    public string? ReversalReason { get; protected set; }

    public bool IsActive => Status != RecordStatus.Reversed;

    protected void Init(Guid groupId, Guid subjectMembershipId, ReportedPayment p, Guid recordedBy, Guid? replaces, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        GroupId = groupId;
        SubjectMembershipId = subjectMembershipId;
        AmountMinor = p.AmountMinor;
        Currency = Currencies.Ngn;
        PaidOn = p.PaidOn;
        Method = p.Method;
        EvidenceReference = p.EvidenceReference;
        Note = p.Note;
        RecordedBy = recordedBy;
        RecordedAt = now;
        ReplacesRecordId = replaces;
        Status = RecordStatus.RecordedByOrganizer;
    }

    /// <summary>
    /// Confirmation is a second person agreeing. When the organizer is also the member a record is about, they can't
    /// confirm (or dispute) their own entry, so it stays "Recorded by organizer".
    /// </summary>
    public bool IsOwnRecord(Guid userId) => RecordedBy == userId;

    /// <summary>The subject member agrees the record is right. Not proof of bank settlement.</summary>
    public void Confirm(Guid userId, DateTimeOffset now)
    {
        if (IsOwnRecord(userId))
            throw new DomainConflictException("own_record", "You recorded this yourself, so you can't confirm it. It stays marked as recorded by the organizer.");
        switch (Status)
        {
            case RecordStatus.ConfirmedByMember: return; // idempotent
            case RecordStatus.Disputed:
                throw new DomainConflictException("record_disputed", "This record has an open dispute. Resolve the dispute instead.");
            case RecordStatus.Reversed:
                throw new DomainConflictException("record_reversed", "This record was reversed and can't be confirmed.");
        }
        Status = RecordStatus.ConfirmedByMember;
        ConfirmedBy = userId;
        ConfirmedAt = now;
    }

    public void MarkDisputed(Guid raisedBy)
    {
        if (IsOwnRecord(raisedBy))
            throw new DomainConflictException("own_record", "You recorded this yourself. Correct the record instead of disputing it.");
        if (Status == RecordStatus.Reversed)
            throw new DomainConflictException("record_reversed", "This record was reversed. Dispute the current record instead.");
        if (Status == RecordStatus.Disputed)
            throw new DomainConflictException("dispute_already_open", "There is already an open dispute on this record.");
        Status = RecordStatus.Disputed;
    }

    /// <summary>The member who disputed the record withdrew the dispute, accepting the record as it stands.</summary>
    public void AcceptAfterDispute(Guid userId, DateTimeOffset now)
    {
        if (Status != RecordStatus.Disputed)
            throw new DomainConflictException("record_not_disputed", "This record is not under dispute.");
        Status = RecordStatus.ConfirmedByMember;
        ConfirmedBy = userId;
        ConfirmedAt = now;
    }

    public void Reverse(Guid actorId, string reason, DateTimeOffset now)
    {
        if (Status == RecordStatus.Reversed)
            throw new DomainConflictException("record_reversed", "This record has already been reversed.");
        ReversedBy = actorId;
        ReversedAt = now;
        ReversalReason = RecordRules.Reason(reason);
        Status = RecordStatus.Reversed;
    }

    /// <summary>A snapshot of the immutable values, kept in the amendment trail.</summary>
    public object Snapshot() => new { AmountMinor, Currency, PaidOn, Method = Method.ToString(), EvidenceReference, Note, Status = Status.ToString() };
}

public sealed class ContributionRecord : ExternalRecord
{
    private ContributionRecord() { }
    public Guid ObligationId { get; private set; }

    /// <summary>
    /// Records a payment made outside the app against one obligation. MVP rule: the amount must equal the amount
    /// owed — partial and over-payments are an open product decision (see README).
    /// </summary>
    public static ContributionRecord Create(Group group, ContributionObligation obligation, bool hasActiveRecord, ReportedPayment payment,
        Guid recordedBy, DateOnly today, DateTimeOffset now, Guid? replaces = null)
    {
        RecordRules.EnsureGroupAcceptsRecords(group);
        if (obligation.GroupId != group.Id) throw new DomainException("obligation_mismatch", "That contribution belongs to another group.");
        if (obligation.Status == ObligationStatus.Cancelled)
            throw new DomainConflictException("obligation_cancelled", "This contribution is no longer owed, so a payment can't be recorded against it.");
        if (hasActiveRecord)
            throw new DomainConflictException("already_recorded", "A payment is already recorded for this contribution. Correct that record instead.");
        var p = payment.Normalize(today);
        if (p.AmountMinor != obligation.AmountMinor)
            throw new DomainValidationException(new Dictionary<string, string[]>
            {
                ["amountMinor"] = ["The amount must match the contribution owed. Part payments aren't supported yet."],
            });
        var r = new ContributionRecord { ObligationId = obligation.Id };
        r.Init(group.Id, obligation.MembershipId, p, recordedBy, replaces, now);
        return r;
    }
}

public sealed class PayoutRecord : ExternalRecord
{
    private PayoutRecord() { }
    public Guid PayoutId { get; private set; }

    /// <summary>
    /// Records what the organizer says was paid to the recipient outside the app. The app never sends money, and a
    /// payout date arriving does not make anything eligible or paid (brief §4.6, §13.1).
    /// </summary>
    public static PayoutRecord Create(Group group, Payout payout, bool hasActiveRecord, ReportedPayment payment,
        Guid recordedBy, DateOnly today, DateTimeOffset now, Guid? replaces = null)
    {
        RecordRules.EnsureCanRecordPayout(group, payout);
        if (payout.GroupId != group.Id) throw new DomainException("payout_mismatch", "That payout belongs to another group.");
        if (hasActiveRecord)
            throw new DomainConflictException("already_recorded", "An outcome is already recorded for this payout. Correct that record instead.");
        var p = payment.Normalize(today);
        if (p.AmountMinor > payout.AmountMinor)
            throw new DomainValidationException(new Dictionary<string, string[]>
            {
                ["amountMinor"] = ["The amount can't be more than the scheduled payout."],
            });
        var r = new PayoutRecord { PayoutId = payout.Id };
        r.Init(group.Id, payout.RecipientMembershipId, p, recordedBy, replaces, now);
        return r;
    }
}

public enum RecordKind { ContributionRecord, PayoutRecord }

/// <summary>Append-only trail linking a reversed record to its replacement, with the reason and both sets of values.</summary>
public sealed class RecordAmendment
{
    private RecordAmendment() { }
    public Guid Id { get; private set; }
    public Guid GroupId { get; private set; }
    public RecordKind RecordType { get; private set; }
    public Guid RecordId { get; private set; }
    public Guid? ReplacementRecordId { get; private set; }
    public string Reason { get; private set; } = "";
    public string OldValues { get; private set; } = "";
    public string? NewValues { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static RecordAmendment Create(Guid groupId, RecordKind kind, Guid recordId, Guid? replacementId, string reason,
        string oldValuesJson, string? newValuesJson, Guid createdBy, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now), GroupId = groupId, RecordType = kind, RecordId = recordId, ReplacementRecordId = replacementId,
            Reason = RecordRules.Reason(reason), OldValues = oldValuesJson, NewValues = newValuesJson, CreatedBy = createdBy, CreatedAt = now,
        };
}

public static class RecordRules
{
    public const int MinReasonLength = 5;
    public const int MaxReasonLength = 500;

    public static void EnsureGroupAcceptsRecords(Group group)
    {
        if (group.Status != GroupStatus.Active)
            throw new DomainConflictException("group_not_active", "Payments can only be recorded while the group is active.");
    }

    /// <summary>
    /// Rotating payouts are recorded while the group runs. Money owed back (release or refund) is recorded after the group
    /// has ended or been cancelled too — that is exactly when it is paid.
    /// </summary>
    public static bool CanRecordPayout(Group group, Payout payout) => payout.Kind == PayoutKind.RotatingPayout
        ? group.Status == GroupStatus.Active
        : group.Status is GroupStatus.Active or GroupStatus.Completed or GroupStatus.Cancelled;

    public static void EnsureCanRecordPayout(Group group, Payout payout)
    {
        if (!CanRecordPayout(group, payout))
            throw new DomainConflictException("group_not_active", "Payouts can only be recorded while the group is active.");
    }

    public static string Reason(string? reason)
    {
        var r = reason?.Trim() ?? "";
        if (r.Length < MinReasonLength || r.Length > MaxReasonLength)
            throw new DomainValidationException(new Dictionary<string, string[]>
            {
                ["reason"] = [$"Give a reason of {MinReasonLength}–{MaxReasonLength} characters."],
            });
        return r;
    }
}
