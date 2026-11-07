using AjoCircle.Domain.Common;
using AjoCircle.Domain.Groups;
using AjoCircle.Domain.Records;
using static AjoCircle.UnitTests.RotatingSchemeTests;

namespace AjoCircle.UnitTests;

/// <summary>Brief §13.1: invalid contribution/payout state transitions, and no payout becomes paid because its date arrived.</summary>
public class RecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organizer = Guid.NewGuid();
    private static readonly Guid MemberUser = Guid.NewGuid();

    private static (Group Group, ActivationResult Schedule) ActiveGroup(int members = 3)
    {
        var g = Group.CreateRotatingDraft(Organizer, "Ikeja Market Ajo", Rules(members), Now);
        var v = g.Publish(Organizer, Today, Now);
        var list = new List<GroupMembership>();
        for (var i = 0; i < members; i++)
        {
            var seq = g.AdmitMember(list.Count, members, Now);
            list.Add(GroupMembership.Create(g.Id, i == 0 ? MemberUser : Guid.NewGuid(), seq, v.Id, Guid.NewGuid(), Now));
        }
        var positions = list.Select((m, i) => PayoutPosition.Create(g.Id, m.Id, i + 1, Organizer, Now)).ToList();
        return (g, RotatingActivation.Activate(g, v, list, positions, Today, Now));
    }

    private static ReportedPayment Paid(long amountMinor, DateOnly? on = null, string? evidence = null) =>
        new(amountMinor, on ?? Today, PaymentMethod.BankTransfer, evidence, null);

    [Fact]
    public void Recording_a_contribution_starts_as_recorded_by_organizer_not_confirmed_or_verified()
    {
        var (g, s) = ActiveGroup();
        var o = s.Obligations[0];
        var r = ContributionRecord.Create(g, o, false, Paid(o.AmountMinor, evidence: "  TRF-123  "), Organizer, Today, Now);
        o.SyncWithRecord(r.Status);

        Assert.Equal(RecordStatus.RecordedByOrganizer, r.Status);
        Assert.Equal(ObligationStatus.RecordedByOrganizer, o.Status);
        Assert.Equal("TRF-123", r.EvidenceReference);
        Assert.Equal(o.MembershipId, r.SubjectMembershipId);
    }

    [Fact]
    public void Contribution_amount_must_match_what_is_owed()
    {
        var (g, s) = ActiveGroup();
        var o = s.Obligations[0];
        var ex = Assert.Throws<DomainValidationException>(() =>
            ContributionRecord.Create(g, o, false, Paid(o.AmountMinor - 100), Organizer, Today, Now));
        Assert.Contains("amountMinor", ex.Errors.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(150)] // not whole naira
    public void Invalid_amounts_are_rejected(long amount)
    {
        var (g, s) = ActiveGroup();
        Assert.Throws<DomainValidationException>(() => ContributionRecord.Create(g, s.Obligations[0], false, Paid(amount), Organizer, Today, Now));
    }

    [Fact]
    public void Payment_dates_cannot_be_in_the_future()
    {
        var (g, s) = ActiveGroup();
        var o = s.Obligations[0];
        var ex = Assert.Throws<DomainValidationException>(() =>
            ContributionRecord.Create(g, o, false, Paid(o.AmountMinor, Today.AddDays(1)), Organizer, Today, Now));
        Assert.Contains("paidOn", ex.Errors.Keys);
    }

    [Fact]
    public void A_second_active_record_for_the_same_obligation_is_rejected()
    {
        var (g, s) = ActiveGroup();
        var o = s.Obligations[0];
        var ex = Assert.Throws<DomainConflictException>(() => ContributionRecord.Create(g, o, true, Paid(o.AmountMinor), Organizer, Today, Now));
        Assert.Equal("already_recorded", ex.Code);
    }

    [Fact]
    public void Records_are_only_accepted_while_the_group_is_active()
    {
        var g = Group.CreateRotatingDraft(Organizer, "Draft", Rules(3), Now);
        var (_, s) = ActiveGroup();
        var ex = Assert.Throws<DomainConflictException>(() =>
            ContributionRecord.Create(g, s.Obligations[0], false, Paid(s.Obligations[0].AmountMinor), Organizer, Today, Now));
        Assert.Equal("group_not_active", ex.Code);
    }

    [Fact]
    public void Member_confirmation_is_idempotent_and_never_means_verified()
    {
        var (g, s) = ActiveGroup();
        var o = s.Obligations[0];
        var r = ContributionRecord.Create(g, o, false, Paid(o.AmountMinor), Organizer, Today, Now);
        r.Confirm(MemberUser, Now);
        r.Confirm(MemberUser, Now);
        o.SyncWithRecord(r.Status);
        Assert.Equal(RecordStatus.ConfirmedByMember, r.Status);
        Assert.Equal(ObligationStatus.ConfirmedByMember, o.Status);
        Assert.NotEqual(ObligationStatus.VerifiedByProvider, o.Status);
    }

    [Fact]
    public void Disputed_and_reversed_records_cannot_be_confirmed()
    {
        var (g, s) = ActiveGroup();
        var o = s.Obligations[0];
        var r = ContributionRecord.Create(g, o, false, Paid(o.AmountMinor), Organizer, Today, Now);
        Dispute.Open(r, RecordKind.ContributionRecord, MemberUser, DisputeCategory.WrongDate, "I paid on Monday, not Tuesday.", Now);
        Assert.Equal("record_disputed", Assert.Throws<DomainConflictException>(() => r.Confirm(MemberUser, Now)).Code);

        r.Reverse(Organizer, "Entered against the wrong member", Now);
        Assert.Equal("record_reversed", Assert.Throws<DomainConflictException>(() => r.Confirm(MemberUser, Now)).Code);
        Assert.Equal("record_reversed", Assert.Throws<DomainConflictException>(() => r.Reverse(Organizer, "again please", Now)).Code);
    }

    [Fact]
    public void Only_one_open_dispute_per_record_and_description_is_required()
    {
        var (g, s) = ActiveGroup();
        var o = s.Obligations[0];
        var r = ContributionRecord.Create(g, o, false, Paid(o.AmountMinor), Organizer, Today, Now);
        Assert.Throws<DomainValidationException>(() => Dispute.Open(r, RecordKind.ContributionRecord, MemberUser, DisputeCategory.Other, "short", Now));
        Assert.Equal(RecordStatus.RecordedByOrganizer, r.Status); // a failed dispute leaves the record unchanged

        var d = Dispute.Open(r, RecordKind.ContributionRecord, MemberUser, DisputeCategory.WrongAmount, "I paid less than this says.", Now);
        Assert.Matches("^DSP-[2-9A-HJKMNP-Z]{8}$", d.Reference);
        Assert.Equal(DisputeStatus.Open, d.Status);
        Assert.Equal("dispute_already_open", Assert.Throws<DomainConflictException>(() =>
            Dispute.Open(r, RecordKind.ContributionRecord, MemberUser, DisputeCategory.Other, "Another complaint here", Now)).Code);
    }

    [Fact]
    public void Withdrawing_a_dispute_confirms_the_record_and_closes_the_thread()
    {
        var (g, s) = ActiveGroup();
        var o = s.Obligations[0];
        var r = ContributionRecord.Create(g, o, false, Paid(o.AmountMinor), Organizer, Today, Now);
        var d = Dispute.Open(r, RecordKind.ContributionRecord, MemberUser, DisputeCategory.WrongDate, "The date looks wrong to me.", Now);
        r.AcceptAfterDispute(MemberUser, Now);
        d.Resolve(MemberUser, DisputeResolution.WithdrawnByMember, "Checked my bank app, it's right.", null, Now);

        Assert.Equal(RecordStatus.ConfirmedByMember, r.Status);
        Assert.Equal(DisputeStatus.Resolved, d.Status);
        Assert.Throws<DomainConflictException>(() => d.Resolve(MemberUser, DisputeResolution.WithdrawnByMember, null, null, Now));
        Assert.Throws<DomainConflictException>(() => DisputeMessage.Create(d, MemberUser, "one more thing", Now));
    }

    [Fact]
    public void Reversal_needs_a_reason_and_returns_the_obligation_to_due()
    {
        var (g, s) = ActiveGroup();
        var o = s.Obligations[0];
        var r = ContributionRecord.Create(g, o, false, Paid(o.AmountMinor), Organizer, Today, Now);
        Assert.Throws<DomainValidationException>(() => r.Reverse(Organizer, "oops", Now));
        Assert.Equal(RecordStatus.RecordedByOrganizer, r.Status);

        r.Reverse(Organizer, "Recorded by mistake", Now);
        o.SyncWithRecord(null);
        Assert.Equal(RecordStatus.Reversed, r.Status);
        Assert.Equal(ObligationStatus.Due, o.Status);
        Assert.Equal(o.AmountMinor, r.AmountMinor); // original values stay visible

        var replacement = ContributionRecord.Create(g, o, false, Paid(o.AmountMinor, Today.AddDays(-1)), Organizer, Today, Now, replaces: r.Id);
        Assert.Equal(r.Id, replacement.ReplacesRecordId);
    }

    [Fact]
    public void Payouts_stay_scheduled_when_their_date_arrives_and_only_move_on_a_record()
    {
        var (g, s) = ActiveGroup();
        var p = s.Payouts[0];
        Assert.Equal(PayoutStatus.Scheduled, p.Status); // nothing in the domain advances it by date

        var r = PayoutRecord.Create(g, p, false, Paid(p.AmountMinor), Organizer, p.ScheduledDate > Today ? Today : p.ScheduledDate, Now);
        p.SyncWithRecord(r.Status);
        Assert.Equal(PayoutStatus.RecordedByOrganizer, p.Status);
        r.Confirm(MemberUser, Now);
        p.SyncWithRecord(r.Status);
        Assert.Equal(PayoutStatus.ConfirmedByRecipient, p.Status);
    }

    [Fact]
    public void An_organizer_who_is_also_the_member_cannot_confirm_or_dispute_their_own_record()
    {
        var (g, s) = ActiveGroup();
        var p = s.Payouts[0];
        var r = PayoutRecord.Create(g, p, false, Paid(p.AmountMinor), Organizer, Today, Now);
        Assert.Equal("own_record", Assert.Throws<DomainConflictException>(() => r.Confirm(Organizer, Now)).Code);
        Assert.Equal("own_record", Assert.Throws<DomainConflictException>(() =>
            Dispute.Open(r, RecordKind.PayoutRecord, Organizer, DisputeCategory.Other, "Disputing my own entry", Now)).Code);
        Assert.Equal(RecordStatus.RecordedByOrganizer, r.Status);
    }

    [Fact]
    public void Payout_record_cannot_exceed_the_scheduled_amount()
    {
        var (g, s) = ActiveGroup();
        var p = s.Payouts[0];
        Assert.Throws<DomainValidationException>(() => PayoutRecord.Create(g, p, false, Paid(p.AmountMinor + 100), Organizer, Today, Now));
        var partial = PayoutRecord.Create(g, p, false, Paid(p.AmountMinor - 10_000), Organizer, Today, Now);
        Assert.Equal(p.AmountMinor - 10_000, partial.AmountMinor);
    }
}
