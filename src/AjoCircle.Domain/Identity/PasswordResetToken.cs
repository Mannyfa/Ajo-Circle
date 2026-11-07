using System.Security.Cryptography;
using System.Text;

namespace AjoCircle.Domain.Identity;

/// <summary>
/// Single-use account recovery link (brief FR-03, §11.1). 256-bit random token; only its SHA-256 is stored, so a
/// database leak can't be used to take over accounts.
/// </summary>
public sealed class PasswordResetToken
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    /// <summary>At most this many reset emails per account per hour, regardless of IP.</summary>
    public const int MaxPerHour = 3;

    private PasswordResetToken() { }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public static (PasswordResetToken Token, string Raw) Create(Guid userId, DateTimeOffset now)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (new PasswordResetToken { Id = Guid.CreateVersion7(now), UserId = userId, TokenHash = Hash(raw), CreatedAt = now, ExpiresAt = now + Lifetime }, raw);
    }

    public static string Hash(string raw) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw ?? "")));

    public bool IsUsable(DateTimeOffset now) => ConsumedAt is null && now < ExpiresAt;

    /// <summary>Used, or invalidated because another reset completed.</summary>
    public void Consume(DateTimeOffset now) => ConsumedAt ??= now;
}
