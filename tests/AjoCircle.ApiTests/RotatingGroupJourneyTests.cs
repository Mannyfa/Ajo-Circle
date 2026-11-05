using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace AjoCircle.ApiTests;

[Collection("api")]
public sealed class RotatingGroupJourneyTests(ApiFixture fx)
{
    private static DateOnly StartDate => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1)).AddDays(7);

    private static object Rules(int members, string order = "JoinOrder") => new
    {
        contributionAmountMinor = 100_000_00L,
        currency = "NGN",
        frequency = "Monthly",
        memberCount = members,
        startDate = StartDate,
        payoutOffsetDays = 0,
        payoutOrder = order,
    };

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Expected {expected}, got {(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<(Guid GroupId, Guid VersionId, string Token)> PublishedGroupWithInvite(HttpClient organizer, int members, string order = "JoinOrder")
    {
        var created = await Json(await organizer.PostAsJsonAsync("/api/v1/groups", new { name = "Yaba Traders Ajo", rules = Rules(members, order) }), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        var published = await Json(await organizer.PostAsync($"/api/v1/groups/{id}/publish", null), HttpStatusCode.OK);
        Assert.Equal("OpenForJoining", published.GetProperty("status").GetString());
        var invite = await Json(await organizer.PostAsJsonAsync($"/api/v1/groups/{id}/invitations", new { validityDays = 7 }), HttpStatusCode.Created);
        return (id, published.GetProperty("currentSchemeVersionId").GetGuid(), invite.GetProperty("token").GetString()!);
    }

    [Fact]
    public async Task Organizer_creates_publishes_members_join_and_group_starts_with_correct_schedule()
    {
        var organizer = await fx.VerifiedUser("Organizer");
        var (groupId, versionId, token) = await PublishedGroupWithInvite(organizer, 3);

        var members = new List<HttpClient>();
        for (var i = 0; i < 3; i++)
        {
            var m = await fx.VerifiedUser($"Member{i}");
            var view = await Json(await m.GetAsync($"/api/v1/invitations/{token}"), HttpStatusCode.OK);
            Assert.True(view.GetProperty("canJoin").GetBoolean());
            Assert.Equal(300_000_00, view.GetProperty("summary").GetProperty("expectedPoolPerRound").GetProperty("amountMinor").GetInt64());
            await Json(await m.PostAsJsonAsync($"/api/v1/invitations/{token}/join", new { acceptedSchemeVersionId = versionId }), HttpStatusCode.Created);
            members.Add(m);
        }

        var ready = await Json(await organizer.GetAsync($"/api/v1/groups/{groupId}"), HttpStatusCode.OK);
        Assert.Equal("ReadyToStart", ready.GetProperty("status").GetString());

        // A 4th person cannot join a full group.
        var late = await fx.VerifiedUser("Late");
        await Json(await late.PostAsJsonAsync($"/api/v1/invitations/{token}/join", new { acceptedSchemeVersionId = versionId }), HttpStatusCode.Conflict);

        var started = await Json(await organizer.PostAsync($"/api/v1/groups/{groupId}/start", null), HttpStatusCode.OK);
        Assert.Equal("Active", started.GetProperty("status").GetString());

        var rounds = await Json(await organizer.GetAsync($"/api/v1/groups/{groupId}/rounds"), HttpStatusCode.OK);
        Assert.Equal(3, rounds.GetArrayLength());
        Assert.All(rounds.EnumerateArray(), r => Assert.Equal("Scheduled", r.GetProperty("payoutStatus").GetString()));

        var all = await Json(await organizer.GetAsync($"/api/v1/groups/{groupId}/contributions"), HttpStatusCode.OK);
        Assert.Equal(9, all.GetProperty("total").GetInt32());
        Assert.All(all.GetProperty("items").EnumerateArray(), o => Assert.Equal("Due", o.GetProperty("status").GetString()));

        // A member sees only their own obligations, and no other members' emails.
        var mine = await Json(await members[0].GetAsync($"/api/v1/groups/{groupId}/contributions"), HttpStatusCode.OK);
        Assert.Equal(3, mine.GetProperty("total").GetInt32());
        var roster = await Json(await members[0].GetAsync($"/api/v1/groups/{groupId}/members"), HttpStatusCode.OK);
        Assert.All(roster.EnumerateArray(), m => Assert.Equal(JsonValueKind.Null, m.GetProperty("email").ValueKind));

        var detail = await Json(await members[0].GetAsync($"/api/v1/groups/{groupId}"), HttpStatusCode.OK);
        Assert.Equal(1, detail.GetProperty("myMembership").GetProperty("payoutPosition").GetInt32());
        Assert.Equal(1, detail.GetProperty("myMembership").GetProperty("acceptedSchemeVersion").GetInt32());

        var audit = await Json(await organizer.GetAsync($"/api/v1/groups/{groupId}/audit-logs"), HttpStatusCode.OK);
        var actions = audit.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("action").GetString()).ToList();
        Assert.Contains("group.started", actions);
        Assert.Contains("membership.joined", actions);
        Assert.DoesNotContain(token, audit.ToString()); // raw invitation tokens are never logged
        // Automatic closure when the group fills is a system action, not attributed to whoever joined last.
        var autoClosed = audit.GetProperty("items").EnumerateArray().Single(a => a.GetProperty("action").GetString() == "group.enrolment_closed");
        Assert.Equal(JsonValueKind.Null, autoClosed.GetProperty("actorName").ValueKind);
    }

    [Fact]
    public async Task Another_organizer_cannot_see_or_change_a_group_by_guessing_its_id()
    {
        var owner = await fx.VerifiedUser("Owner");
        var (groupId, _, _) = await PublishedGroupWithInvite(owner, 3);
        var intruder = await fx.VerifiedUser("Intruder");

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/v1/groups/{groupId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/v1/groups/{groupId}/members")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/v1/groups/{groupId}/invitations")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsJsonAsync($"/api/v1/groups/{groupId}/invitations", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsJsonAsync($"/api/v1/groups/{groupId}/cancel", new { reason = "hijack attempt" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/v1/groups/{groupId}/audit-logs")).StatusCode);
        var list = await Json(await intruder.GetAsync("/api/v1/groups"), HttpStatusCode.OK);
        Assert.Equal(0, list.GetArrayLength());
    }

    [Fact]
    public async Task Members_cannot_run_organizer_commands()
    {
        var organizer = await fx.VerifiedUser("Org");
        var (groupId, versionId, token) = await PublishedGroupWithInvite(organizer, 2);
        var member = await fx.VerifiedUser("Mem");
        await Json(await member.PostAsJsonAsync($"/api/v1/invitations/{token}/join", new { acceptedSchemeVersionId = versionId }), HttpStatusCode.Created);

        Assert.Equal(HttpStatusCode.NotFound, (await member.PostAsync($"/api/v1/groups/{groupId}/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.PostAsync($"/api/v1/groups/{groupId}/close-enrolment", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/v1/groups/{groupId}/audit-logs")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/v1/groups/{groupId}")).StatusCode);
    }

    [Fact]
    public async Task Public_invitation_view_exposes_no_member_data_and_revoked_links_cannot_join()
    {
        var organizer = await fx.VerifiedUser("Host");
        var (groupId, versionId, token) = await PublishedGroupWithInvite(organizer, 5);
        var joiner = await fx.VerifiedUser("Joiner");
        await Json(await joiner.PostAsJsonAsync($"/api/v1/invitations/{token}/join", new { acceptedSchemeVersionId = versionId }), HttpStatusCode.Created);

        var anon = fx.Client();
        var raw = await (await anon.GetAsync($"/api/v1/invitations/{token}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("@example.test", raw);
        Assert.DoesNotContain("Joiner", raw);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/v1/invitations/not-a-real-token")).StatusCode);

        var invites = await Json(await organizer.GetAsync($"/api/v1/groups/{groupId}/invitations"), HttpStatusCode.OK);
        var inviteId = invites[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await organizer.DeleteAsync($"/api/v1/groups/{groupId}/invitations/{inviteId}")).StatusCode);

        var other = await fx.VerifiedUser("Other");
        var view = await Json(await other.GetAsync($"/api/v1/invitations/{token}"), HttpStatusCode.OK);
        Assert.False(view.GetProperty("canJoin").GetBoolean());
        await Json(await other.PostAsJsonAsync($"/api/v1/invitations/{token}/join", new { acceptedSchemeVersionId = versionId }), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Joining_requires_the_exact_current_terms_version_and_is_not_duplicated()
    {
        var organizer = await fx.VerifiedUser("Terms");
        var (_, versionId, token) = await PublishedGroupWithInvite(organizer, 4);
        var m = await fx.VerifiedUser("Careful");

        var stale = await Json(await m.PostAsJsonAsync($"/api/v1/invitations/{token}/join", new { acceptedSchemeVersionId = Guid.NewGuid() }), HttpStatusCode.Conflict);
        Assert.Equal("terms_changed", stale.GetProperty("code").GetString());

        await Json(await m.PostAsJsonAsync($"/api/v1/invitations/{token}/join", new { acceptedSchemeVersionId = versionId }), HttpStatusCode.Created);
        var dup = await Json(await m.PostAsJsonAsync($"/api/v1/invitations/{token}/join", new { acceptedSchemeVersionId = versionId }), HttpStatusCode.Conflict);
        Assert.Equal("already_member", dup.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Published_terms_cannot_be_edited()
    {
        var organizer = await fx.VerifiedUser("Editor");
        var (groupId, _, _) = await PublishedGroupWithInvite(organizer, 3);
        var r = await organizer.PatchAsJsonAsync($"/api/v1/groups/{groupId}", new { name = "Changed", rules = Rules(6) });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task Organizer_assigned_payout_order_must_cover_every_member_once()
    {
        var organizer = await fx.VerifiedUser("Assigner");
        var (groupId, versionId, token) = await PublishedGroupWithInvite(organizer, 2, "OrganizerAssigned");
        for (var i = 0; i < 2; i++)
        {
            var m = await fx.VerifiedUser($"P{i}");
            await Json(await m.PostAsJsonAsync($"/api/v1/invitations/{token}/join", new { acceptedSchemeVersionId = versionId }), HttpStatusCode.Created);
        }
        // No positions yet, so the group cannot start.
        await Json(await organizer.PostAsync($"/api/v1/groups/{groupId}/start", null), HttpStatusCode.BadRequest);

        var roster = await Json(await organizer.GetAsync($"/api/v1/groups/{groupId}/members"), HttpStatusCode.OK);
        var ids = roster.EnumerateArray().Select(m => m.GetProperty("membershipId").GetGuid()).ToList();
        await Json(await organizer.PutAsJsonAsync($"/api/v1/groups/{groupId}/payout-positions", new { membershipIds = new[] { ids[0], ids[0] } }), HttpStatusCode.BadRequest);

        var assigned = await Json(await organizer.PutAsJsonAsync($"/api/v1/groups/{groupId}/payout-positions", new { membershipIds = new[] { ids[1], ids[0] } }), HttpStatusCode.OK);
        Assert.Equal(1, assigned.EnumerateArray().Single(m => m.GetProperty("membershipId").GetGuid() == ids[1]).GetProperty("payoutPosition").GetInt32());
        await Json(await organizer.PostAsync($"/api/v1/groups/{groupId}/start", null), HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unverified_users_cannot_create_groups()
    {
        var c = fx.Client();
        (await c.PostAsJsonAsync("/api/v1/auth/register", new { email = $"u{Guid.NewGuid():N}@example.test", displayName = "Unverified", password = "correct-horse-battery", acceptTerms = true }))
            .EnsureSuccessStatusCode();
        var r = await Json(await c.PostAsJsonAsync("/api/v1/groups", new { name = "Nope", rules = Rules(3) }), (HttpStatusCode)422);
        Assert.Equal("contact_unverified", r.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Requests_without_session_or_csrf_header_are_rejected()
    {
        var anon = fx.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/groups")).StatusCode);

        var noHeader = fx.Factory.CreateClient();
        var r = await noHeader.PostAsJsonAsync("/api/v1/auth/login", new { email = "a@example.test", password = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Login_does_not_reveal_whether_an_account_exists()
    {
        var c = fx.Client();
        var r1 = await c.PostAsJsonAsync("/api/v1/auth/login", new { email = "nobody@example.test", password = "whatever-password" });
        var email = $"real{Guid.NewGuid():N}@example.test";
        (await fx.Client().PostAsJsonAsync("/api/v1/auth/register", new { email, displayName = "Real", password = "correct-horse-battery", acceptTerms = true })).EnsureSuccessStatusCode();
        var r2 = await c.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "wrong-password-123" });
        Assert.Equal(HttpStatusCode.Unauthorized, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, r2.StatusCode);
        Assert.Equal(await r1.Content.ReadAsStringAsync().ContinueWith(t => JsonDocument.Parse(t.Result).RootElement.GetProperty("title").GetString()),
                     await r2.Content.ReadAsStringAsync().ContinueWith(t => JsonDocument.Parse(t.Result).RootElement.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task Audit_log_rows_cannot_be_deleted_or_updated_at_database_level()
    {
        await fx.VerifiedUser("Auditor"); // ensures at least one audit row exists
        await using var conn = new NpgsqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using var del = new NpgsqlCommand("DELETE FROM audit_logs", conn);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => del.ExecuteNonQueryAsync());
        Assert.Contains("ajo_append_only", ex.MessageText);
        await using var upd = new NpgsqlCommand("UPDATE audit_logs SET action = 'tampered'", conn);
        await Assert.ThrowsAsync<PostgresException>(() => upd.ExecuteNonQueryAsync());
    }
}
