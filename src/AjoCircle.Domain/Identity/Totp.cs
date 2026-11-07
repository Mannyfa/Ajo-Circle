using System.Security.Cryptography;

namespace AjoCircle.Domain.Identity;

/// <summary>
/// Time-based one-time passwords (RFC 6238: HMAC-SHA1, 30-second steps, 6 digits) — what authenticator apps such as
/// Google Authenticator or Microsoft Authenticator produce. Used as the second factor for operations staff (brief §11.1).
/// </summary>
public static class Totp
{
    public const int Digits = 6;
    public static readonly TimeSpan Step = TimeSpan.FromSeconds(30);

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    public static long StepAt(DateTimeOffset t) => t.ToUnixTimeSeconds() / (long)Step.TotalSeconds;

    public static string Code(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--) { counter[i] = (byte)(step & 0xff); step >>= 8; }
        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>
    /// Accepts the current step or one either side (clock drift). Returns the matched step so the caller can refuse to
    /// accept the same code twice; null when the code is wrong or was already used.
    /// </summary>
    public static long? Verify(byte[] secret, string? code, DateTimeOffset now, long? lastUsedStep)
    {
        code = (code ?? "").Trim().Replace(" ", "");
        if (code.Length != Digits || !code.All(char.IsAsciiDigit)) return null;
        var current = StepAt(now);
        for (var s = current - 1; s <= current + 1; s++)
        {
            if (lastUsedStep is { } last && s <= last) continue;
            if (CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(Code(secret, s)), System.Text.Encoding.ASCII.GetBytes(code)))
                return s;
        }
        return null;
    }

    public static string Base32(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var sb = new System.Text.StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }
}
