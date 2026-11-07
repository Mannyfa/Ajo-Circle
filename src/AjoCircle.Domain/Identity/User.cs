using System.Security.Cryptography;
using System.Text;
using AjoCircle.Domain.Common;

namespace AjoCircle.Domain.Identity;

public enum UserStatus { Active, Locked }

/// <summary>
/// How far a user's identity has been checked (owner decision 7). Members need phone + one ID document; organizers also
/// need BVN and a passport photo. Organizer level includes everything a member can do.
/// </summary>
public enum IdentityLevel { None, Member, Organizer }

public sealed class User
{
    private User() { }
    public Guid Id { get; private set; }
    /// <summary>Lower-cased, trimmed. Unique.</summary>
    public string Email { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public string PasswordHash { get; private set; } = "";
    public UserStatus Status { get; private set; }
    public DateTimeOffset? ContactVerifiedAt { get; private set; }
    public DateTimeOffset? TermsAcceptedAt { get; private set; }
    /// <summary>Changing this invalidates existing sessions.</summary>
    public string SecurityStamp { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    /// <summary>Email copies of notifications. Security emails (verification, password reset) are always sent.</summary>
    public bool EmailNotificationsEnabled { get; private set; } = true;

    public void SetEmailNotifications(bool enabled) => EmailNotificationsEnabled = enabled;

    // ---- Phone ----
    /// <summary>E.164, e.g. +2348031234567. Unique across users.</summary>
    public string? PhoneNumber { get; private set; }
    public DateTimeOffset? PhoneVerifiedAt { get; private set; }
    public bool IsPhoneVerified => PhoneVerifiedAt is not null && PhoneNumber is not null;

    public void SetVerifiedPhone(string e164, DateTimeOffset now) { PhoneNumber = e164; PhoneVerifiedAt = now; }

    // ---- Identity ----
    public IdentityLevel IdentityLevel { get; private set; } = IdentityLevel.None;
    public DateTimeOffset? IdentityVerifiedAt { get; private set; }

    public bool HasIdentity(IdentityLevel needed) => IdentityLevel >= needed;

    /// <summary>Only ever raises the level: approving a member check never downgrades a verified organizer.</summary>
    public void GrantIdentity(IdentityLevel level, DateTimeOffset now)
    {
        if (level <= IdentityLevel) return;
        IdentityLevel = level;
        IdentityVerifiedAt = now;
    }

    // ---- Operations staff (brief §3, §5.4) ----
    public bool IsOperations { get; private set; }
    /// <summary>TOTP secret, encrypted by the application before it reaches this entity.</summary>
    public string? MfaSecretProtected { get; private set; }
    public DateTimeOffset? MfaEnabledAt { get; private set; }
    /// <summary>Last accepted TOTP step, so the same code can't be replayed.</summary>
    public long? MfaLastStep { get; private set; }
    public bool IsMfaEnabled => MfaEnabledAt is not null && MfaSecretProtected is not null;

    public void GrantOperations() => IsOperations = true;

    /// <summary>Removing the role also signs the user out everywhere.</summary>
    public void RevokeOperations() { IsOperations = false; SecurityStamp = NewStamp(); }

    public void BeginMfaSetup(string protectedSecret)
    {
        if (IsMfaEnabled) throw new DomainConflictException("mfa_already_enabled", "Two-step sign-in is already set up.");
        MfaSecretProtected = protectedSecret;
    }

    public void EnableMfa(long step, DateTimeOffset now) { MfaEnabledAt = now; MfaLastStep = step; }

    public void RecordMfaStep(long step) => MfaLastStep = step;

    public bool IsContactVerified => ContactVerifiedAt is not null;

    public static string NormalizeEmail(string email) => (email ?? "").Trim().ToLowerInvariant();

    public static User Register(string email, string displayName, bool acceptedTerms, DateTimeOffset now)
    {
        var errors = new Dictionary<string, List<string>>();
        email = NormalizeEmail(email);
        displayName = (displayName ?? "").Trim();
        if (email.Length is < 5 or > 254 || !System.Net.Mail.MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            errors["email"] = ["Enter a valid email address."];
        if (displayName.Length is < 2 or > 60)
            errors["displayName"] = ["Display name must be 2–60 characters."];
        if (!acceptedTerms)
            errors["acceptTerms"] = ["You must accept the Terms of Service and Privacy Notice."];
        DomainValidationException.ThrowIfAny(errors);

        return new User
        {
            Id = Guid.CreateVersion7(now), Email = email, DisplayName = displayName, Status = UserStatus.Active,
            TermsAcceptedAt = now, SecurityStamp = NewStamp(), CreatedAt = now, EmailNotificationsEnabled = true,
        };
    }

    public void SetPasswordHash(string hash) { PasswordHash = hash; SecurityStamp = NewStamp(); }

    public void MarkContactVerified(DateTimeOffset now) => ContactVerifiedAt ??= now;

    public void UpdateDisplayName(string displayName)
    {
        displayName = (displayName ?? "").Trim();
        if (displayName.Length is < 2 or > 60)
            throw new DomainValidationException(new Dictionary<string, string[]> { ["displayName"] = ["Display name must be 2–60 characters."] });
        DisplayName = displayName;
    }

    public static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 10 || password.Length > 128)
            throw new DomainValidationException(new Dictionary<string, string[]> { ["password"] = ["Password must be 10–128 characters."] });
    }

    private static string NewStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}

public enum ContactChannel { Email, Phone }

/// <summary>One-time code sent to the user's email or phone. Only a hash is stored.</summary>
public sealed class ContactVerification
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private ContactVerification() { }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string CodeHash { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public int Attempts { get; private set; }
    public ContactChannel Channel { get; private set; } = ContactChannel.Email;
    /// <summary>The phone number (E.164) the code was sent to; null for email.</summary>
    public string? Target { get; private set; }

    public static (ContactVerification Verification, string Code) Create(Guid userId, DateTimeOffset now,
        ContactChannel channel = ContactChannel.Email, string? target = null)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        return (new ContactVerification
        {
            Id = Guid.CreateVersion7(now), UserId = userId, CodeHash = Hash(userId, code), CreatedAt = now, ExpiresAt = now + Lifetime,
            Channel = channel, Target = target,
        }, code);
    }

    /// <summary>Returns true when the code matches. Wrong codes count toward the attempt limit.</summary>
    public bool TryConsume(string code, DateTimeOffset now)
    {
        if (ConsumedAt is not null || now >= ExpiresAt || Attempts >= MaxAttempts) return false;
        Attempts++;
        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(CodeHash), Encoding.ASCII.GetBytes(Hash(UserId, (code ?? "").Trim())));
        if (ok) ConsumedAt = now;
        return ok;
    }

    private static string Hash(Guid userId, string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId:N}:{code}")));
}
