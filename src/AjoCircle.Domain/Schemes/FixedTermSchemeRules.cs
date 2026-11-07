using AjoCircle.Domain.Common;

namespace AjoCircle.Domain.Schemes;

/// <summary>
/// How money is shared out at the end of a fixed-term group. Chosen by the organizer and locked into the terms members
/// accept, so the formula exists before anyone joins (brief §7.3; owner decision 1).
/// </summary>
public enum SavingsAllocation
{
    /// <summary>Individual target: each member gets back exactly what was recorded for them.</summary>
    OwnContributions,
    /// <summary>Shared target: the recorded total is split equally among members still in the group.</summary>
    Equal,
    /// <summary>Shared target: each member's share is in proportion to what was recorded for them.</summary>
    Proportional,
    /// <summary>Shared target: the organizer assigns each member a percentage before the group starts.</summary>
    OrganizerAssigned,
}

/// <summary>
/// Terms of a fixed-term savings group (brief §4.3, §7.3): every member contributes the same amount each period for a
/// fixed number of periods, then the money is released according to <see cref="Allocation"/>. Early exit and
/// cancellation return what each member paid (owner decision 2). The app never holds the money; the organizer does.
/// </summary>
public sealed record FixedTermSchemeRules
{
    public const string SchemaId = "fixed_term.v1";

    public const int MinMembers = 2;
    public const int MaxMembers = 100;
    public const int MinPeriods = 1;
    public const int MaxPeriods = 104;
    public const int MaxTermDays = 3 * 366;
    public const int MaxReleaseOffsetDays = 14;
    public const int MaxGoalLength = 120;

    public string Schema { get; init; } = SchemaId;
    public required SchemeVariant Variant { get; init; }
    public required long ContributionAmountMinor { get; init; }
    public string Currency { get; init; } = Currencies.Ngn;
    public required ContributionFrequency Frequency { get; init; }
    /// <summary>Most members the group accepts. Unlike a rotating ajo it can start with fewer (at least 2).</summary>
    public required int MemberCount { get; init; }
    /// <summary>Number of contributions each member makes.</summary>
    public required int Periods { get; init; }
    /// <summary>Calendar date (Africa/Lagos) on which the first contribution is due.</summary>
    public required DateOnly StartDate { get; init; }
    /// <summary>Days after the last due date on which the money is released.</summary>
    public int ReleaseOffsetDays { get; init; }
    public required SavingsAllocation Allocation { get; init; }
    /// <summary>Optional purpose shown to members, e.g. "School fees for January".</summary>
    public string? Goal { get; init; }

    public Money Contribution => new(ContributionAmountMinor, Currency);

    public void Validate(DateOnly today)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string msg) => (errors.TryGetValue(field, out var l) ? l : errors[field] = []).Add(msg);

        if (Schema != SchemaId) Add(nameof(Schema), "Unsupported scheme schema.");
        if (!Currencies.IsSupported(Currency)) Add(nameof(Currency), "Only NGN is supported.");
        if (ContributionAmountMinor < RotatingSchemeRules.MinContributionMinor || ContributionAmountMinor > RotatingSchemeRules.MaxContributionMinor)
            Add(nameof(ContributionAmountMinor),
                $"Contribution must be between NGN {RotatingSchemeRules.MinContributionMinor / 100:N0} and NGN {RotatingSchemeRules.MaxContributionMinor / 100:N0}.");
        if (ContributionAmountMinor % Currencies.NgnMinorUnitsPerMajor != 0)
            Add(nameof(ContributionAmountMinor), "Contribution must be a whole naira amount.");
        if (MemberCount < MinMembers || MemberCount > MaxMembers)
            Add(nameof(MemberCount), $"Member limit must be between {MinMembers} and {MaxMembers}.");
        if (Periods < MinPeriods || Periods > MaxPeriods)
            Add(nameof(Periods), $"Number of contributions must be between {MinPeriods} and {MaxPeriods}.");
        if (!Enum.IsDefined(Frequency)) Add(nameof(Frequency), "Unknown frequency.");
        if (ReleaseOffsetDays < 0 || ReleaseOffsetDays > MaxReleaseOffsetDays)
            Add(nameof(ReleaseOffsetDays), $"Release must be 0–{MaxReleaseOffsetDays} days after the last contribution.");
        if (StartDate < today) Add(nameof(StartDate), "Start date cannot be in the past.");
        if (StartDate > today.AddYears(1)) Add(nameof(StartDate), "Start date must be within the next 12 months.");
        if (Goal?.Length > MaxGoalLength) Add(nameof(Goal), $"Keep the goal under {MaxGoalLength} characters.");

        var allocationOk = Variant switch
        {
            SchemeVariant.Individual => Allocation == SavingsAllocation.OwnContributions,
            SchemeVariant.Shared => Allocation is SavingsAllocation.Equal or SavingsAllocation.Proportional or SavingsAllocation.OrganizerAssigned,
            _ => false,
        };
        if (!allocationOk)
            Add(nameof(Allocation), Variant == SchemeVariant.Individual
                ? "Individual savings always return each member's own contributions."
                : "Choose how the shared total is divided: equally, in proportion to what each paid, or by percentages you assign.");

        if (Periods is >= MinPeriods and <= MaxPeriods && Enum.IsDefined(Frequency))
        {
            var last = FixedTermCalculator.DueDate(this, Periods);
            if (last.DayNumber - StartDate.DayNumber > MaxTermDays) Add(nameof(Periods), "The savings term can't be longer than 3 years.");
        }

        DomainValidationException.ThrowIfAny(errors);
    }
}

