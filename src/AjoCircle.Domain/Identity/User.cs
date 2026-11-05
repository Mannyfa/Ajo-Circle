using System.Security.Cryptography;
using System.Text;
using AjoCircle.Domain.Common;

namespace AjoCircle.Domain.Identity;

public enum UserStatus { Active, Locked }

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
            TermsAcceptedAt = now, SecurityStamp = NewStamp(), CreatedAt = now,
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

/// <summary>One-time code sent to the user's contact. Only a hash is stored.</summary>
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

    public static (ContactVerification Verification, string Code) Create(Guid userId, DateTimeOffset now)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        return (new ContactVerification
        {
            Id = Guid.CreateVersion7(now), UserId = userId, CodeHash = Hash(userId, code), CreatedAt = now, ExpiresAt = now + Lifetime,
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
