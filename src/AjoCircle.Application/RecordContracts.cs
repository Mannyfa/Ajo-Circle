using AjoCircle.Domain.Groups;
using AjoCircle.Domain.Records;

namespace AjoCircle.Application.Contracts;

// ---- Organizer-reported records ----
public sealed record RecordPaymentRequest(long AmountMinor, DateOnly PaidOn, PaymentMethod Method, string? EvidenceReference, string? Note)
{
    public ReportedPayment ToDomain() => new(AmountMinor, PaidOn, Method, EvidenceReference, Note);
}

/// <summary>Reverses a record with a reason. With a replacement, the corrected values become a new linked record.</summary>
public sealed record CorrectRecordRequest(string Reason, RecordPaymentRequest? Replacement);

public sealed record RecordDto(
    Guid Id, MoneyDto Amount, DateOnly PaidOn, PaymentMethod Method, string? EvidenceReference, string? Note, RecordStatus Status,
    string RecordedByName, DateTimeOffset RecordedAt, string? ConfirmedByName, DateTimeOffset? ConfirmedAt,
    string? ReversedByName, DateTimeOffset? ReversedAt, string? ReversalReason, Guid? ReplacesRecordId,
    Guid? OpenDisputeId, IReadOnlyList<string> AllowedActions);

public sealed record ObligationDetailDto(
    Guid Id, Guid GroupId, int RoundNumber, string MemberName, Guid MembershipId, bool IsYou, MoneyDto Amount, DateOnly DueDate,
    ObligationStatus Status, IReadOnlyList<RecordDto> Records, IReadOnlyList<DisputeSummaryDto> Disputes, IReadOnlyList<string> AllowedActions);

public sealed record PayoutDetailDto(
    Guid Id, Guid GroupId, PayoutKind Kind, int? RoundNumber, string RecipientName, Guid RecipientMembershipId, bool IsYou, MoneyDto ScheduledAmount,
    DateOnly ScheduledDate, PayoutStatus Status, IReadOnlyList<RecordDto> Records, IReadOnlyList<DisputeSummaryDto> Disputes,
    IReadOnlyList<string> AllowedActions);

/// <summary>Organizer-only round totals. "Recorded" counts organizer records, which are not verified money.</summary>
public sealed record RoundProgressDto(int Expected, int Recorded, int Confirmed, int Disputed, MoneyDto ExpectedTotal, MoneyDto RecordedTotal);

// ---- Disputes ----
public sealed record RaiseDisputeRequest(DisputeCategory Category, string Description);
public sealed record DisputeMessageRequest(string Body);
public sealed record WithdrawDisputeRequest(string? Note);

public sealed record DisputeSummaryDto(
    Guid Id, string Reference, Guid GroupId, string GroupName, RecordKind RecordType, Guid RecordId,
    /// <summary>The obligation or payout the disputed record belongs to.</summary>
    Guid SubjectId, string SubjectLabel, DisputeCategory Category, DisputeStatus Status, DisputeResolution? Resolution,
    string RaisedByName, bool RaisedByYou, DateTimeOffset RaisedAt, DateTimeOffset? ResolvedAt);

public sealed record DisputeMessageDto(Guid Id, string AuthorName, bool IsYou, bool IsOrganizer, string Body, DateTimeOffset CreatedAt);

public sealed record DisputeDetailDto(
    DisputeSummaryDto Summary, string Description, string? ResolutionNote, string? ResolvedByName, Guid? CorrectiveRecordId,
    RecordDto Record, IReadOnlyList<DisputeMessageDto> Messages, IReadOnlyList<string> AllowedActions);
