using AjoCircle.Domain.Groups;
using AjoCircle.Domain.Schemes;

namespace AjoCircle.Application.Contracts;

// ---- Auth ----
public sealed record RegisterRequest(string Email, string DisplayName, string Password, bool AcceptTerms, Domain.Identity.AccountRole? Role);
public sealed record LoginRequest(string Email, string Password);
public sealed record VerifyContactRequest(string Code);
public sealed record UpdateProfileRequest(string DisplayName);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword);
public sealed record MeDto(Guid Id, string Email, string DisplayName, bool ContactVerified, DateTimeOffset CreatedAt,
    Domain.Identity.IdentityLevel IdentityLevel, bool PhoneVerified, bool IsOperations, bool MfaEnabled, bool OperationsSessionActive,
    Domain.Identity.AccountRole Role);

// ---- Money ----
public sealed record MoneyDto(long AmountMinor, string Currency);

// ---- Schemes ----
public sealed record RotatingRulesDto(
    long ContributionAmountMinor,
    string Currency,
    ContributionFrequency Frequency,
    int MemberCount,
    DateOnly StartDate,
    int PayoutOffsetDays,
    PayoutOrderMethod PayoutOrder)
{
    public RotatingSchemeRules ToDomain() => new()
    {
        ContributionAmountMinor = ContributionAmountMinor,
        Currency = string.IsNullOrWhiteSpace(Currency) ? "NGN" : Currency.Trim().ToUpperInvariant(),
        Frequency = Frequency,
        MemberCount = MemberCount,
        StartDate = StartDate,
        PayoutOffsetDays = PayoutOffsetDays,
        PayoutOrder = PayoutOrder,
    };

    public static RotatingRulesDto From(RotatingSchemeRules r) =>
        new(r.ContributionAmountMinor, r.Currency, r.Frequency, r.MemberCount, r.StartDate, r.PayoutOffsetDays, r.PayoutOrder);
}

public sealed record SchemeSummaryDto(
    int Members, int Rounds, MoneyDto ContributionPerMemberPerRound, MoneyDto ExpectedPoolPerRound,
    MoneyDto TotalCommitmentPerMember, MoneyDto TotalScheduledContributions, DateOnly FirstDueDate, DateOnly FinalPayoutDate)
{
    public static SchemeSummaryDto From(RotatingSchemeSummary s) => new(
        s.Members, s.Rounds, M(s.ContributionPerMemberPerRound), M(s.ExpectedPoolPerRound),
        M(s.TotalCommitmentPerMember), M(s.TotalScheduledContributions), s.FirstDueDate, s.FinalPayoutDate);
    private static MoneyDto M(Domain.Common.Money m) => new(m.AmountMinor, m.Currency);
}

/// <summary>PayoutDate is null for fixed-term periods, which have no payout of their own.</summary>
public sealed record RoundPlanDto(int RoundNumber, DateOnly ContributionDueDate, DateOnly? PayoutDate);
/// <summary>Exactly one of Summary (rotating) or FixedTermSummary is set.</summary>
public sealed record SchemePreviewDto(SchemeSummaryDto? Summary, IReadOnlyList<RoundPlanDto> Schedule, FixedTermSummaryDto? FixedTermSummary = null);

// ---- Groups ----
public sealed record CreateRotatingGroupRequest(string Name, RotatingRulesDto Rules);
public sealed record UpdateDraftGroupRequest(string Name, RotatingRulesDto Rules);
public sealed record CancelGroupRequest(string Reason);
public sealed record AssignPayoutOrderRequest(IReadOnlyList<Guid> MembershipIds);

public enum ViewerRole { Organizer, Member }

public sealed record GroupListItemDto(
    Guid Id, string Name, SchemeType SchemeType, GroupStatus Status, ViewerRole ViewerRole,
    int MemberCount, int Capacity, MoneyDto Contribution, ContributionFrequency Frequency, DateOnly StartDate);

/// <summary>Rules/Summary are set for a rotating ajo; FixedTermRules/FixedTermSummary for fixed-term savings.</summary>
public sealed record GroupDetailDto(
    Guid Id, string Name, SchemeType SchemeType, GroupStatus Status, ViewerRole ViewerRole,
    string OrganizerName, RotatingRulesDto? Rules, SchemeSummaryDto? Summary,
    FixedTermRulesDto? FixedTermRules, FixedTermSummaryDto? FixedTermSummary, int Capacity,
    int? CurrentSchemeVersion, Guid? CurrentSchemeVersionId, int ActiveMemberCount,
    DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt, DateTimeOffset? StartedAt,
    DateTimeOffset? CancelledAt, string? CancellationReason, DateTimeOffset? CompletedAt,
    MyMembershipDto? MyMembership, SavingsProgressDto? Savings, IReadOnlyList<string> AllowedActions);

