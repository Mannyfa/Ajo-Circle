using System.Text.RegularExpressions;
using AjoCircle.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace AjoCircle.Infrastructure;

/// <summary>Encryption at rest via ASP.NET Core Data Protection (authenticated encryption, rotating keys).</summary>
public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _p = provider.CreateProtector("AjoCircle.SensitiveData.v1");
    public string Protect(string plaintext) => _p.Protect(plaintext);
    public string Unprotect(string protectedText) => _p.Unprotect(protectedText);
    public byte[] Protect(byte[] plaintext) => _p.Protect(plaintext);
    public byte[] Unprotect(byte[] protectedBytes) => _p.Unprotect(protectedBytes);
}

/// <summary>
/// Development only: logs the text (with the number partly masked) and copies it to the Mailpit test inbox as an email to
/// sms-&lt;digits&gt;@sms.test, so phone codes can be read at http://localhost:8025. Nothing is sent to a real phone.
/// </summary>
internal sealed partial class DevelopmentSmsSender(SmtpEmailSender? smtp, ILogger<DevelopmentSmsSender> log) : ISmsSender
{
    public async Task SendAsync(string e164Phone, string text, CancellationToken ct)
    {
        log.LogWarning("[DEV ONLY] SMS to {Phone}: {Text}", AjoCircle.Domain.Identity.PhoneNumbers.Mask(e164Phone), text);
        if (smtp is null) return;
        try
        {
            await smtp.SendAsync(new EmailMessage($"sms-{Digits().Replace(e164Phone, "")}@sms.test", e164Phone, $"[SMS] to {e164Phone}", text), ct);
        }
        catch (Exception ex) when (ex is System.Net.Mail.SmtpException or InvalidOperationException or IOException)
        {
            log.LogWarning("[DEV ONLY] Could not copy the SMS to Mailpit ({Message}).", ex.Message);
        }
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex Digits();
}

internal sealed class UnconfiguredSmsSender : ISmsSender
{
    public Task SendAsync(string e164Phone, string text, CancellationToken ct) =>
        throw new InvalidOperationException("No SMS provider is configured.");
}
