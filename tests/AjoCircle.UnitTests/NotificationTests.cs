using AjoCircle.Domain.Identity;
using AjoCircle.Domain.Notifications;

namespace AjoCircle.UnitTests;

public class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    private static Notification New() => Notification.Create(Guid.NewGuid(), NotificationType.ContributionRecorded, Guid.NewGuid(),
        "Ada recorded a payment", "₦50,000 paid", "/app/groups/x", null, Now);

    [Fact]
    public void New_notifications_are_unread_and_queued_for_email_now()
    {
        var n = New();
        Assert.Null(n.ReadAt);
        Assert.Equal(EmailDelivery.Pending, n.EmailStatus);
        Assert.Equal(Now, n.NextEmailAttemptAt);
    }

    [Fact]
    public void Failed_emails_back_off_then_stop_after_five_attempts()
    {
        var n = New();
        var expectedDelays = new[] { 1, 2, 4, 8 };
        foreach (var minutes in expectedDelays)
        {
            n.EmailFailed("smtp down", Now);
            Assert.Equal(EmailDelivery.Pending, n.EmailStatus);
            Assert.Equal(Now.AddMinutes(minutes), n.NextEmailAttemptAt);
        }
        n.EmailFailed("smtp down", Now);
        Assert.Equal(EmailDelivery.Failed, n.EmailStatus);
        Assert.Null(n.NextEmailAttemptAt);
        Assert.Equal(Notification.MaxEmailAttempts, n.EmailAttempts);
    }

    [Fact]
    public void Sent_and_read_are_recorded_once()
    {
        var n = New();
        n.EmailSent(Now);
        n.MarkRead(Now);
        n.MarkRead(Now.AddHours(1));
        Assert.Equal(EmailDelivery.Sent, n.EmailStatus);
        Assert.Equal(Now, n.ReadAt);
    }

    [Fact]
    public void Links_must_stay_inside_the_app_and_text_is_clipped()
    {
        var n = Notification.Create(Guid.NewGuid(), NotificationType.DisputeReply, null, new string('x', 500), "b", "https://evil.example/phish", null, Now);
        Assert.Equal("/app", n.LinkPath);
        Assert.Equal(Notification.MaxTitle, n.Title.Length);
    }

    [Fact]
    public void Reset_tokens_store_only_a_hash_and_expire_after_thirty_minutes()
    {
        var (token, raw) = PasswordResetToken.Create(Guid.NewGuid(), Now);
        Assert.NotEqual(raw, token.TokenHash);
        Assert.Equal(PasswordResetToken.Hash(raw), token.TokenHash);
        Assert.True(raw.Length >= 43);
        Assert.True(token.IsUsable(Now.AddMinutes(29)));
        Assert.False(token.IsUsable(Now.AddMinutes(30)));
        token.Consume(Now);
        Assert.False(token.IsUsable(Now));
    }
}
