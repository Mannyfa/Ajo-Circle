using AjoCircle.Domain.Common;
using AjoCircle.Domain.Groups;
using AjoCircle.Domain.Identity;
using AjoCircle.Domain.Schemes;
using static AjoCircle.UnitTests.RotatingSchemeTests;

namespace AjoCircle.UnitTests;

public class GroupLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organizer = Guid.NewGuid();

    private static (Group Group, SchemeVersion Version) Published(int members = 3, PayoutOrderMethod order = PayoutOrderMethod.JoinOrder)
    {
        var g = Group.CreateRotatingDraft(Organizer, "Ikeja Market Ajo", Rules(members, order: order), Now);
        var v = g.Publish(Organizer, Today, Now);
        return (g, v);
    }

    private static List<GroupMembership> Join(Group g, SchemeVersion v, int count)
    {
        var list = new List<GroupMembership>();
        for (var i = 0; i < count; i++)
        {
            var seq = g.AdmitMember(list.Count, g.GetDraftRules().MemberCount, Now);
            list.Add(GroupMembership.Create(g.Id, Guid.NewGuid(), seq, v.Id, Guid.NewGuid(), Now));
        }
        return list;
    }

    private static List<PayoutPosition> Positions(Group g, IEnumerable<Guid> ordered) =>
        ordered.Select((id, i) => PayoutPosition.Create(g.Id, id, i + 1, Organizer, Now)).ToList();

    [Fact]
    public void Publish_freezes_terms_and_opens_group()
    {
        var (g, v) = Published();
        Assert.Equal(GroupStatus.OpenForJoining, g.Status);
        Assert.Equal(v.Id, g.CurrentSchemeVersionId);
        Assert.Equal(1, v.Version);
        Assert.Throws<DomainConflictException>(() => g.UpdateDraft("Renamed", Rules(5)));
        Assert.Throws<DomainConflictException>(() => g.Publish(Organizer, Today, Now));
    }

    [Fact]
    public void Publishing_invalid_terms_fails_and_stays_draft()
    {
        var g = Group.CreateRotatingDraft(Organizer, "Bad", Rules(members: 1), Now);
        Assert.Throws<DomainValidationException>(() => g.Publish(Organizer, Today, Now));
        Assert.Equal(GroupStatus.Draft, g.Status);
    }

    [Fact]
    public void Enrolment_closes_automatically_when_full_and_rejects_extra_members()
    {
        var (g, v) = Published(members: 3);
        var members = Join(g, v, 3);
        Assert.Equal(GroupStatus.ReadyToStart, g.Status);
        Assert.Equal(new[] { 1, 2, 3 }, members.Select(m => m.JoinSequence));
        Assert.Throws<DomainConflictException>(() => g.AdmitMember(3, 3, Now));
    }

    [Fact]
    public void Draft_group_cannot_admit_members()
    {
        var g = Group.CreateRotatingDraft(Organizer, "Draft ajo", Rules(3), Now);
        Assert.Throws<DomainConflictException>(() => g.AdmitMember(0, 3, Now));
    }

    [Fact]
    public void Activation_generates_rounds_obligations_and_payouts()
    {
        var (g, v) = Published(members: 3);
        var members = Join(g, v, 3);
        var positions = Positions(g, PayoutOrderPlanner.JoinOrder(members));

        var result = RotatingActivation.Activate(g, v, members, positions, Today, Now);

        Assert.Equal(GroupStatus.Active, g.Status);
        Assert.Equal(3, result.Rounds.Count);
        Assert.Equal(9, result.Obligations.Count);                        // N × R
        Assert.All(result.Obligations, o => Assert.Equal(ObligationStatus.Due, o.Status));
        Assert.All(result.Obligations, o => Assert.Equal(100_000_00, o.AmountMinor));
        Assert.Equal(9, result.Obligations.Select(o => (o.MembershipId, o.RoundId)).Distinct().Count());
        Assert.All(result.Payouts, p => Assert.Equal(300_000_00, p.AmountMinor)); // pool = N × C
        Assert.All(result.Payouts, p => Assert.Equal(PayoutStatus.Scheduled, p.Status));
        // Round k pays the member in position k.
        Assert.Equal(members.Select(m => m.Id), result.Payouts.OrderBy(p => result.Rounds.Single(r => r.Id == p.RoundId).RoundNumber).Select(p => p.RecipientMembershipId));
    }

    [Fact]
    public void Activation_requires_full_membership()
    {
        var (g, v) = Published(members: 4);
        var members = Join(g, v, 2);
        g.CloseEnrolment(members.Count, Now);
        var ex = Assert.Throws<DomainValidationException>(() =>
            RotatingActivation.Activate(g, v, members, Positions(g, members.Select(m => m.Id)), Today, Now));
        Assert.Contains(ex.Errors["activation"], m => m.Contains("exactly 4 members"));
        Assert.Equal(GroupStatus.ReadyToStart, g.Status);
    }

    [Fact]
    public void Activation_rejects_duplicate_or_missing_positions()
    {
        var (g, v) = Published(members: 3);
        var members = Join(g, v, 3);
        var dup = new List<PayoutPosition>
        {
            PayoutPosition.Create(g.Id, members[0].Id, 1, Organizer, Now),
            PayoutPosition.Create(g.Id, members[1].Id, 1, Organizer, Now),
            PayoutPosition.Create(g.Id, members[2].Id, 3, Organizer, Now),
        };
        Assert.Throws<DomainValidationException>(() => RotatingActivation.Activate(g, v, members, dup, Today, Now));
        Assert.Throws<DomainValidationException>(() => RotatingActivation.Activate(g, v, members, dup.Take(2).ToList(), Today, Now));
    }

    [Fact]
    public void Activation_rejects_members_on_another_terms_version()
    {
        var (g, v) = Published(members: 2);
        var members = Join(g, v, 1);
        var seq = g.AdmitMember(1, 2, Now);
        members.Add(GroupMembership.Create(g.Id, Guid.NewGuid(), seq, Guid.NewGuid(), Guid.NewGuid(), Now));
        Assert.Throws<DomainValidationException>(() =>
            RotatingActivation.Activate(g, v, members, Positions(g, members.Select(m => m.Id)), Today, Now));
    }

    [Fact]
    public void Activation_rejects_start_date_that_has_passed()
    {
        var (g, v) = Published(members: 2);
        var members = Join(g, v, 2);
        var later = new DateOnly(2027, 1, 1);
        Assert.Throws<DomainValidationException>(() =>
            RotatingActivation.Activate(g, v, members, Positions(g, members.Select(m => m.Id)), later, Now));
    }

    [Fact]
    public void Payout_order_must_be_a_permutation_of_active_members()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        PayoutOrderPlanner.ValidateAssignment([a, b, c], [c, a, b]);
        Assert.Throws<DomainValidationException>(() => PayoutOrderPlanner.ValidateAssignment([a, b, c], [a, a, b]));
        Assert.Throws<DomainValidationException>(() => PayoutOrderPlanner.ValidateAssignment([a, b, c], [a, b]));
        Assert.Throws<DomainValidationException>(() => PayoutOrderPlanner.ValidateAssignment([a, b, c], [a, b, Guid.NewGuid()]));
    }

    [Fact]
    public void Active_group_cannot_be_cancelled_in_this_release()
    {
        var (g, v) = Published(members: 2);
        var members = Join(g, v, 2);
        RotatingActivation.Activate(g, v, members, Positions(g, members.Select(m => m.Id)), Today, Now);
        Assert.Throws<DomainConflictException>(() => g.Cancel("Members asked to stop", Now));
    }

    [Fact]
    public void Cancel_requires_reason_and_is_final()
    {
        var (g, _) = Published();
        Assert.Throws<DomainValidationException>(() => g.Cancel("", Now));
        g.Cancel("Not enough interest", Now);
        Assert.Equal(GroupStatus.Cancelled, g.Status);
        Assert.Throws<DomainConflictException>(() => g.AdmitMember(0, 3, Now));
    }
}

