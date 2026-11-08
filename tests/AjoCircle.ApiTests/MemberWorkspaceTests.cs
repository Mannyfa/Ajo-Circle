using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AjoCircle.ApiTests;

/// <summary>Separate organizer and member accounts (owner decision, 7 Nov 2026) and the member's cross-group views.</summary>
[Collection("api")]
public sealed class MemberWorkspaceTests(ApiFixture fx)
{
    private static DateOnly StartDate => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1)).AddDays(7);

    private static object Rules(int members) => new
    {
        contributionAmountMinor = 25_000_00L, currency = "NGN", frequency = "Monthly", memberCount = members,
        startDate = StartDate, payoutOffsetDays = 0, payoutOrder = "JoinOrder",
    };

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Expected {expected}, got {(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    [Fact]
    public async Task Registration_requires_a_role_and_the_role_cannot_be_anything_else()
    {
        var c = fx.Client();
        var missing = await Json(await c.PostAsJsonAsync("/api/v1/auth/register",
            new { email = $"r{Guid.NewGuid():N}@example.test", displayName = "No Role", password = "correct-horse-battery", acceptTerms = true }), HttpStatusCode.BadRequest);
        Assert.True(missing.GetProperty("errors").TryGetProperty("role", out _));

        // Unknown values (e.g. trying to self-assign staff access) are rejected outright.
        var bogus = await c.PostAsJsonAsync("/api/v1/auth/register",
            new { email = $"r{Guid.NewGuid():N}@example.test", displayName = "Sneaky", password = "correct-horse-battery", acceptTerms = true, role = "Operations" });
        Assert.Equal(HttpStatusCode.BadRequest, bogus.StatusCode);

        var org = fx.Client();
        var me = await Json(await org.PostAsJsonAsync("/api/v1/auth/register",
            new { email = $"r{Guid.NewGuid():N}@example.test", displayName = "Org", password = "correct-horse-battery", acceptTerms = true, role = "Organizer" }), HttpStatusCode.Created);
        Assert.Equal("Organizer", me.GetProperty("role").GetString());
        Assert.False(me.GetProperty("isOperations").GetBoolean());
    }

    [Fact]
    public async Task Organizers_can_join_other_groups_and_members_see_only_their_own_records_across_groups()
    {
        var orgA = await fx.VerifiedUser("OrgA");
        var orgB = await fx.VerifiedUser("OrgB");
        var member = await fx.VerifiedUser("Bisi", identityLevel: "Member");
        var other = await fx.VerifiedUser("Tayo", identityLevel: "Member");

        async Task<Guid> Group(HttpClient organizer, string name, params HttpClient[] joiners)
        {
            var id = (await Json(await organizer.PostAsJsonAsync("/api/v1/groups", new { name, rules = Rules(joiners.Length) }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
            var v = (await Json(await organizer.PostAsync($"/api/v1/groups/{id}/publish", null), HttpStatusCode.OK)).GetProperty("currentSchemeVersionId").GetGuid();
            var token = (await Json(await organizer.PostAsJsonAsync($"/api/v1/groups/{id}/invitations", new { validityDays = 7 }), HttpStatusCode.Created)).GetProperty("token").GetString();
            foreach (var j in joiners) await Json(await j.PostAsJsonAsync($"/api/v1/invitations/{token}/join", new { acceptedSchemeVersionId = v }), HttpStatusCode.Created);
            await Json(await organizer.PostAsync($"/api/v1/groups/{id}/start", null), HttpStatusCode.OK);
            return id;
        }

        // Organizer B joins organizer A's group as an ordinary member (owner decision).
        var g1 = await Group(orgA, "Group One", member, orgB);
        var g2 = await Group(orgB, "Group Two", member, other);

        var mine = await Json(await member.GetAsync("/api/v1/me/contributions"), HttpStatusCode.OK);
        Assert.Equal(4, mine.GetProperty("total").GetInt32()); // 2 rounds in each of 2 groups
        Assert.Equal(new[] { g1, g2 }.OrderBy(x => x), mine.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("groupId").GetGuid()).Distinct().OrderBy(x => x));

        var schedule = await Json(await member.GetAsync("/api/v1/me/schedule"), HttpStatusCode.OK);
        Assert.Equal(4, schedule.GetProperty("contributions").GetArrayLength());
        Assert.Equal(2, schedule.GetProperty("payouts").GetArrayLength()); // one turn in each group
        Assert.All(schedule.GetProperty("payouts").EnumerateArray(), p => Assert.Equal("Scheduled", p.GetProperty("status").GetString()));

        // Tayo is only in group two and sees nothing from group one.
        var theirs = await Json(await other.GetAsync("/api/v1/me/contributions"), HttpStatusCode.OK);
        Assert.All(theirs.GetProperty("items").EnumerateArray(), i => Assert.Equal(g2, i.GetProperty("groupId").GetGuid()));

        // A member account still can't create a group, even fully verified as a member.
        Assert.Equal("organizer_role_required", (await Json(await member.PostAsJsonAsync("/api/v1/groups", new { name = "Mine", rules = Rules(2) }), (HttpStatusCode)422)).GetProperty("code").GetString());
    }
}
