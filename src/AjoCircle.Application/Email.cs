namespace AjoCircle.Application;

public sealed record EmailMessage(string To, string ToName, string Subject, string TextBody);

/// <summary>Sends one plain-text email. The vendor is an open decision (brief §16.3); SMTP is the current adapter.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>Product-level settings the application needs for user-facing text and links.</summary>
public sealed class AppOptions
{
    public const string Section = "App";
    /// <summary>Working brand name (brief §1.5); kept in config so a rename doesn't touch code.</summary>
    public string ProductName { get; init; } = "AjoCircle";
    /// <summary>Where the web app is served, used to build links in emails. No trailing slash.</summary>
    public string PublicBaseUrl { get; init; } = "http://localhost:5173";
}

/// <summary>
/// Email wording. Security emails carry the code or link they exist for; notification emails carry only the safe title
/// and a link, so amounts, references and dispute text never sit in an inbox preview (brief §11.2).
/// </summary>
public sealed class EmailTemplates(AppOptions app)
{
    private string Footer => $"{app.ProductName} records ajo activity. It never holds your money and never asks for your bank PIN, password or card details.";

    public EmailMessage VerificationCode(string email, string name, string code) => new(email, name,
        $"Your {app.ProductName} verification code",
        $"""
        Hi {name},

        Your {app.ProductName} verification code is: {code}

        It expires in 15 minutes. If you didn't create an account, you can ignore this email.

        {Footer}
        """);

    public EmailMessage PasswordReset(string email, string name, string rawToken) => new(email, name,
        $"Reset your {app.ProductName} password",
        $"""
        Hi {name},

        Someone asked to reset the password for your {app.ProductName} account. To choose a new password, open this link:

        {app.PublicBaseUrl}/reset-password?token={rawToken}

        The link works once and expires in 30 minutes. If you didn't ask for this, ignore this email — your password won't change.

        {Footer}
        """);

    public EmailMessage PasswordChanged(string email, string name) => new(email, name,
        $"Your {app.ProductName} password was changed",
        $"""
        Hi {name},

        The password for your {app.ProductName} account was just changed, and every device was signed out.

        If this wasn't you, reset your password straight away at {app.PublicBaseUrl}/forgot-password.

        {Footer}
        """);

    public EmailMessage Notification(string email, string name, string title, string linkPath) => new(email, name,
        $"{app.ProductName}: {title}",
        $"""
        Hi {name},

        {title}

        Open {app.ProductName} to see the details:
        {app.PublicBaseUrl}{linkPath}

        You can turn off these emails in Settings: {app.PublicBaseUrl}/app/settings

        {Footer}
        """);
}
