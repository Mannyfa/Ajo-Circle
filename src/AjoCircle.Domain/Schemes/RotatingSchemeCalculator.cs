using AjoCircle.Domain.Common;

namespace AjoCircle.Domain.Schemes;

/// <summary>Scheduled (not collected) totals. These never imply money is held, funded or guaranteed.</summary>
public sealed record RotatingSchemeSummary(
    int Members,
    int Rounds,
    Money ContributionPerMemberPerRound,
    Money ExpectedPoolPerRound,
    Money TotalCommitmentPerMember,
    Money TotalScheduledContributions,
    DateOnly FirstDueDate,
    DateOnly FinalPayoutDate);

public sealed record RoundPlan(int RoundNumber, DateOnly ContributionDueDate, DateOnly PayoutDate);

public static class RotatingSchemeCalculator
{
    public static RotatingSchemeSummary Summarize(RotatingSchemeRules rules)
    {
        var n = rules.MemberCount;
        var r = n; // standard model: each member receives exactly one turn
        var c = rules.Contribution;
        var schedule = BuildSchedule(rules);
        return new RotatingSchemeSummary(
            Members: n,
            Rounds: r,
            ContributionPerMemberPerRound: c,
            ExpectedPoolPerRound: c.Times(n),
            TotalCommitmentPerMember: c.Times(r),
            TotalScheduledContributions: c.Times(n).Times(r),
            FirstDueDate: schedule[0].ContributionDueDate,
            FinalPayoutDate: schedule[^1].PayoutDate);
    }

    /// <summary>
    /// Due dates are computed from the start date for every round (no cumulative drift).
    /// Monthly schedules clamp to month end, e.g. 31 Jan → 28/29 Feb → 31 Mar.
    /// </summary>
    public static IReadOnlyList<RoundPlan> BuildSchedule(RotatingSchemeRules rules)
    {
        var rounds = new List<RoundPlan>(rules.MemberCount);
        for (var i = 0; i < rules.MemberCount; i++)
        {
            var due = rules.Frequency switch
            {
                ContributionFrequency.Weekly => rules.StartDate.AddDays(7 * i),
                ContributionFrequency.Biweekly => rules.StartDate.AddDays(14 * i),
                ContributionFrequency.Monthly => rules.StartDate.AddMonths(i),
                _ => throw new DomainException("frequency_unknown", "Unknown frequency."),
            };
            rounds.Add(new RoundPlan(i + 1, due, due.AddDays(rules.PayoutOffsetDays)));
        }
        return rounds;
    }
}
