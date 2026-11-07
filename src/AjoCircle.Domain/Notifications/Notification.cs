namespace AjoCircle.Domain.Notifications;

/// <summary>Brief §14.1 events that exist in the product so far.</summary>
public enum NotificationType
{
    MemberJoined, GroupReadyToStart, GroupStarted, GroupCancelled,
    ContributionRecorded, ContributionCorrected, PayoutRecorded, PayoutCorrected,
    DisputeRaised, DisputeReply, DisputeResolved,
    ContributionDueSoon, ContributionOverdue, PayoutApproaching,
    MemberExited, SavingsCompleted,
}

/// <summary>Email delivery state. The notification row doubles as the outbox entry (brief §10.5).</summary>
public enum EmailDelivery { Pending, Sent, Failed, Skipped }

/// <summary>
/// A message for one user, written in the same transaction as the change that caused it, so a notification exists if and
/// only if the change committed. <see cref="Title"/> is safe for email subjects and previews (no amounts, references or
/// dispute text — brief §11.2); <see cref="Body"/> is shown only inside the signed-in app.
/// </summary>
public sealed class Notification
{
    public const int MaxEmailAttempts = 5;
    public const int MaxTitle = 160;
    public const int MaxBody = 600;

    private Notification() { }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public NotificationType Type { get; private set; }
    public Guid? GroupId { get; private set; }
    public string Title { get; private set; } = "";
    public string Body { get; private set; } = "";
    /// <summary>App-relative path, e.g. /app/groups/{id}/contributions/{id}.</summary>
    public string LinkPath { get; private set; } = "";
    /// <summary>Makes an event idempotent per user: the same key is never notified twice (unique index).</summary>
    public string? DedupeKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public EmailDelivery EmailStatus { get; private set; }
    public int EmailAttempts { get; private set; }
    public DateTimeOffset? NextEmailAttemptAt { get; private set; }
    public DateTimeOffset? EmailSentAt { get; private set; }
    public string? EmailLastError { get; private set; }

    public static Notification Create(Guid userId, NotificationType type, Guid? groupId, string title, string body, string linkPath,
        string? dedupeKey, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now), UserId = userId, Type = type, GroupId = groupId,
        Title = Clip(title, MaxTitle), Body = Clip(body, MaxBody), LinkPath = linkPath.StartsWith('/') ? linkPath : "/app",
        DedupeKey = dedupeKey, CreatedAt = now, EmailStatus = EmailDelivery.Pending, NextEmailAttemptAt = now,
    };

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;

    public void EmailSent(DateTimeOffset now)
    {
        EmailStatus = EmailDelivery.Sent;
        EmailSentAt = now;
        EmailAttempts++;
        NextEmailAttemptAt = null;
        EmailLastError = null;
    }

    /// <summary>The user opted out, or has no verified address. The in-app notification still stands.</summary>
    public void EmailSkipped() { EmailStatus = EmailDelivery.Skipped; NextEmailAttemptAt = null; }

    /// <summary>Exponential backoff (1, 2, 4, 8 minutes), then give up. The in-app copy is unaffected.</summary>
    public void EmailFailed(string error, DateTimeOffset now)
    {
        EmailAttempts++;
        EmailLastError = Clip(error, 300);
        if (EmailAttempts >= MaxEmailAttempts) { EmailStatus = EmailDelivery.Failed; NextEmailAttemptAt = null; }
        else NextEmailAttemptAt = now.AddMinutes(Math.Pow(2, EmailAttempts - 1));
    }

    private static string Clip(string s, int max)
    {
        s = (s ?? "").Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }
}