public enum SchemeVariant { Individual, Shared }

/// <summary>Scheduled (not collected) figures. A target being reached never implies the money is held or available.</summary>
public sealed record FixedTermSummary(
    int MemberLimit, int Periods, Money ContributionPerPeriod, Money TargetPerMember, Money MaxScheduledTotal,
    DateOnly FirstDueDate, DateOnly LastDueDate, DateOnly ReleaseDate);

public sealed record PeriodPlan(int Number, DateOnly DueDate);

public static class FixedTermCalculator
{
    public static FixedTermSummary Summarize(FixedTermSchemeRules r)
    {
        var target = r.Contribution.Times(r.Periods);
        var last = DueDate(r, r.Periods);
        return new FixedTermSummary(r.MemberCount, r.Periods, r.Contribution, target, target.Times(r.MemberCount),
            r.StartDate, last, last.AddDays(r.ReleaseOffsetDays));
    }

    /// <summary>Due date of period <paramref name="n"/> (1-based), computed from the start date so there is no drift.</summary>
    public static DateOnly DueDate(FixedTermSchemeRules r, int n) => r.Frequency switch
    {
        ContributionFrequency.Weekly => r.StartDate.AddDays(7 * (n - 1)),
        ContributionFrequency.Biweekly => r.StartDate.AddDays(14 * (n - 1)),
        ContributionFrequency.Monthly => r.StartDate.AddMonths(n - 1),
        _ => throw new DomainException("frequency_unknown", "Unknown frequency."),
    };

    public static IReadOnlyList<PeriodPlan> BuildSchedule(FixedTermSchemeRules r) =>
        Enumerable.Range(1, r.Periods).Select(n => new PeriodPlan(n, DueDate(r, n))).ToList();
}

/// <summary>
/// Splits a total exactly, in kobo, by weights (largest-remainder method). The parts always add up to the total; leftover
/// kobo go to the largest remainders, ties broken by the order given (join order), so results are deterministic.
/// </summary>
public static class Allocator
{
    public static IReadOnlyList<long> Split(long totalMinor, IReadOnlyList<long> weights)
    {
        if (totalMinor < 0) throw new DomainException("money_negative", "Amounts cannot be negative.");
        if (weights.Count == 0) return [];
        if (weights.Any(w => w < 0)) throw new DomainException("weight_negative", "Shares cannot be negative.");
        var sum = weights.Aggregate(0m, (a, w) => a + w);
        if (sum == 0) return weights.Select(_ => 0L).ToList();

        var exact = weights.Select(w => (decimal)totalMinor * w / sum).ToList();
        var parts = exact.Select(e => (long)Math.Floor(e)).ToArray();
        var leftover = totalMinor - parts.Sum();
        foreach (var i in Enumerable.Range(0, parts.Length).OrderByDescending(i => exact[i] - Math.Floor(exact[i])).ThenBy(i => i).Take((int)leftover))
            parts[i]++;
        return parts;
    }
}
