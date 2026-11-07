using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AjoCircle.Application;
using Microsoft.Extensions.DependencyInjection;

namespace AjoCircle.ApiTests;

/// <summary>Slice 3: notifications (in-app + email outbox), reminders and password recovery.</summary>
[Collection("api")]
public sealed class NotificationsAndRecoveryTests(ApiFixture fx)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1)); // Lagos

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Expected {expected}, got {(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> f)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await f(scope.ServiceProvider);
    }

    private Task<int> Dispatch() => InScope(sp => sp.GetRequiredService<NotificationDispatcher>().DispatchAsync(500, default));
    private Task<int> Reminders() => InScope(sp => sp.GetRequiredService<ReminderScheduler>().RunAsync(default));

    private static async Task<string> EmailOf(HttpClient c) => (await Json(await c.GetAsync("/api/v1/me"), HttpStatusCode.OK)).GetProperty("email").GetString()!;

    private static async Task<JsonElement[]> Inbox(HttpClient c) =>
        (await Json(await c.GetAsync("/api/v1/me/notifications"), HttpStatusCode.OK)).GetProperty("items").EnumerateArray().ToArray();

    /// <summary>Organizer (not a member) + Ada + Bayo, started today, so round 1 is due today.</summary>
    private async Task<(Guid GroupId, HttpClient Org, HttpClient Ada, HttpClient Bayo)> StartedGroup(string name)
    {
        var org = await fx.VerifiedUser("Ngozi");
        var ada = await fx.VerifiedUser("Ada");
        var bayo = await fx.VerifiedUser("Bayo");
        var rules = new { contributionAmountMinor = 20_000_00L, currency = "NGN", frequency = "Weekly", memberCount = 2, startDate = Today, payoutOffsetDays = 0, payoutOrder = "JoinOrder" };
        var id = (await Json(await org.PostAsJsonAsync("/api/v1/groups", new { name, rules }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var version = (await Json(await org.PostAsync($"/api/v1/groups/{id}/publish", null), HttpStatusCode.OK)).GetProperty("currentSchemeVersionId").GetGuid();
        var token = (await Json(await org.PostAsJsonAsync($"/api/v1/groups/{id}/invitations", new { validityDays = 7 }), HttpStatusCode.Created)).GetProperty("token").GetString();
        foreach (var m in new[] { ada, bayo })
            await Json(await m.PostAsJsonAsync($"/api/v1/invitations/{token}/join", new { acceptedSchemeVersionId = version }), HttpStatusCode.Created);
        await Json(await org.PostAsync($"/api/v1/groups/{id}/start", null), HttpStatusCode.OK);
        return (id, org, ada, bayo);
    }

    [Fact]
    public async Task Lifecycle_and_record_events_notify_the_right_people_and_emails_carry_no_amounts()
    {
        var (groupId, org, ada, bayo) = await StartedGroup("Ikorodu Traders");

        // Organizer heard about both joins (the second one as "ready to start"); members heard the group started.
        var orgInbox = await Inbox(org);
        Assert.Contains(orgInbox, n => n.GetProperty("type").GetString() == "MemberJoined");
        Assert.Contains(orgInbox, n => n.GetProperty("type").GetString() == "GroupReadyToStart");
        var adaStarted = Assert.Single(await Inbox(ada), n => n.GetProperty("type").GetString() == "GroupStarted");
        Assert.Contains("round 1", adaStarted.GetProperty("body").GetString());

        // Organizer records Ada's payment → Ada (only) is notified, labelled as the organizer's report.
        var obligations = (await Json(await org.GetAsync($"/api/v1/groups/{groupId}/contributions"), HttpStatusCode.OK)).GetProperty("items").EnumerateArray();
        var adaObligation = obligations.First(o => o.GetProperty("memberName").GetString() == "Ada").GetProperty("id").GetGuid();
        var detail = await Json(await org.PostAsJsonAsync($"/api/v1/groups/{groupId}/contributions/{adaObligation}/records",
            new { amountMinor = 20_000_00L, paidOn = Today, method = "Cash", evidenceReference = "RCPT-42", note = (string?)null }), HttpStatusCode.Created);
        var recordId = detail.GetProperty("records")[0].GetProperty("id").GetGuid();

        var recorded = Assert.Single(await Inbox(ada), n => n.GetProperty("type").GetString() == "ContributionRecorded");
        Assert.Contains("Recorded by organizer", recorded.GetProperty("body").GetString());
        Assert.EndsWith($"/contributions/{adaObligation}", recorded.GetProperty("linkPath").GetString());
        Assert.DoesNotContain(await Inbox(bayo), n => n.GetProperty("type").GetString() == "ContributionRecorded");

        // Dispute → organizer notified; organizer reply → Ada notified.
        var dispute = await Json(await ada.PostAsJsonAsync($"/api/v1/contribution-records/{recordId}/dispute",
            new { category = "WrongAmount", description = "I gave you less than this, please check." }), HttpStatusCode.Created);
        var disputeId = dispute.GetProperty("summary").GetProperty("id").GetGuid();
        Assert.Contains(await Inbox(org), n => n.GetProperty("type").GetString() == "DisputeRaised");
        await Json(await org.PostAsJsonAsync($"/api/v1/disputes/{disputeId}/responses", new { body = "Checking my notebook." }), HttpStatusCode.OK);
        Assert.Contains(await Inbox(ada), n => n.GetProperty("type").GetString() == "DisputeReply");

        // Emails go out through the outbox; previews never contain amounts, references or dispute text.
        await Dispatch();
        var adaEmails = fx.Emails.To(await EmailOf(ada)).Where(m => m.Subject.StartsWith("AjoCircle:")).ToList();
        Assert.Contains(adaEmails, m => m.Subject.Contains("recorded a payment"));
        Assert.All(adaEmails, m =>
        {
            Assert.DoesNotContain("₦", m.Subject + m.TextBody);
            Assert.DoesNotContain("RCPT-42", m.TextBody);
            Assert.DoesNotContain("notebook", m.TextBody);
            Assert.Contains("/app/", m.TextBody);
        });
        Assert.Equal(0, await Dispatch()); // nothing is sent twice

        // Unread count and mark-as-read.
        var unread = (await Json(await ada.GetAsync("/api/v1/me/notifications/unread-count"), HttpStatusCode.OK)).GetProperty("unread").GetInt32();
        Assert.True(unread >= 3);
        await Json(await ada.PostAsync($"/api/v1/me/notifications/{recorded.GetProperty("id").GetGuid()}/read", null), HttpStatusCode.NoContent);
        Assert.Equal(unread - 1, (await Json(await ada.GetAsync("/api/v1/me/notifications/unread-count"), HttpStatusCode.OK)).GetProperty("unread").GetInt32());
        // Someone else's notification can't be touched.
        await Json(await bayo.PostAsync($"/api/v1/me/notifications/{recorded.GetProperty("id").GetGuid()}/read", null), HttpStatusCode.NotFound);
        await Json(await ada.PostAsync("/api/v1/me/notifications/read-all", null), HttpStatusCode.NoContent);
        Assert.Equal(0, (await Json(await ada.GetAsync("/api/v1/me/notifications/unread-count"), HttpStatusCode.OK)).GetProperty("unread").GetInt32());
    }

    [Fact]
    public async Task Opting_out_stops_notification_emails_but_keeps_them_in_the_app()
    {
        var (groupId, org, ada, _) = await StartedGroup("Opt-out Ajo");
        await Dispatch();
        var adaEmail = await EmailOf(ada);
        var before = fx.Emails.To(adaEmail).Count;

        var settings = await Json(await ada.PutAsJsonAsync("/api/v1/me/settings", new { emailNotifications = false }), HttpStatusCode.OK);
        Assert.False(settings.GetProperty("emailNotifications").GetBoolean());
        await org.PostAsJsonAsync($"/api/v1/groups/{groupId}/contributions/{(await Json(await ada.GetAsync($"/api/v1/groups/{groupId}/contributions"), HttpStatusCode.OK)).GetProperty("items")[0].GetProperty("id").GetGuid()}/records",
            new { amountMinor = 20_000_00L, paidOn = Today, method = "Cash", evidenceReference = (string?)null, note = (string?)null });
        await Dispatch();

        Assert.Equal(before, fx.Emails.To(adaEmail).Count);
        Assert.Contains(await Inbox(ada), n => n.GetProperty("type").GetString() == "ContributionRecorded");
    }

    [Fact]
    public async Task Reminders_are_created_once_and_say_no_payment_is_recorded()
    {
        var (_, _, ada, bayo) = await StartedGroup("Reminder Ajo");
        await Reminders();
        await Reminders(); // idempotent

        var adaInbox = await Inbox(ada);
        Assert.Single(adaInbox, n => n.GetProperty("type").GetString() == "ContributionDueSoon");
        Assert.Contains("today", Assert.Single(adaInbox, n => n.GetProperty("type").GetString() == "ContributionDueSoon").GetProperty("title").GetString());
        // Ada joined first, so round 1's payout (scheduled today) is hers.
        Assert.Single(adaInbox, n => n.GetProperty("type").GetString() == "PayoutApproaching");
        Assert.DoesNotContain(await Inbox(bayo), n => n.GetProperty("type").GetString() == "PayoutApproaching");
    }

    [Fact]
    public async Task Password_reset_works_once_signs_out_sessions_and_never_reveals_accounts()
    {
        var oldSession = await fx.VerifiedUser("Kemi");
        var email = await EmailOf(oldSession);
        var anon = fx.Client();

        // Unknown email: same response, no email.
        await Json(await anon.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "nobody.here@example.test" }), HttpStatusCode.Accepted);
        Assert.Empty(fx.Emails.To("nobody.here@example.test"));

        await Json(await anon.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = email.ToUpperInvariant() }), HttpStatusCode.Accepted);
        var token = fx.Emails.LatestResetToken(email);
        Assert.False(string.IsNullOrEmpty(token));

        // Weak password is rejected without burning the link.
        await Json(await anon.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword = "short" }), HttpStatusCode.BadRequest);
        await Json(await anon.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword = "a-brand-new-passphrase" }), HttpStatusCode.NoContent);

        // The link is single-use, every old session is signed out, and the new password works.
        await Json(await anon.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword = "another-new-passphrase" }), HttpStatusCode.UnprocessableEntity);
        await Json(await oldSession.GetAsync("/api/v1/me"), HttpStatusCode.Unauthorized);
        await Json(await anon.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "correct-horse-battery" }), HttpStatusCode.Unauthorized);
        await Json(await anon.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "a-brand-new-passphrase" }), HttpStatusCode.OK);
        Assert.Contains(fx.Emails.To(email), m => m.Subject.Contains("password was changed"));

        // A made-up token gets the same answer as an expired one.
        await Json(await anon.PostAsJsonAsync("/api/v1/auth/reset-password", new { token = "not-a-real-token", newPassword = "a-brand-new-passphrase" }), HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Reset_emails_are_capped_per_account_per_hour()
    {
        var c = await fx.VerifiedUser("Tolu");
        var email = await EmailOf(c);
        for (var i = 0; i < 5; i++)
            await Json(await fx.Client().PostAsJsonAsync("/api/v1/auth/forgot-password", new { email }), HttpStatusCode.Accepted);
        Assert.Equal(3, fx.Emails.To(email).Count(m => m.Subject.Contains("Reset")));
    }
}
