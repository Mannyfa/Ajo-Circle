using AjoCircle.Domain.Common;
using AjoCircle.Domain.Schemes;

namespace AjoCircle.UnitTests;

public class RotatingSchemeTests
{
    internal static readonly DateOnly Today = new(2026, 11, 5);

    internal static RotatingSchemeRules Rules(int members = 10, long contributionNaira = 100_000,
        ContributionFrequency freq = ContributionFrequency.Monthly, DateOnly? start = null, int offset = 0,
        PayoutOrderMethod order = PayoutOrderMethod.JoinOrder) => new()
    {
        ContributionAmountMinor = contributionNaira * 100,
        Frequency = freq,
        MemberCount = members,
        StartDate = start ?? new DateOnly(2026, 12, 1),
        PayoutOffsetDays = offset,
        PayoutOrder = order,
    };

    [Fact]
    public void Brief_example_10_members_100k_monthly()
    {
        var s = RotatingSchemeCalculator.Summarize(Rules());

        Assert.Equal(10, s.Rounds);
        Assert.Equal(1_000_000_00, s.ExpectedPoolPerRound.AmountMinor);        // NGN 1,000,000 per round
        Assert.Equal(1_000_000_00, s.TotalCommitmentPerMember.AmountMinor);    // NGN 1,000,000 per member
        Assert.Equal(10_000_000_00, s.TotalScheduledContributions.AmountMinor); // NGN 10,000,000 total
        Assert.All(new[] { s.ExpectedPoolPerRound, s.TotalScheduledContributions }, m => Assert.Equal("NGN", m.Currency));
    }

    [Fact]
    public void Kobo_representation_of_100k_naira_is_10_million_minor_units()
    {
        Assert.Equal(10_000_000, Money.Ngn(100_000 * 100).AmountMinor);
    }

    [Fact]
    public void Monthly_schedule_clamps_to_month_end_without_drift()
    {
        var rounds = RotatingSchemeCalculator.BuildSchedule(Rules(members: 4, start: new DateOnly(2027, 1, 31)));

        Assert.Equal(new[] { new DateOnly(2027, 1, 31), new DateOnly(2027, 2, 28), new DateOnly(2027, 3, 31), new DateOnly(2027, 4, 30) },
            rounds.Select(r => r.ContributionDueDate));
        Assert.Equal(new[] { 1, 2, 3, 4 }, rounds.Select(r => r.RoundNumber));
    }

    [Theory]
    [InlineData(ContributionFrequency.Weekly, 7)]
    [InlineData(ContributionFrequency.Biweekly, 14)]
    public void Fixed_period_schedules_are_ordered_and_evenly_spaced(ContributionFrequency f, int days)
    {
        var rounds = RotatingSchemeCalculator.BuildSchedule(Rules(members: 5, freq: f, offset: 2));
        for (var i = 1; i < rounds.Count; i++)
            Assert.Equal(days, rounds[i].ContributionDueDate.DayNumber - rounds[i - 1].ContributionDueDate.DayNumber);
        Assert.All(rounds, r => Assert.Equal(2, r.PayoutDate.DayNumber - r.ContributionDueDate.DayNumber));
    }

    [Fact]
    public void Valid_rules_pass_validation() => Rules().Validate(Today);

    [Theory]
    [InlineData(1, 100_000, 0, "MemberCount")]
    [InlineData(101, 100_000, 0, "MemberCount")]
    [InlineData(10, 50, 0, "ContributionAmountMinor")]
    [InlineData(10, 20_000_000, 0, "ContributionAmountMinor")]
    [InlineData(10, 100_000, 7, "PayoutOffsetDays")]
    [InlineData(10, 100_000, -1, "PayoutOffsetDays")]
    public void Invalid_rules_are_rejected_with_field_errors(int members, long naira, int offset, string field)
    {
        var ex = Assert.Throws<DomainValidationException>(() => Rules(members, naira, offset: offset).Validate(Today));
        Assert.Contains(field, ex.Errors.Keys);
    }

    [Fact]
    public void Fractional_naira_contribution_is_rejected()
    {
        var rules = Rules() with { ContributionAmountMinor = 100_000_50 };
        var ex = Assert.Throws<DomainValidationException>(() => rules.Validate(Today));
        Assert.Contains("ContributionAmountMinor", ex.Errors.Keys);
    }

    [Fact]
    public void Past_start_date_is_rejected()
    {
        var ex = Assert.Throws<DomainValidationException>(() => Rules(start: Today.AddDays(-1)).Validate(Today));
        Assert.Contains("StartDate", ex.Errors.Keys);
    }

    [Fact]
    public void Non_ngn_currency_is_rejected()
    {
        var ex = Assert.Throws<DomainValidationException>(() => (Rules() with { Currency = "USD" }).Validate(Today));
        Assert.Contains("Currency", ex.Errors.Keys);
    }

    [Fact]
    public void Money_rejects_negative_and_overflow()
    {
        Assert.Throws<DomainException>(() => Money.Ngn(-1));
        Assert.Throws<DomainException>(() => Money.Ngn(long.MaxValue / 2).Times(3));
    }
}
