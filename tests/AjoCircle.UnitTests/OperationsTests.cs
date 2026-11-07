using AjoCircle.Domain.Common;
using AjoCircle.Domain.Groups;
using AjoCircle.Domain.Identity;
using AjoCircle.Domain.Records;
using static AjoCircle.UnitTests.RotatingSchemeTests;

namespace AjoCircle.UnitTests;

/// <summary>Owner decisions 5–7: investigators decide only, a 7-day response window, identity checks before acting.</summary>
public class OperationsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromDays(7);

    [Fact]
    public void Totp_matches_the_rfc_6238_test_vector()
    {
        var secret = "12345678901234567890"u8.ToArray();
        // RFC 6238 Appendix B, SHA-1, T = 59 s → 94287082 (8 digits); the 6-digit code is the last six.
        Assert.Equal("287082", Totp.Code(secret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(59))));
        Assert.Equal("081804", Totp.Code(secret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(1111111109))));
    }

    [Fact]
    public void Totp_allows_one_step_of_drift_and_refuses_reuse()
    {
        var secret = Totp.NewSecret();
        var step = Totp.StepAt(Now);
        Assert.Equal(step, Totp.Verify(secret, Totp.Code(secret, step), Now, null));
        Assert.Equal(step - 1, Totp.Verify(secret, Totp.Code(secret, step - 1), Now, null));
        Assert.Null(Totp.Verify(secret, Totp.Code(secret, step - 2), Now, null));
        Assert.Null(Totp.Verify(secret, Totp.Code(secret, step), Now, lastUsedStep: step)); // replay
        Assert.Null(Totp.Verify(secret, "12a456", Now, null));
    }

    private static (ContributionRecord Record, Dispute Dispute, Guid Raiser) Disputed()
    {
        var organizer = Guid.NewGuid();
        var g = Group.CreateRotatingDraft(organizer, "Test", Rules(2), Now);
        var v = g.Publish(organizer, Today, Now);
        var ms = new List<GroupMembership>();
        for (var i = 0; i < 2; i++) ms.Add(GroupMembership.Create(g.Id, Guid.NewGuid(), g.AdmitMember(ms.Count, 2, Now), v.Id, Guid.NewGuid(), Now));
        var s = RotatingActivation.Activate(g, v, ms, ms.Select((m, i) => PayoutPosition.Create(g.Id, m.Id, i + 1, organizer, Now)).ToList(), Today, Now);
        var o = s.Obligations[0];
        var r = ContributionRecord.Create(g, o, false, new ReportedPayment(o.AmountMinor, Today, PaymentMethod.Cash, null, null), organizer, Today, Now);
        var raiser = ms.Single(m => m.Id == o.MembershipId).UserId;
        return (r, Dispute.Open(r, RecordKind.ContributionRecord, raiser, DisputeCategory.DidNotHappen, "I never paid this amount.", Now), raiser);
    }

    [Fact]
    public void Escalation_waits_for_the_organizers_response_window()
    {
        var (_, d, raiser) = Disputed();
        Assert.Equal("escalation_too_early", Assert.Throws<DomainConflictException>(() => d.Escalate(raiser, true, Window, Now.AddDays(6))).Code);
        Assert.Throws<DomainException>(() => d.Escalate(Guid.NewGuid(), false, Window, Now.AddDays(8)));
        d.Escalate(raiser, true, Window, Now.AddDays(7));
        Assert.Equal(DisputeStatus.Escalated, d.Status);
        Assert.True(d.IsUnresolved);
        Assert.Equal("dispute_escalated", Assert.Throws<DomainConflictException>(() => d.Escalate(raiser, true, Window, Now.AddDays(8))).Code);
    }

    [Fact]
    public void Investigator_upholding_restores_the_record_without_marking_it_confirmed()
    {
        var (r, d, raiser) = Disputed();
        Assert.Equal("dispute_not_escalated", Assert.Throws<DomainConflictException>(() =>
            d.Decide(Guid.NewGuid(), OperationsDecision.RecordUpheld, "Bank statement shows it.", r, Now)).Code);
        d.Escalate(raiser, true, Window, Now.AddDays(7));
        Assert.Throws<DomainValidationException>(() => d.Decide(Guid.NewGuid(), OperationsDecision.RecordUpheld, "short", r, Now));
        d.Decide(Guid.NewGuid(), OperationsDecision.RecordUpheld, "Bank statement shows the transfer.", r, Now.AddDays(8));
        Assert.Equal(DisputeStatus.Resolved, d.Status);
        Assert.Equal(DisputeResolution.UpheldByOperations, d.Resolution);
        Assert.Equal(RecordStatus.RecordedByOrganizer, r.Status);
        Assert.Equal("record_upheld", Assert.Throws<DomainConflictException>(() =>
            Dispute.Open(r, RecordKind.ContributionRecord, raiser, DisputeCategory.Other, "Disputing it once more.", Now, previouslyUpheld: true)).Code);
    }

    [Fact]
    public void Correction_required_leaves_the_record_with_the_organizer()
    {
        var (r, d, raiser) = Disputed();
        d.Escalate(raiser, true, Window, Now.AddDays(7));
        d.Decide(Guid.NewGuid(), OperationsDecision.CorrectionRequired, "No evidence the money changed hands.", r, Now.AddDays(8));
        Assert.Equal(DisputeStatus.Escalated, d.Status); // resolves when the organizer corrects
        Assert.Equal(RecordStatus.Disputed, r.Status);   // investigators never edit records
        Assert.Throws<DomainConflictException>(() => d.Decide(Guid.NewGuid(), OperationsDecision.RecordUpheld, "Changed my mind on this.", r, Now));
    }

    private static User UserWith(bool phone)
    {
        var u = User.Register($"u{Guid.NewGuid():N}@example.test", "Test User", true, Now);
        u.MarkContactVerified(Now);
        if (phone) u.SetVerifiedPhone("+2348031234567", Now);
        return u;
    }

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2];
    private static string Plain(string s) => "enc:" + s;

    [Fact]
    public void Organizer_check_needs_phone_id_bvn_and_photo_and_keeps_only_last_four_digits()
    {
        var noPhone = Assert.Throws<DomainValidationException>(() => IdentitySubmission.Create(UserWith(false), IdentityLevel.Organizer, IdDocumentType.NationalId,
            "12345678901", "22233344455", [new(IdentityDocumentKind.IdFront, "image/jpeg", Jpeg), new(IdentityDocumentKind.PassportPhoto, "image/png", Png)], Plain, Now));
        Assert.Contains("phone", noPhone.Errors.Keys);

        var missing = Assert.Throws<DomainValidationException>(() => IdentitySubmission.Create(UserWith(true), IdentityLevel.Organizer, IdDocumentType.NationalId,
            "12345678901", null, [new(IdentityDocumentKind.IdFront, "image/jpeg", Jpeg)], Plain, Now));
        Assert.Contains("bvn", missing.Errors.Keys);
        Assert.Contains("passportPhoto", missing.Errors.Keys);

        var s = IdentitySubmission.Create(UserWith(true), IdentityLevel.Organizer, IdDocumentType.NationalId, "123 4567 8901", "222-333-444-55",
            [new(IdentityDocumentKind.IdFront, "image/jpeg", Jpeg), new(IdentityDocumentKind.PassportPhoto, "image/png", Png)], Plain, Now);
        Assert.Equal("enc:12345678901", s.IdNumberProtected);
        Assert.Equal("8901", s.IdNumberLast4);
        Assert.Equal("4455", s.BvnLast4);
    }

    [Fact]
    public void A_pdf_is_fine_for_an_id_but_not_for_a_passport_photo()
    {
        var pdf = "%PDF-1.7 ..."u8.ToArray();
        var ex = Assert.Throws<DomainValidationException>(() => IdentitySubmission.Create(UserWith(true), IdentityLevel.Organizer, IdDocumentType.NationalId,
            "12345678901", "22233344455", [new(IdentityDocumentKind.IdFront, "application/pdf", pdf), new(IdentityDocumentKind.PassportPhoto, "application/pdf", pdf)], Plain, Now));
        Assert.Equal(["passportPhoto"], ex.Errors.Keys);
    }

    [Fact]
    public void Approval_raises_the_level_and_reviewers_need_the_role_and_cannot_review_themselves()
    {
        var subject = UserWith(true);
        var s = IdentitySubmission.Create(subject, IdentityLevel.Member, IdDocumentType.InternationalPassport, "A12345678", null,
            [new(IdentityDocumentKind.IdFront, "image/jpeg", Jpeg)], Plain, Now);
        var notStaff = UserWith(false);
        Assert.Throws<DomainException>(() => s.Approve(notStaff, subject, null, Now));
        Assert.Equal("own_submission", Assert.Throws<DomainConflictException>(() => { subject.GrantOperations(); s.Approve(subject, subject, null, Now); }).Code);

        var reviewer = UserWith(false);
        reviewer.GrantOperations();
        s.Approve(reviewer, subject, "Looks right", Now);
        Assert.Equal(IdentityLevel.Member, subject.IdentityLevel);
        subject.GrantIdentity(IdentityLevel.None, Now); // never downgrades
        Assert.Equal(IdentityLevel.Member, subject.IdentityLevel);

        s.PurgeSensitiveData(Now);
        Assert.Null(s.IdNumberProtected);
        Assert.Equal("5678", s.IdNumberLast4);
    }

    [Theory]
    [InlineData("0803 123 4567", "+2348031234567")]
    [InlineData("+234 803 123 4567", "+2348031234567")]
    [InlineData("2349011112222", "+2349011112222")]
    public void Nigerian_mobile_numbers_are_normalized(string input, string expected) => Assert.Equal(expected, PhoneNumbers.Normalize(input));

    [Theory]
    [InlineData("123")]
    [InlineData("0601 234 5678")]
    public void Invalid_phone_numbers_are_rejected(string input) => Assert.Throws<DomainValidationException>(() => PhoneNumbers.Normalize(input));
}
