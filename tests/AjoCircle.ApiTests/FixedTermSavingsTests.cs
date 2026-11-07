using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AjoCircle.ApiTests;

/// <summary>Slice 4: fixed-term savings under the owner's decisions (organizer-chosen split, refunds on exit/cancel).</summary>
[Collection("api")]
public sealed class FixedTermSavingsTests(ApiFixture fx)
{
    private const long C = 10_000_00L; // ₦10,000 per period
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1)); // Lagos

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Expected {expected}, got {(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static object Rules(string variant, string allocation, int periods = 1, int members = 5) => new
    {
        variant, contributionAmountMinor = C, currency = "NGN", frequency = "Weekly", memberCount = members, periods,
        startDate = Today, releaseOffsetDays = 0, allocation, goal = "Christmas shopping",
    };

    private sealed record Ctx(Guid GroupId, Guid VersionId, string Token, HttpClient Org, HttpClient[] Members);

    /// <summary>Organizer (not a member) publishes; the given members join; organizer closes enrolment.</summary>
    private async Task<Ctx> Ready(string variant, string allocation, int periods = 1, int join = 3, string name = "Lekki Savers")
    {
        var org = await fx.VerifiedUser("Funke");
        var g = await Json(await org.PostAsJsonAsync("/api/v1/groups/fixed-term", new { name, rules = Rules(variant, allocation, periods) }), HttpStatusCode.Created);
        Assert.Equal(variant == "Individual" ? "FixedTermIndividual" : "FixedTermShared", g.GetProperty("schemeType").GetString());
        Assert.Equal(JsonValueKind.Null, g.GetProperty("rules").ValueKind);
        var id = g.GetProperty("id").GetGuid();
        var version = (await Json(await org.PostAsync($"/api/v1/groups/{id}/publish", null), HttpStatusCode.OK)).GetProperty("currentSchemeVersionId").GetGuid();
        var token = (await Json(await org.PostAsJsonAsync($"/api/v1/groups/{id}/invitations", new { validityDays = 7 }), HttpStatusCode.Created)).GetProperty("token").GetString()!;

        var invite = await Json(await fx.Client().GetAsync($"/api/v1/invitations/{token}"), HttpStatusCode.OK);
        Assert.Equal(allocation, invite.GetProperty("fixedTermRules").GetProperty("allocation").GetString());
        Assert.Equal(C * periods, invite.GetProperty("fixedTermSummary").GetProperty("targetPerMember").GetProperty("amountMinor").GetInt64());

        var members = new List<HttpClient>();
        foreach (var n in new[] { "Amaka", "Bola", "Chidi", "Dayo" }.Take(join))
        {
            var m = await fx.VerifiedUser(n);
            await Json(await m.PostAsJsonAsync($"/api/v1/invitations/{token}/join", new { acceptedSchemeVersionId = version }), HttpStatusCode.Created);
            members.Add(m);
        }
        await Json(await org.PostAsync($"/api/v1/groups/{id}/close-enrolment", null), HttpStatusCode.OK);
        return new Ctx(id, version, token, org, members.ToArray());
    }

    private static async Task Start(Ctx c) => await Json(await c.Org.PostAsync($"/api/v1/groups/{c.GroupId}/start", null), HttpStatusCode.OK);

    private static async Task<JsonElement[]> Obligations(HttpClient who, Guid groupId) =>
        (await Json(await who.GetAsync($"/api/v1/groups/{groupId}/contributions?pageSize=200"), HttpStatusCode.OK)).GetProperty("items").EnumerateArray().ToArray();

    private static async Task<Guid> RecordPaid(Ctx c, string member, int round = 1)
    {
        var o = (await Obligations(c.Org, c.GroupId)).Single(x => x.GetProperty("memberName").GetString() == member && x.GetProperty("roundNumber").GetInt32() == round);
        var d = await Json(await c.Org.PostAsJsonAsync($"/api/v1/groups/{c.GroupId}/contributions/{o.GetProperty("id").GetGuid()}/records",
            new { amountMinor = C, paidOn = Today, method = "Cash", evidenceReference = (string?)null, note = (string?)null }), HttpStatusCode.Created);
        return d.GetProperty("records")[0].GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement[]> Payouts(HttpClient who, Guid groupId) =>
        (await Json(await who.GetAsync($"/api/v1/groups/{groupId}/payouts"), HttpStatusCode.OK)).EnumerateArray().ToArray();

    [Fact]
    public async Task Individual_savings_start_short_of_the_limit_and_release_each_members_own_money()
    {
        var c = await Ready("Individual", "OwnContributions", periods: 1, join: 3);
        await Start(c); // 3 of 5 is fine for savings
        Assert.Equal(3, (await Obligations(c.Org, c.GroupId)).Length);

        await RecordPaid(c, "Amaka");
        await RecordPaid(c, "Bola");
        var amakaGroup = await Json(await c.Members[0].GetAsync($"/api/v1/groups/{c.GroupId}"), HttpStatusCode.OK);
        var mine = amakaGroup.GetProperty("savings");
        Assert.True(mine.GetProperty("forYou").GetBoolean());
        Assert.Equal(C, mine.GetProperty("recorded").GetProperty("amountMinor").GetInt64());
        Assert.Contains("leave", amakaGroup.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));

        // Members can't complete; the organizer can once the last contribution date has arrived (period 1 = today).
        await Json(await c.Members[0].PostAsync($"/api/v1/groups/{c.GroupId}/complete", null), HttpStatusCode.NotFound);
        var done = await Json(await c.Org.PostAsync($"/api/v1/groups/{c.GroupId}/complete", null), HttpStatusCode.OK);
        Assert.Equal("Completed", done.GetProperty("status").GetString());

        var all = await Payouts(c.Org, c.GroupId);
        Assert.Equal(2, all.Length); // Chidi paid nothing, so nothing is owed to him
        Assert.All(all, p => { Assert.Equal("Release", p.GetProperty("kind").GetString()); Assert.Equal(C, p.GetProperty("amount").GetProperty("amountMinor").GetInt64()); });

        // Releases are private: each member sees only their own.
        var amakas = Assert.Single(await Payouts(c.Members[0], c.GroupId));
        Assert.True(amakas.GetProperty("isYours").GetBoolean());
        Assert.Empty(await Payouts(c.Members[2], c.GroupId));

        // After completion the organizer records paying it back; the member confirms.
        var pid = amakas.GetProperty("id").GetGuid();
        await Json(await c.Members[1].GetAsync($"/api/v1/groups/{c.GroupId}/payouts/{pid}"), HttpStatusCode.NotFound);
        var detail = await Json(await c.Org.PostAsJsonAsync($"/api/v1/groups/{c.GroupId}/payouts/{pid}/records",
            new { amountMinor = C, paidOn = Today, method = "BankTransfer", evidenceReference = "TRF-REL-1", note = (string?)null }), HttpStatusCode.Created);
        Assert.Equal("Release", detail.GetProperty("kind").GetString());
        await Json(await c.Members[0].PostAsync($"/api/v1/payout-records/{detail.GetProperty("records")[0].GetProperty("id").GetGuid()}/confirm", null), HttpStatusCode.NoContent);
        Assert.Equal("ConfirmedByRecipient", (await Json(await c.Members[0].GetAsync($"/api/v1/groups/{c.GroupId}/payouts/{pid}"), HttpStatusCode.OK)).GetProperty("status").GetString());

        // Contributions can't be recorded once the term is over.
        var chidi = (await Obligations(c.Org, c.GroupId)).Single(o => o.GetProperty("memberName").GetString() == "Chidi").GetProperty("id").GetGuid();
        await Json(await c.Org.PostAsJsonAsync($"/api/v1/groups/{c.GroupId}/contributions/{chidi}/records",
            new { amountMinor = C, paidOn = Today, method = "Cash", evidenceReference = (string?)null, note = (string?)null }), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Shared_equal_split_divides_the_recorded_total_to_the_kobo()
    {
        var c = await Ready("Shared", "Equal", periods: 1, join: 3);
        await Start(c);
        await RecordPaid(c, "Amaka");
        await RecordPaid(c, "Bola");
        await Json(await c.Org.PostAsync($"/api/v1/groups/{c.GroupId}/complete", null), HttpStatusCode.OK);
        var amounts = (await Payouts(c.Org, c.GroupId)).Select(p => p.GetProperty("amount").GetProperty("amountMinor").GetInt64()).OrderDescending().ToArray();
        Assert.Equal(new[] { 666_667L, 666_667L, 666_666L }, amounts); // ₦20,000 three ways, nothing lost
    }

    [Fact]
    public async Task Organizer_assigned_shares_are_required_before_start_and_drive_the_release()
    {
        var c = await Ready("Shared", "OrganizerAssigned", periods: 1, join: 2);
        var detail = await Json(await c.Org.GetAsync($"/api/v1/groups/{c.GroupId}"), HttpStatusCode.OK);
        Assert.Contains("assignShares", detail.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));
        await Json(await c.Org.PostAsync($"/api/v1/groups/{c.GroupId}/start", null), HttpStatusCode.BadRequest);

        var members = (await Json(await c.Org.GetAsync($"/api/v1/groups/{c.GroupId}/members"), HttpStatusCode.OK)).EnumerateArray().ToArray();
        var amaka = members.Single(m => m.GetProperty("displayName").GetString() == "Amaka").GetProperty("membershipId").GetGuid();
        var bola = members.Single(m => m.GetProperty("displayName").GetString() == "Bola").GetProperty("membershipId").GetGuid();
        await Json(await c.Org.PutAsJsonAsync($"/api/v1/groups/{c.GroupId}/shares",
            new { shares = new[] { new { membershipId = amaka, basisPoints = 7_000 }, new { membershipId = bola, basisPoints = 2_000 } } }), HttpStatusCode.BadRequest);
        await Json(await c.Members[0].PutAsJsonAsync($"/api/v1/groups/{c.GroupId}/shares",
            new { shares = new[] { new { membershipId = amaka, basisPoints = 10_000 }, new { membershipId = bola, basisPoints = 0 } } }), HttpStatusCode.NotFound);
        var saved = await Json(await c.Org.PutAsJsonAsync($"/api/v1/groups/{c.GroupId}/shares",
            new { shares = new[] { new { membershipId = amaka, basisPoints = 7_500 }, new { membershipId = bola, basisPoints = 2_500 } } }), HttpStatusCode.OK);
        Assert.Equal(7_500, saved.EnumerateArray().Single(m => m.GetProperty("membershipId").GetGuid() == amaka).GetProperty("shareBasisPoints").GetInt32());

        await Start(c);
        await RecordPaid(c, "Amaka");
        await RecordPaid(c, "Bola");
        await Json(await c.Org.PostAsync($"/api/v1/groups/{c.GroupId}/complete", null), HttpStatusCode.OK);
        var byMember = (await Payouts(c.Org, c.GroupId)).ToDictionary(p => p.GetProperty("recipientMembershipId").GetGuid(), p => p.GetProperty("amount").GetProperty("amountMinor").GetInt64());
        Assert.Equal(15_000_00, byMember[amaka]);
        Assert.Equal(5_000_00, byMember[bola]);
    }

    [Fact]
    public async Task Leaving_early_refunds_what_was_paid_and_drops_future_contributions()
    {
        var c = await Ready("Individual", "OwnContributions", periods: 2, join: 2);
        await Start(c);
        var recordId = await RecordPaid(c, "Amaka");
        var detail = await Json(await c.Members[0].GetAsync($"/api/v1/groups/{c.GroupId}"), HttpStatusCode.OK);
        var amakaMembership = detail.GetProperty("myMembership").GetProperty("membershipId").GetGuid();

        // Completing early is refused: the term isn't over.
        Assert.Equal("term_not_finished", (await Json(await c.Org.PostAsync($"/api/v1/groups/{c.GroupId}/complete", null), HttpStatusCode.Conflict)).GetProperty("code").GetString());

        // Bola can't remove Amaka; an open dispute blocks the exit until it's resolved.
        await Json(await c.Members[1].PostAsJsonAsync($"/api/v1/groups/{c.GroupId}/members/{amakaMembership}/exit", new { reason = "She asked me to" }), HttpStatusCode.Forbidden);
        var dispute = await Json(await c.Members[0].PostAsJsonAsync($"/api/v1/contribution-records/{recordId}/dispute",
            new { category = "WrongDate", description = "I paid yesterday, not today." }), HttpStatusCode.Created);
        Assert.Equal("disputes_open", (await Json(await c.Members[0].PostAsJsonAsync($"/api/v1/groups/{c.GroupId}/members/{amakaMembership}/exit",
            new { reason = "Relocating to Abuja" }), HttpStatusCode.Conflict)).GetProperty("code").GetString());
        await Json(await c.Members[0].PostAsJsonAsync($"/api/v1/disputes/{dispute.GetProperty("summary").GetProperty("id").GetGuid()}/withdraw", new { note = "Fine" }), HttpStatusCode.OK);

        var after = await Json(await c.Members[0].PostAsJsonAsync($"/api/v1/groups/{c.GroupId}/members/{amakaMembership}/exit",
            new { reason = "Relocating to Abuja" }), HttpStatusCode.OK);
        Assert.Equal("Exited", after.GetProperty("myMembership").GetProperty("status").GetString());
        Assert.Empty(after.GetProperty("allowedActions").EnumerateArray());

        var mine = await Obligations(c.Members[0], c.GroupId);
        Assert.Equal(new[] { "ConfirmedByMember", "Cancelled" }, mine.OrderBy(o => o.GetProperty("roundNumber").GetInt32()).Select(o => o.GetProperty("status").GetString()));
        var refund = Assert.Single(await Payouts(c.Members[0], c.GroupId));
        Assert.Equal("ExitRefund", refund.GetProperty("kind").GetString());
        Assert.Equal(C, refund.GetProperty("amount").GetProperty("amountMinor").GetInt64());

        // The organizer heard about it; exiting twice is refused.
        var orgInbox = (await Json(await c.Org.GetAsync("/api/v1/me/notifications"), HttpStatusCode.OK)).GetProperty("items").EnumerateArray();
        Assert.Contains(orgInbox, n => n.GetProperty("type").GetString() == "MemberExited");
        await Json(await c.Org.PostAsJsonAsync($"/api/v1/groups/{c.GroupId}/members/{amakaMembership}/exit", new { reason = "Again please" }), HttpStatusCode.Conflict);

        // The organizer's roster shows her as exited; Bola's group view counts one active member.
        var roster = (await Json(await c.Org.GetAsync($"/api/v1/groups/{c.GroupId}/members"), HttpStatusCode.OK)).EnumerateArray();
        Assert.Contains(roster, m => m.GetProperty("status").GetString() == "Exited" && m.GetProperty("exitReason").GetString() == "Relocating to Abuja");
        Assert.Equal(1, (await Json(await c.Members[1].GetAsync($"/api/v1/groups/{c.GroupId}"), HttpStatusCode.OK)).GetProperty("activeMemberCount").GetInt32());
    }

    [Fact]
    public async Task Cancelling_a_running_savings_group_refunds_each_member_what_they_paid()
    {
        var c = await Ready("Shared", "Equal", periods: 2, join: 3);
        await Start(c);
        await RecordPaid(c, "Amaka");
        await RecordPaid(c, "Bola");

        await Json(await c.Org.PostAsJsonAsync($"/api/v1/groups/{c.GroupId}/cancel", new { reason = "Members agreed to stop" }), HttpStatusCode.OK);
        var refunds = await Payouts(c.Org, c.GroupId);
        Assert.Equal(2, refunds.Length);
        Assert.All(refunds, r => { Assert.Equal("CancellationRefund", r.GetProperty("kind").GetString()); Assert.Equal(C, r.GetProperty("amount").GetProperty("amountMinor").GetInt64()); });
        Assert.DoesNotContain(await Obligations(c.Org, c.GroupId), o => o.GetProperty("status").GetString() == "Due");

        var amakaInbox = (await Json(await c.Members[0].GetAsync("/api/v1/me/notifications"), HttpStatusCode.OK)).GetProperty("items").EnumerateArray();
        Assert.Contains(amakaInbox, n => n.GetProperty("type").GetString() == "GroupCancelled" && n.GetProperty("body").GetString()!.Contains("owed back to you"));

        // The refund can still be recorded and confirmed after cancellation.
        var pid = (await Payouts(c.Members[0], c.GroupId))[0].GetProperty("id").GetGuid();
        await Json(await c.Org.PostAsJsonAsync($"/api/v1/groups/{c.GroupId}/payouts/{pid}/records",
            new { amountMinor = C, paidOn = Today, method = "Cash", evidenceReference = (string?)null, note = (string?)null }), HttpStatusCode.Created);
    }

    [Fact]
    public async Task Fixed_term_terms_are_validated_on_the_server()
    {
        var org = await fx.VerifiedUser("Funke");
        var bad = await Json(await org.PostAsJsonAsync("/api/v1/schemes/fixed-term/preview", Rules("Individual", "Equal")), HttpStatusCode.BadRequest);
        Assert.True(bad.GetProperty("errors").TryGetProperty("allocation", out _));
        var preview = await Json(await org.PostAsJsonAsync("/api/v1/schemes/fixed-term/preview", Rules("Shared", "Proportional", periods: 4)), HttpStatusCode.OK);
        Assert.Equal(4, preview.GetProperty("schedule").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("schedule")[0].GetProperty("payoutDate").ValueKind);
        Assert.Equal(4 * C * 5, preview.GetProperty("fixedTermSummary").GetProperty("maxScheduledTotal").GetProperty("amountMinor").GetInt64());
    }
}
