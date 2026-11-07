using AjoCircle.Domain.Common;
using AjoCircle.Domain.Groups;
using AjoCircle.Domain.Records;
using AjoCircle.Domain.Schemes;
using static AjoCircle.UnitTests.RotatingSchemeTests;

namespace AjoCircle.UnitTests;

/// <summary>Brief §4.3, §7.3, §13.1 and the owner's decisions on shares, exits and cancellation.</summary>
public class FixedTermTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organizer = Guid.NewGuid();

    internal static FixedTermSchemeRules FtRules(SchemeVariant variant = SchemeVariant.Individual, SavingsAllocation? allocation = null,
        int members = 5, int periods = 6, long contributionNaira = 10_000, ContributionFrequency freq = ContributionFrequency.Monthly) => new()
    {
        Variant = variant,
        ContributionAmountMinor = contributionNaira * 100,
        Frequency = freq,
        MemberCount = members,
        Periods = periods,
        StartDate = Today.AddDays(7),
        ReleaseOffsetDays = 3,
        Allocation = allocation ?? (variant == SchemeVariant.Individual ? SavingsAllocation.OwnContributions : SavingsAllocation.Equal),
    };

    private static (Group Group, SchemeVersion Version, List<GroupMembership> Members) Ready(FixedTermSchemeRules rules, int join)
    {
        var g = Group.CreateFixedTermDraft(Organizer, "Lekki Savers", rules, Now);
        var v = g.Publish(Organizer, Today, Now);
        var members = new List<GroupMembership>();
        for (var i = 0; i < join; i++)
            members.Add(GroupMembership.Create(g.Id, Guid.NewGuid(), g.AdmitMember(members.Count, rules.MemberCount, Now), v.Id, Guid.NewGuid(), Now));
        if (g.Status == GroupStatus.OpenForJoining) g.CloseEnrolment(members.Count, Now);
        return (g, v, members);
    }

    [Fact]
    public void Summary_shows_each_members_target_and_the_release_date()
    {
        var s = FixedTermCalculator.Summarize(FtRules(periods: 6));
        Assert.Equal(60_000_00, s.TargetPerMember.AmountMinor);
        Assert.Equal(300_000_00, s.MaxScheduledTotal.AmountMinor);
        Assert.Equal(Today.AddDays(7).AddMonths(5), s.LastDueDate);
        Assert.Equal(s.LastDueDate.AddDays(3), s.ReleaseDate);
    }

    [Fact]
    public void Individual_savings_always_return_own_contributions_and_shared_needs_an_explicit_rule()
    {
        FtRules().Validate(Today);
        Assert.Throws<DomainValidationException>(() => (FtRules() with { Allocation = SavingsAllocation.Equal }).Validate(Today));
        Assert.Throws<DomainValidationException>(() => FtRules(SchemeVariant.Shared, SavingsAllocation.OwnContributions).Validate(Today));
        foreach (var a in new[] { SavingsAllocation.Equal, SavingsAllocation.Proportional, SavingsAllocation.OrganizerAssigned })
            FtRules(SchemeVariant.Shared, a).Validate(Today);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(105)]
    public void Period_count_is_bounded(int periods) =>
        Assert.Throws<DomainValidationException>(() => FtRules(periods: periods).Validate(Today));

    [Fact]
    public void Term_cannot_exceed_three_years() =>
        Assert.Contains("Periods", Assert.Throws<DomainValidationException>(() => FtRules(periods: 40, freq: ContributionFrequency.Monthly).Validate(Today)).Errors.Keys);

    [Fact]
    public void Allocator_splits_to_the_kobo_and_always_adds_up()
    {
        Assert.Equal(new long[] { 3334, 3333, 3333 }, Allocator.Split(10_000, [1, 1, 1]));
        Assert.Equal(new long[] { 2500, 7500 }, Allocator.Split(10_000, [2_500, 7_500]));
        Assert.Equal(new long[] { 0, 0 }, Allocator.Split(10_000, [0, 0]));
        var parts = Allocator.Split(1_000_001, [3, 7, 11, 13]);
        Assert.Equal(1_000_001, parts.Sum());
    }

    [Fact]
    public void Fixed_term_starts_with_fewer_members_than_the_limit_and_schedules_no_payouts()
    {
        var (g, v, members) = Ready(FtRules(members: 5, periods: 4), join: 2);
        var result = FixedTermActivation.Activate(g, v, members, new Dictionary<Guid, int>(), Today, Now);
        Assert.Equal(GroupStatus.Active, g.Status);
        Assert.Equal(4, result.Rounds.Count);
        Assert.Equal(8, result.Obligations.Count);
        Assert.Empty(result.Payouts); // money is only "owed back" at the end, on exit or on cancellation
    }

    [Fact]
    public void Organizer_assigned_shares_must_be_set_before_starting()
    {
        var (g, v, members) = Ready(FtRules(SchemeVariant.Shared, SavingsAllocation.OrganizerAssigned, members: 3), join: 3);
        var ex = Assert.Throws<DomainValidationException>(() => FixedTermActivation.Activate(g, v, members, new Dictionary<Guid, int>(), Today, Now));
        Assert.Contains(ex.Errors["activation"], m => m.Contains("share"));

        Assert.Throws<DomainValidationException>(() => AllocationShare.Validate(members.Select(m => m.Id).ToList(),
            members.ToDictionary(m => m.Id, _ => 3_000))); // 90%, not 100%
        var shares = new Dictionary<Guid, int> { [members[0].Id] = 5_000, [members[1].Id] = 3_000, [members[2].Id] = 2_000 };
        FixedTermActivation.Activate(g, v, members, shares, Today, Now);
        Assert.Equal(GroupStatus.Active, g.Status);
    }

    [Fact]
    public void Release_follows_the_accepted_rule()
    {
        var a = new MemberPaid(Guid.NewGuid(), 1, 60_000_00);
        var b = new MemberPaid(Guid.NewGuid(), 2, 30_000_00);
        var c = new MemberPaid(Guid.NewGuid(), 3, 10_000_00);
        MemberPaid[] all = [a, b, c];

        var own = FixedTermSettlement.Release(FtRules(), all, new Dictionary<Guid, int>());
        Assert.Equal(new long[] { 60_000_00, 30_000_00, 10_000_00 }, own.Select(x => x.AmountMinor));

        var equal = FixedTermSettlement.Release(FtRules(SchemeVariant.Shared, SavingsAllocation.Equal), all, new Dictionary<Guid, int>());
        Assert.Equal(100_000_00, equal.Sum(x => x.AmountMinor));
        Assert.Equal(new long[] { 3_333_334, 3_333_333, 3_333_333 }, equal.Select(x => x.AmountMinor));

        var proportional = FixedTermSettlement.Release(FtRules(SchemeVariant.Shared, SavingsAllocation.Proportional), all, new Dictionary<Guid, int>());
        Assert.Equal(own.Select(x => x.AmountMinor), proportional.Select(x => x.AmountMinor));

        var shares = new Dictionary<Guid, int> { [a.MembershipId] = 2_000, [b.MembershipId] = 3_000, [c.MembershipId] = 5_000 };
        var assigned = FixedTermSettlement.Release(FtRules(SchemeVariant.Shared, SavingsAllocation.OrganizerAssigned), all, shares);
        Assert.Equal(new long[] { 20_000_00, 30_000_00, 50_000_00 }, assigned.Select(x => x.AmountMinor));
    }

    [Fact]
    public void Exit_needs_a_reason_and_only_unpaid_obligations_are_dropped()
    {
        var (g, v, members) = Ready(FtRules(members: 2, periods: 2), join: 2);
        var result = FixedTermActivation.Activate(g, v, members, new Dictionary<Guid, int>(), Today, Now);
        var m = members[0];
        Assert.Throws<DomainValidationException>(() => m.Exit(m.UserId, "no", Now));
        m.Exit(m.UserId, "Moving to Abuja", Now);
        Assert.Equal(MembershipStatus.Exited, m.Status);
        Assert.Throws<DomainConflictException>(() => m.Exit(m.UserId, "Moving to Abuja", Now));

        var mine = result.Obligations.Where(o => o.MembershipId == m.Id).ToList();
        mine[0].SyncWithRecord(RecordStatus.ConfirmedByMember);
        Assert.False(mine[0].CancelIfDue());
        Assert.True(mine[1].CancelIfDue());
        Assert.Equal(ObligationStatus.Cancelled, mine[1].Status);
        Assert.Equal("obligation_cancelled", Assert.Throws<DomainConflictException>(() =>
            ContributionRecord.Create(g, mine[1], false, new ReportedPayment(mine[1].AmountMinor, Today, PaymentMethod.Cash, null, null), Organizer, Today, Now)).Code);
    }

    [Fact]
    public void Active_fixed_term_can_be_cancelled_but_an_active_rotating_ajo_cannot()
    {
        var (g, v, members) = Ready(FtRules(members: 2), join: 2);
        FixedTermActivation.Activate(g, v, members, new Dictionary<Guid, int>(), Today, Now);
        g.Cancel("Members agreed to stop saving", Now);
        Assert.Equal(GroupStatus.Cancelled, g.Status);

        var r = Group.CreateRotatingDraft(Organizer, "Rotating", Rules(2), Now);
        var rv = r.Publish(Organizer, Today, Now);
        var rm = new List<GroupMembership>();
        for (var i = 0; i < 2; i++) rm.Add(GroupMembership.Create(r.Id, Guid.NewGuid(), r.AdmitMember(rm.Count, 2, Now), rv.Id, Guid.NewGuid(), Now));
        RotatingActivation.Activate(r, rv, rm, rm.Select((m, i) => PayoutPosition.Create(r.Id, m.Id, i + 1, Organizer, Now)).ToList(), Today, Now);
        Assert.Equal("cancel_not_allowed", Assert.Throws<DomainConflictException>(() => r.Cancel("Members agreed to stop", Now)).Code);
    }

    [Fact]
    public void Money_owed_back_can_be_recorded_after_the_group_ends_but_rotating_payouts_cannot()
    {
        var (g, v, members) = Ready(FtRules(members: 2), join: 2);
        FixedTermActivation.Activate(g, v, members, new Dictionary<Guid, int>(), Today, Now);
        g.CompleteTerm(Now);
        Assert.Equal(GroupStatus.Completed, g.Status);

        var release = Payout.CreateOwedBack(g.Id, PayoutKind.Release, members[0].Id, Money.Ngn(60_000_00), Today, Now);
        var record = PayoutRecord.Create(g, release, false, new ReportedPayment(60_000_00, Today, PaymentMethod.BankTransfer, null, null), Organizer, Today, Now);
        Assert.Equal(RecordStatus.RecordedByOrganizer, record.Status);

        var rotating = Payout.Create(g.Id, Guid.NewGuid(), members[0].Id, Money.Ngn(10_000_00), Today, Now);
        Assert.Equal("group_not_active", Assert.Throws<DomainConflictException>(() =>
            PayoutRecord.Create(g, rotating, false, new ReportedPayment(10_000_00, Today, PaymentMethod.Cash, null, null), Organizer, Today, Now)).Code);
        Assert.Throws<DomainException>(() => Payout.CreateOwedBack(g.Id, PayoutKind.ExitRefund, members[0].Id, Money.Ngn(0), Today, Now));
    }
}
