using System.Net;
using System.Net.Mail;
using AjoCircle.Application;
using Microsoft.Extensions.Logging;

namespace AjoCircle.Infrastructure;

public sealed class SmtpOptions
{
    public const string Section = "Email:Smtp";
    public string? Host { get; init; }
    public int Port { get; init; } = 587;
    public bool EnableSsl { get; init; } = true;
    public string? Username { get; init; }
    /// <summary>Supply through user secrets or environment variables, never appsettings in the repo.</summary>
    public string? Password { get; init; }
    public string From { get; init; } = "no-reply@ajocircle.test";
}

/// <summary>Sends plain-text email over SMTP. Throws if the server can't be reached.</summary>
public sealed class SmtpEmailSender(SmtpOptions options, AppOptions app) : IEmailSender
{
    public async Task SendAsync(EmailMessage m, CancellationToken ct)
    {
        using var client = new SmtpClient(options.Host, options.Port) { EnableSsl = options.EnableSsl };
        if (!string.IsNullOrEmpty(options.Username))
            client.Credentials = new NetworkCredential(options.Username, options.Password);
        using var message = new MailMessage(new MailAddress(options.From, app.ProductName), new MailAddress(m.To, m.ToName))
        {
            Subject = m.Subject,
            Body = m.TextBody,
        };
        await client.SendMailAsync(message, ct);
    }
}

/// <summary>
/// Development only: logs every email in full (codes and reset links included — never do this elsewhere) and also sends
/// it to the local test inbox when SMTP (Mailpit) is configured. A Mailpit outage is a warning, not a failed request.
/// </summary>
internal sealed class DevelopmentEmailSender(SmtpEmailSender? smtp, ILogger<DevelopmentEmailSender> log) : IEmailSender
{
    public async Task SendAsync(EmailMessage m, CancellationToken ct)
    {
        log.LogWarning("[DEV ONLY] Email to {Email} — {Subject}\n{Body}", m.To, m.Subject, m.TextBody);
        if (smtp is null) return;
        try
        {
            await smtp.SendAsync(m, ct);
            log.LogInformation("[DEV ONLY] Delivered to the local test inbox (Mailpit, http://localhost:8025).");
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or IOException)
        {
            log.LogWarning("[DEV ONLY] Could not reach the SMTP test inbox ({Message}). Is Mailpit running? The email is logged above.", ex.Message);
        }
    }
}

internal sealed class UnconfiguredEmailSender : IEmailSender
{
    public Task SendAsync(EmailMessage m, CancellationToken ct) =>
        throw new InvalidOperationException("No email provider is configured.");
}
