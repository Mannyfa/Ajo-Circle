using AjoCircle.Domain.Common;

namespace AjoCircle.Domain.Schemes;

public enum ContributionFrequency { Weekly, Biweekly, Monthly }

public enum PayoutOrderMethod
{
    /// <summary>Positions follow the order in which members joined.</summary>
    JoinOrder,
    /// <summary>Organizer assigns positions after enrolment closes and before the group starts.</summary>
    OrganizerAssigned,
}

/// <summary>
/// Financial terms of a standard rotating ajo: every member contributes the same amount each round and
/// receives exactly one payout, so rounds R = members N (brief §7.1). Serialized into an immutable
/// scheme version on publish; members accept a specific version.
/// </summary>
public sealed record RotatingSchemeRules
{
    public const string SchemaId = "rotating.standard.v1";

    // Product bounds — configurable defaults, not regulatory limits.
    public const int MinMembers = 2;
    public const int MaxMembers = 100;
    public const long MinContributionMinor = 100_00;          // NGN 100
    public const long MaxContributionMinor = 10_000_000_00;   // NGN 10,000,000
    public const int MaxPayoutOffsetDays = 6;                 // must stay inside the shortest period (weekly)

    public string Schema { get; init; } = SchemaId;
    public required long ContributionAmountMinor { get; init; }
    public string Currency { get; init; } = Currencies.Ngn;
    public required ContributionFrequency Frequency { get; init; }
    /// <summary>Exact number of members (and therefore rounds) the scheme is designed for.</summary>
    public required int MemberCount { get; init; }
    /// <summary>Calendar date (Africa/Lagos) on which round 1 contributions are due.</summary>
    public required DateOnly StartDate { get; init; }
    /// <summary>Days after a round's contribution due date on which that round's payout is scheduled.</summary>
    public int PayoutOffsetDays { get; init; }
    public required PayoutOrderMethod PayoutOrder { get; init; }

    public Money Contribution => new(ContributionAmountMinor, Currency);

    /// <summary>Validates internal consistency. <paramref name="today"/> is the current Lagos calendar date.</summary>
    public void Validate(DateOnly today)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string msg) =>
            (errors.TryGetValue(field, out var l) ? l : errors[field] = []).Add(msg);

        if (Schema != SchemaId) Add(nameof(Schema), "Unsupported scheme schema.");
        if (!Currencies.IsSupported(Currency)) Add(nameof(Currency), "Only NGN is supported.");
        if (ContributionAmountMinor < MinContributionMinor || ContributionAmountMinor > MaxContributionMinor)
            Add(nameof(ContributionAmountMinor),
                $"Contribution must be between NGN {MinContributionMinor / 100:N0} and NGN {MaxContributionMinor / 100:N0}.");
        if (ContributionAmountMinor % Currencies.NgnMinorUnitsPerMajor != 0)
            Add(nameof(ContributionAmountMinor), "Contribution must be a whole naira amount.");
        if (MemberCount < MinMembers || MemberCount > MaxMembers)
            Add(nameof(MemberCount), $"Member count must be between {MinMembers} and {MaxMembers}.");
        if (!Enum.IsDefined(Frequency)) Add(nameof(Frequency), "Unknown frequency.");
        if (!Enum.IsDefined(PayoutOrder)) Add(nameof(PayoutOrder), "Unknown payout order method.");
        if (PayoutOffsetDays < 0 || PayoutOffsetDays > MaxPayoutOffsetDays)
            Add(nameof(PayoutOffsetDays), $"Payout offset must be between 0 and {MaxPayoutOffsetDays} days.");
        if (StartDate < today) Add(nameof(StartDate), "Start date cannot be in the past.");
        if (StartDate > today.AddYears(1)) Add(nameof(StartDate), "Start date must be within the next 12 months.");

        DomainValidationException.ThrowIfAny(errors);
    }
}
