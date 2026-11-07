using AjoCircle.Domain.Identity;
using AjoCircle.Domain.Records;

namespace AjoCircle.Application.Contracts;

// ---- Identity (user side) ----
public sealed record PhoneRequest(string Phone);
public sealed record SubmissionSummaryDto(Guid Id, IdentityLevel Level, IdentitySubmissionStatus Status, DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt, string? RejectionReason);
public sealed record VerificationStatusDto(IdentityLevel Level, string? PhoneMasked, bool PhoneVerified, SubmissionSummaryDto? Latest);

/// <summary>Built by the API from a multipart form; the content type is set from the file's own bytes.</summary>
public sealed record UploadedFileDto(IdentityDocumentKind Kind, string ContentType, byte[] Content);
public sealed record SubmitVerificationCommand(IdentityLevel Level, IdDocumentType IdType, string? IdNumber, string? Bvn, IReadOnlyList<UploadedFileDto> Files);

// ---- Operations: MFA ----
public sealed record MfaSetupDto(string Secret, string OtpAuthUri);
public sealed record MfaCodeRequest(string Code);

// ---- Operations: identity review ----
public sealed record VerificationQueueItemDto(Guid Id, string UserName, string UserEmail, IdentityLevel Level, IdDocumentType IdType,
    IdentitySubmissionStatus Status, DateTimeOffset SubmittedAt, DateTimeOffset? ReviewedAt);
public sealed record IdentityDocumentDto(Guid Id, IdentityDocumentKind Kind, string ContentType, long SizeBytes, bool Available);
public sealed record VerificationDetailDto(
    VerificationQueueItemDto Summary, string PhoneMasked, string IdNumberLast4, string? BvnLast4, bool SensitiveDataPurged,
    string? DecisionNote, string? ReviewedByName, IReadOnlyList<IdentityDocumentDto> Documents, IReadOnlyList<string> AllowedActions);
/// <summary>Full identifiers, shown only on an explicit, audited request.</summary>
public sealed record RevealedIdentifiersDto(string IdNumber, string? Bvn);
public sealed record ReviewDecisionRequest(string? Note);
public sealed record DocumentFile(string ContentType, byte[] Content);

// ---- Operations: disputes ----
public sealed record EscalationInfoDto(bool CanEscalate, DateTimeOffset EscalationAllowedFrom, DateTimeOffset? EscalatedAt, string? EscalatedByName,
    OperationsDecision? Decision, string? DecisionNote, DateTimeOffset? DecidedAt);
public sealed record OperationsDecisionRequest(OperationsDecision Decision, string Note);

// ---- Operations: audit search ----
public sealed record AuditSearchItemDto(Guid Id, string Action, string EntityType, Guid EntityId, Guid? GroupId, string? GroupName,
    string? ActorName, string? ActorEmail, string? Metadata, DateTimeOffset CreatedAt);