public class InvitationAndIdentityTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Invitation_stores_only_a_hash_of_the_token()
    {
        var (inv, raw) = GroupInvitation.Create(Guid.NewGuid(), Guid.NewGuid(), 14, null, Now);
        Assert.NotEqual(raw, inv.TokenHash);
        Assert.Equal(GroupInvitation.HashToken(raw), inv.TokenHash);
        Assert.True(raw.Length >= 43);
    }

    [Fact]
    public void Expired_revoked_and_used_up_invitations_cannot_be_used()
    {
        var (inv, _) = GroupInvitation.Create(Guid.NewGuid(), Guid.NewGuid(), 1, 1, Now);
        Assert.Null(inv.UnusableReason(Now));
        Assert.Equal("expired", inv.UnusableReason(Now.AddDays(1)));
        inv.RecordUse(Now);
        Assert.Equal("used_up", inv.UnusableReason(Now));
        Assert.Throws<DomainConflictException>(() => inv.RecordUse(Now));

        var (inv2, _) = GroupInvitation.Create(Guid.NewGuid(), Guid.NewGuid(), 14, null, Now);
        inv2.Revoke(Guid.NewGuid(), Now);
        Assert.Equal("revoked", inv2.UnusableReason(Now));
    }

    [Fact]
    public void Verification_code_is_single_use_and_attempt_limited()
    {
        var userId = Guid.NewGuid();
        var (v, code) = ContactVerification.Create(userId, Now);
        Assert.False(v.TryConsume("000000" == code ? "111111" : "000000", Now));
        Assert.True(v.TryConsume(code, Now));
        Assert.False(v.TryConsume(code, Now));

        var (v2, code2) = ContactVerification.Create(userId, Now);
        for (var i = 0; i < ContactVerification.MaxAttempts; i++) v2.TryConsume("x", Now);
        Assert.False(v2.TryConsume(code2, Now));

        var (v3, code3) = ContactVerification.Create(userId, Now);
        Assert.False(v3.TryConsume(code3, Now + ContactVerification.Lifetime));
    }

    [Fact]
    public void Registration_normalizes_email_and_requires_terms()
    {
        var u = User.Register("  Ada@Example.COM ", "Ada", true, AccountRole.Organizer, Now);
        Assert.Equal("ada@example.com", u.Email);
        Assert.Throws<DomainValidationException>(() => User.Register("ada@example.com", "Ada", false, AccountRole.Member, Now));
        Assert.Throws<DomainValidationException>(() => User.Register("not-an-email", "Ada", true, AccountRole.Member, Now));
    }
}