public sealed record MyMembershipDto(Guid MembershipId, int? PayoutPosition, int AcceptedSchemeVersion, DateTimeOffset JoinedAt,
    MembershipStatus Status, DateTimeOffset? ExitedAt, int? ShareBasisPoints);

/// <summary>Organizers see the roster. Members see first names / display names only, no contact details.</summary>
public sealed record MemberDto(Guid MembershipId, string DisplayName, string? Email, int JoinSequence, int? PayoutPosition,
    int AcceptedSchemeVersion, DateTimeOffset JoinedAt, bool IsYou, MembershipStatus Status, DateTimeOffset? ExitedAt, string? ExitReason,
    int? ShareBasisPoints);

public sealed record SchemeVersionDto(Guid Id, int Version, RotatingRulesDto? Rules, DateTimeOffset CreatedAt, FixedTermRulesDto? FixedTermRules = null);

/// <summary>Payout fields are null for fixed-term periods.</summary>
public sealed record RoundDto(Guid Id, int RoundNumber, DateOnly ContributionDueDate, DateOnly? PayoutDate, RoundStatus Status,
    string? PayoutRecipientName, MoneyDto? ScheduledPayout, PayoutStatus? PayoutStatus, Guid? PayoutId, bool PayoutIsYours,
    RoundProgressDto? Progress);

public sealed record ObligationDto(Guid Id, int RoundNumber, string MemberName, Guid MembershipId, MoneyDto Amount,
    DateOnly DueDate, ObligationStatus Status);

/// <summary>One of the caller's own contributions, from any group they belong to (member workspace).</summary>
public sealed record MyObligationDto(Guid Id, Guid GroupId, string GroupName, int RoundNumber, MoneyDto Amount, DateOnly DueDate, ObligationStatus Status);
/// <summary>A payout or refund scheduled to the caller.</summary>
public sealed record MyPayoutDto(Guid Id, Guid GroupId, string GroupName, PayoutKind Kind, int? RoundNumber, MoneyDto Amount, DateOnly ScheduledDate, PayoutStatus Status);
/// <summary>What the caller owes and is owed next. Dates are schedule dates only — the app moves no money.</summary>
public sealed record MyScheduleDto(IReadOnlyList<MyObligationDto> Contributions, IReadOnlyList<MyPayoutDto> Payouts);

public sealed record AuditLogDto(Guid Id, string Action, string EntityType, Guid EntityId, string? ActorName, string? Metadata, DateTimeOffset CreatedAt);

// ---- Invitations ----
public sealed record CreateInvitationRequest(int? ValidityDays, int? MaxUses);
/// <summary>The raw token is returned once at creation and never again.</summary>
public sealed record CreatedInvitationDto(Guid Id, string Token, DateTimeOffset ExpiresAt, int? MaxUses);
public sealed record InvitationDto(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, int? MaxUses, int UseCount,
    DateTimeOffset? RevokedAt, string State);

/// <summary>Safe public view: no member list, no contact details (brief §2, §4.4).</summary>
public sealed record PublicInvitationDto(
    string GroupName, string OrganizerName, SchemeType SchemeType, Guid SchemeVersionId, int SchemeVersion,
    RotatingRulesDto? Rules, SchemeSummaryDto? Summary, int SpotsRemaining, bool CanJoin, string? CannotJoinReason,
    FixedTermRulesDto? FixedTermRules = null, FixedTermSummaryDto? FixedTermSummary = null);

public sealed record JoinGroupRequest(Guid AcceptedSchemeVersionId);
public sealed record JoinResultDto(Guid GroupId, Guid MembershipId);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

// ---- Notifications & settings ----
public sealed record NotificationDto(Guid Id, Domain.Notifications.NotificationType Type, Guid? GroupId, string Title, string Body,
    string LinkPath, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
public sealed record NotificationPageDto(IReadOnlyList<NotificationDto> Items, int Page, int PageSize, int Total, int Unread);
public sealed record UnreadCountDto(int Unread);
public sealed record SettingsDto(bool EmailNotifications);
public sealed record UpdateSettingsRequest(bool EmailNotifications);
