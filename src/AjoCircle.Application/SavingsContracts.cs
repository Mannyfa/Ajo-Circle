using AjoCircle.Domain.Groups;
using AjoCircle.Domain.Schemes;

namespace AjoCircle.Application.Contracts;

public sealed record FixedTermRulesDto(
    SchemeVariant Variant,
    long ContributionAmountMinor,
    string Currency,
    ContributionFrequency Frequency,
    int MemberCount,
    int Periods,
    DateOnly StartDate,
    int ReleaseOffsetDays,
    SavingsAllocation Allocation,
    string? Goal)
{
    public FixedTermSchemeRules ToDomain() => new()
    {
        Variant = Variant,
        ContributionAmountMinor = ContributionAmountMinor,
        Currency = string.IsNullOrWhiteSpace(Currency) ? "NGN" : Currency.Trim().ToUpperInvariant(),
        Frequency = Frequency,
        MemberCount = MemberCount,
        Periods = Periods,
        StartDate = StartDate,
        ReleaseOffsetDays = ReleaseOffsetDays,
        Allocation = Allocation,
        Goal = string.IsNullOrWhiteSpace(Goal) ? null : Goal.Trim(),
    };

    public static FixedTermRulesDto From(FixedTermSchemeRules r) =>
        new(r.Variant, r.ContributionAmountMinor, r.Currency, r.Frequency, r.MemberCount, r.Periods, r.StartDate, r.ReleaseOffsetDays, r.Allocation, r.Goal);
}

public sealed record FixedTermSummaryDto(
    int MemberLimit, int Periods, MoneyDto ContributionPerPeriod, MoneyDto TargetPerMember, MoneyDto MaxScheduledTotal,
    DateOnly FirstDueDate, DateOnly LastDueDate, DateOnly ReleaseDate)
{
    public static FixedTermSummaryDto From(FixedTermSummary s) => new(s.MemberLimit, s.Periods, M(s.ContributionPerPeriod), M(s.TargetPerMember),
        M(s.MaxScheduledTotal), s.FirstDueDate, s.LastDueDate, s.ReleaseDate);
    private static MoneyDto M(Domain.Common.Money m) => new(m.AmountMinor, m.Currency);
}

public sealed record CreateFixedTermGroupRequest(string Name, FixedTermRulesDto Rules);

public sealed record ShareDto(Guid MembershipId, int BasisPoints);
public sealed record AssignSharesRequest(IReadOnlyList<ShareDto> Shares);
public sealed record ExitMemberRequest(string Reason);

/// <summary>
/// Fixed-term figures kept separate as brief §7.3 asks. "Recorded" is the organizer's records, not verified money.
/// Members see their own figures; the organizer sees the group's.
/// </summary>
public sealed record SavingsProgressDto(
    bool ForYou, int Members, MoneyDto Target, MoneyDto Scheduled, MoneyDto Recorded, MoneyDto NotYetRecorded,
    MoneyDto OwedBack, MoneyDto OwedBackRecorded);

/// <summary>A payout of any kind. Members see only their own unless it's a rotating payout on the shared schedule.</summary>
public sealed record PayoutListItemDto(
    Guid Id, PayoutKind Kind, int? RoundNumber, string RecipientName, Guid RecipientMembershipId, bool IsYours,
    MoneyDto Amount, DateOnly ScheduledDate, PayoutStatus Status);

// ---- Reports (brief §14.2) ----
public sealed record ReportTable(AjoCircle.Application.ReportKind Kind, string Title, string GroupName, DateOnly AsOf,
    IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, string Note);

/// <summary>Headline figures for the Reports tab. ForYou = a member's own figures; otherwise the whole group's.</summary>
public sealed record ReportSummaryDto(bool ForYou, string GroupName, DateOnly AsOf, MoneyDto ScheduledToDate, MoneyDto Recorded,
    int OverdueCount, MoneyDto OverdueAmount, int RecordedCount, int ConfirmedCount, int OpenDisputes,
    IReadOnlyList<AjoCircle.Application.ReportKind> Available);
