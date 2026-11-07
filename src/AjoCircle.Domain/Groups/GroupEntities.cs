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

/// <summary>Exited = left a fixed-term group early; they keep read access to their own history and refund.</summary>
public enum MembershipStatus { Active, Removed, Exited }

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
    public DateTimeOffset? ExitedAt { get; private set; }
    public Guid? ExitedBy { get; private set; }
    public string? ExitReason { get; private set; }

    /// <summary>Leaves a fixed-term group early. What they paid is owed back to them (owner decision 2).</summary>
    public void Exit(Guid actorId, string? reason, DateTimeOffset now)
    {
        if (Status != MembershipStatus.Active)
            throw new DomainConflictException("member_not_active", "This member is no longer active in the group.");
        var r = (reason ?? "").Trim();
        if (r.Length is < 5 or > 500)
            throw new DomainValidationException(new Dictionary<string, string[]> { ["reason"] = ["Give a reason of 5–500 characters."] });
        Status = MembershipStatus.Exited;
        ExitedAt = now;
        ExitedBy = actorId;
        ExitReason = r;
    }

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
/// <summary>Cancelled = no longer owed because the member left or the group was cancelled before it fell due.</summary>
public enum ObligationStatus { Due, SubmittedForReview, RecordedByOrganizer, ConfirmedByMember, VerifiedByProvider, Disputed, Reversed, Cancelled }

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

    /// <summary>
    /// The obligation's status mirrors its current (non-reversed) record, or Due when there is none. Status is never
    /// set directly from a request (brief §6.2).
    /// </summary>
    /// <summary>Only an unpaid obligation is dropped; anything already recorded stays as history.</summary>
    public bool CancelIfDue()
    {
        if (Status != ObligationStatus.Due) return false;
        Status = ObligationStatus.Cancelled;
        return true;
    }

    public void SyncWithRecord(Records.RecordStatus? current) => Status = current switch
    {
        null or Records.RecordStatus.Reversed => ObligationStatus.Due,
        Records.RecordStatus.RecordedByOrganizer => ObligationStatus.RecordedByOrganizer,
        Records.RecordStatus.ConfirmedByMember => ObligationStatus.ConfirmedByMember,
        Records.RecordStatus.Disputed => ObligationStatus.Disputed,
        _ => throw new ArgumentOutOfRangeException(nameof(current)),
    };
}

/// <summary>
/// A scheduled payout. Scheduled is not executed, funded or guaranteed (brief Rule 2). The other states reflect what
/// the organizer reported and how the recipient responded; none of them means the app moved money.
/// </summary>
public enum PayoutStatus { Scheduled, RecordedByOrganizer, ConfirmedByRecipient, Disputed }

/// <summary>
/// What a payout is for. A rotating payout belongs to a round. The others are money the organizer owes back to a member
/// of a fixed-term group: the release at the end, or a refund on early exit or cancellation (owner decision 2).
/// </summary>
public enum PayoutKind { RotatingPayout, Release, ExitRefund, CancellationRefund }

public sealed class Payout
{
    private Payout() { }
    public Guid Id { get; private set; }
    public Guid GroupId { get; private set; }
    public PayoutKind Kind { get; private set; }
    /// <summary>Set for rotating payouts only.</summary>
    public Guid? RoundId { get; private set; }
    public Guid RecipientMembershipId { get; private set; }
    public long AmountMinor { get; private set; }
    public string Currency { get; private set; } = Currencies.Ngn;
    public DateOnly ScheduledDate { get; private set; }
    public PayoutStatus Status { get; private set; }

    public static Payout Create(Guid groupId, Guid roundId, Guid recipient, Money amount, DateOnly date, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now), GroupId = groupId, Kind = PayoutKind.RotatingPayout, RoundId = roundId, RecipientMembershipId = recipient,
            AmountMinor = amount.AmountMinor, Currency = amount.Currency, ScheduledDate = date, Status = PayoutStatus.Scheduled,
        };

    /// <summary>Money the organizer owes back to a member. Only created when there is something to return.</summary>
    public static Payout CreateOwedBack(Guid groupId, PayoutKind kind, Guid recipient, Money amount, DateOnly date, DateTimeOffset now)
    {
        if (kind == PayoutKind.RotatingPayout) throw new ArgumentException("Use Create for rotating payouts.", nameof(kind));
        if (amount.AmountMinor <= 0) throw new DomainException("nothing_owed", "There is nothing to return.");
        return new()
        {
            Id = Guid.CreateVersion7(now), GroupId = groupId, Kind = kind, RoundId = null, RecipientMembershipId = recipient,
            AmountMinor = amount.AmountMinor, Currency = amount.Currency, ScheduledDate = date, Status = PayoutStatus.Scheduled,
        };
    }

    /// <summary>Mirrors the current outcome record; back to Scheduled when the record is reversed without a replacement.</summary>
    public void SyncWithRecord(Records.RecordStatus? current) => Status = current switch
    {
        null or Records.RecordStatus.Reversed => PayoutStatus.Scheduled,
        Records.RecordStatus.RecordedByOrganizer => PayoutStatus.RecordedByOrganizer,
        Records.RecordStatus.ConfirmedByMember => PayoutStatus.ConfirmedByRecipient,
        Records.RecordStatus.Disputed => PayoutStatus.Disputed,
        _ => throw new ArgumentOutOfRangeException(nameof(current)),
    };
}

/// <summary>
/// For a shared fixed-term group whose terms say the organizer assigns shares: each member's share of the released total
/// in basis points (1/100 of a percent). Set after enrolment closes and before the group starts; locked once it starts.
/// </summary>
public sealed class AllocationShare
{
    public const int Whole = 10_000;

    private AllocationShare() { }
    public Guid Id { get; private set; }
    public Guid GroupId { get; private set; }
    public Guid MembershipId { get; private set; }
    public int BasisPoints { get; private set; }
    public Guid AssignedBy { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }

    public static AllocationShare Create(Guid groupId, Guid membershipId, int basisPoints, Guid assignedBy, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(now), GroupId = groupId, MembershipId = membershipId, BasisPoints = basisPoints, AssignedBy = assignedBy, AssignedAt = now };

    /// <summary>Every active member exactly once, each share 0–100%, and the shares add up to exactly 100%.</summary>
    public static void Validate(IReadOnlyCollection<Guid> activeMembershipIds, IReadOnlyDictionary<Guid, int> shares)
    {
        var errors = new List<string>();
        if (!shares.Keys.ToHashSet().SetEquals(activeMembershipIds)) errors.Add("Give every current member a share, and only current members.");
        if (shares.Values.Any(v => v is < 0 or > Whole)) errors.Add("Each share must be between 0% and 100%.");
        if (shares.Values.Sum() != Whole) errors.Add($"Shares must add up to exactly 100% (they add up to {shares.Values.Sum() / 100m:0.##}%).");
        if (errors.Count > 0) throw new DomainValidationException(new Dictionary<string, string[]> { ["shares"] = errors.ToArray() });
    }
}
