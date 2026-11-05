using AjoCircle.Domain.Groups;
using AjoCircle.Domain.Schemes;

namespace AjoCircle.Application.Contracts;

// ---- Auth ----
public sealed record RegisterRequest(string Email, string DisplayName, string Password, bool AcceptTerms);
public sealed record LoginRequest(string Email, string Password);
public sealed record VerifyContactRequest(string Code);
public sealed record UpdateProfileRequest(string DisplayName);
public sealed record MeDto(Guid Id, string Email, string DisplayName, bool ContactVerified, DateTimeOffset CreatedAt);

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

public sealed record RoundPlanDto(int RoundNumber, DateOnly ContributionDueDate, DateOnly PayoutDate);
public sealed record SchemePreviewDto(SchemeSummaryDto Summary, IReadOnlyList<RoundPlanDto> Schedule);

// ---- Groups ----
public sealed record CreateRotatingGroupRequest(string Name, RotatingRulesDto Rules);
public sealed record UpdateDraftGroupRequest(string Name, RotatingRulesDto Rules);
public sealed record CancelGroupRequest(string Reason);
public sealed record AssignPayoutOrderRequest(IReadOnlyList<Guid> MembershipIds);

public enum ViewerRole { Organizer, Member }

public sealed record GroupListItemDto(
    Guid Id, string Name, SchemeType SchemeType, GroupStatus Status, ViewerRole ViewerRole,
    int MemberCount, int Capacity, MoneyDto Contribution, ContributionFrequency Frequency, DateOnly StartDate);

public sealed record GroupDetailDto(
    Guid Id, string Name, SchemeType SchemeType, GroupStatus Status, ViewerRole ViewerRole,
    string OrganizerName, RotatingRulesDto Rules, SchemeSummaryDto Summary,
    int? CurrentSchemeVersion, Guid? CurrentSchemeVersionId, int ActiveMemberCount,
    DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt, DateTimeOffset? StartedAt,
    DateTimeOffset? CancelledAt, string? CancellationReason,
    MyMembershipDto? MyMembership, IReadOnlyList<string> AllowedActions);

public sealed record MyMembershipDto(Guid MembershipId, int? PayoutPosition, int AcceptedSchemeVersion, DateTimeOffset JoinedAt);

/// <summary>Organizers see the roster. Members see first names / display names only, no contact details.</summary>
public sealed record MemberDto(Guid MembershipId, string DisplayName, string? Email, int JoinSequence, int? PayoutPosition,
    int AcceptedSchemeVersion, DateTimeOffset JoinedAt, bool IsYou);

public sealed record SchemeVersionDto(Guid Id, int Version, RotatingRulesDto Rules, DateTimeOffset CreatedAt);

public sealed record RoundDto(Guid Id, int RoundNumber, DateOnly ContributionDueDate, DateOnly PayoutDate, RoundStatus Status,
    string? PayoutRecipientName, MoneyDto ScheduledPayout, PayoutStatus PayoutStatus);

public sealed record ObligationDto(Guid Id, int RoundNumber, string MemberName, Guid MembershipId, MoneyDto Amount,
    DateOnly DueDate, ObligationStatus Status);

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
    RotatingRulesDto Rules, SchemeSummaryDto Summary, int SpotsRemaining, bool CanJoin, string? CannotJoinReason);

public sealed record JoinGroupRequest(Guid AcceptedSchemeVersionId);
public sealed record JoinResultDto(Guid GroupId, Guid MembershipId);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
