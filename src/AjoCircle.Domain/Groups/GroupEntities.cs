using System.Security.Cryptography;
using System.Text;
using AjoCircle.Domain.Common;

namespace AjoCircle.Domain.Groups;

/// <summary>Immutable published ruleset. Never updated after creation (brief §7.5).</summary>
public sealed class SchemeVersion
{
    private SchemeVersion() { }
    public Guid Id { get; private set; }
    public Guid GroupId { get; private set; }
    public int Version { get; private set; }
    public string RulesJson { get; private set; } = "";
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static SchemeVersion Create(Guid groupId, int version, string rulesJson, Guid createdBy, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(now), GroupId = groupId, Version = version, RulesJson = rulesJson, CreatedBy = createdBy, CreatedAt = now };
}

public enum MembershipStatus { Active, Removed }

public sealed class GroupMembership
{
    private GroupMembership() { }
    public Guid Id { get; private set; }
    public Guid GroupId { get; private set; }
    public Guid UserId { get; private set; }
    public MembershipStatus Status { get; private set; }
    public int JoinSequence { get; private set; }
    public Guid AcceptedSchemeVersionId { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }
    public DateTimeOffset TermsAcceptedAt { get; private set; }
    public Guid? JoinedViaInvitationId { get; private set; }

    public static GroupMembership Create(Guid groupId, Guid userId, int joinSequence, Guid acceptedVersionId, Guid invitationId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now), GroupId = groupId, UserId = userId, Status = MembershipStatus.Active,
            JoinSequence = joinSequence, AcceptedSchemeVersionId = acceptedVersionId, JoinedAt = now,
            TermsAcceptedAt = now, JoinedViaInvitationId = invitationId,
        };
}

public sealed class GroupInvitation
{
    public const int DefaultValidityDays = 14;
    public const int MaxValidityDays = 30;

    private GroupInvitation() { }
    public Guid Id { get; private set; }
    public Guid GroupId { get; private set; }
    /// <summary>SHA-256 of the token. The raw token is shown to the organizer once and never stored.</summary>
    public string TokenHash { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public int? MaxUses { get; private set; }
    public int UseCount { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? RevokedBy { get; private set; }

    public static (GroupInvitation Invitation, string RawToken) Create(Guid groupId, Guid createdBy, int validityDays, int? maxUses, DateTimeOffset now)
    {
        if (validityDays is < 1 or > MaxValidityDays)
            throw new DomainValidationException(new Dictionary<string, string[]> { ["validityDays"] = [$"Validity must be 1–{MaxValidityDays} days."] });
        if (maxUses is < 1 or > 100)
            throw new DomainValidationException(new Dictionary<string, string[]> { ["maxUses"] = ["Max uses must be 1–100."] });
        var raw = Base64Url(RandomNumberGenerator.GetBytes(32));
        var inv = new GroupInvitation
        {
            Id = Guid.CreateVersion7(now), GroupId = groupId, TokenHash = HashToken(raw), CreatedAt = now,
            CreatedBy = createdBy, ExpiresAt = now.AddDays(validityDays), MaxUses = maxUses,
        };
        return (inv, raw);
    }

    public static string HashToken(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    public string? UnusableReason(DateTimeOffset now) =>
        RevokedAt is not null ? "revoked"
        : now >= ExpiresAt ? "expired"
        : MaxUses is { } m && UseCount >= m ? "used_up"
        : null;

    public void RecordUse(DateTimeOffset now)
    {
        if (UnusableReason(now) is { } reason)
            throw new DomainConflictException("invitation_" + reason, "This invitation can no longer be used.");
        UseCount++;
    }

    public void Revoke(Guid actorId, DateTimeOffset now)
    {
        if (RevokedAt is not null) return; // idempotent
        RevokedAt = now;
        RevokedBy = actorId;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class PayoutPosition
{
    private PayoutPosition() { }
    public Guid Id { get; private set; }
    public Guid GroupId { get; private set; }
    public Guid MembershipId { get; private set; }
    /// <summary>1-based position; position k receives the payout for round k.</summary>
    public int Position { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }
    public Guid AssignedBy { get; private set; }

    public static PayoutPosition Create(Guid groupId, Guid membershipId, int position, Guid assignedBy, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(now), GroupId = groupId, MembershipId = membershipId, Position = position, AssignedBy = assignedBy, AssignedAt = now };
}

public enum RoundStatus { Scheduled, Completed }

public sealed class SchemeRound
{
    private SchemeRound() { }
    public Guid Id { get; private set; }
    public Guid GroupId { get; private set; }
    public int RoundNumber { get; private set; }
    public DateOnly ContributionDueDate { get; private set; }
    public DateOnly PayoutDate { get; private set; }
    public RoundStatus Status { get; private set; }

    public static SchemeRound Create(Guid groupId, int number, DateOnly due, DateOnly payout, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(now), GroupId = groupId, RoundNumber = number, ContributionDueDate = due, PayoutDate = payout, Status = RoundStatus.Scheduled };
}

/// <summary>What a member is scheduled to pay. An obligation is NOT money received (brief Rule 2).</summary>
public enum ObligationStatus { Due, SubmittedForReview, RecordedByOrganizer, ConfirmedByMember, VerifiedByProvider, Disputed, Reversed }

public sealed class ContributionObligation
{
    private ContributionObligation() { }
    public Guid Id { get; private set; }
    public Guid GroupId { get; private set; }
    public Guid MembershipId { get; private set; }
    public Guid RoundId { get; private set; }
    public long AmountMinor { get; private set; }
    public string Currency { get; private set; } = Currencies.Ngn;
    public DateOnly DueDate { get; private set; }
    public ObligationStatus Status { get; private set; }

    public static ContributionObligation Create(Guid groupId, Guid membershipId, Guid roundId, Money amount, DateOnly due, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now), GroupId = groupId, MembershipId = membershipId, RoundId = roundId,
            AmountMinor = amount.AmountMinor, Currency = amount.Currency, DueDate = due, Status = ObligationStatus.Due,
        };
}

/// <summary>A scheduled payout. Scheduled is not executed, funded or guaranteed (brief Rule 2).</summary>
public enum PayoutStatus { Scheduled }

public sealed class Payout
{
    private Payout() { }
    public Guid Id { get; private set; }
    public Guid GroupId { get; private set; }
    public Guid RoundId { get; private set; }
    public Guid RecipientMembershipId { get; private set; }
    public long AmountMinor { get; private set; }
    public string Currency { get; private set; } = Currencies.Ngn;
    public DateOnly ScheduledDate { get; private set; }
    public PayoutStatus Status { get; private set; }

    public static Payout Create(Guid groupId, Guid roundId, Guid recipient, Money amount, DateOnly date, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now), GroupId = groupId, RoundId = roundId, RecipientMembershipId = recipient,
            AmountMinor = amount.AmountMinor, Currency = amount.Currency, ScheduledDate = date, Status = PayoutStatus.Scheduled,
        };
}
