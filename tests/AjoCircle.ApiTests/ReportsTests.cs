using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace AjoCircle.ApiTests;

/// <summary>Slice 6: reports and CSV exports (brief §14.2) — only data the requester may see.</summary>
[Collection("api")]
public sealed class ReportsTests(ApiFixture fx)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Expected {expected}, got {(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Organizer + Ada (paid, recorded) + a member whose display name looks like a spreadsheet formula.</summary>
    private async Task<(Guid G, HttpClient Org, HttpClient Ada, HttpClient Sly)> Group()
    {
        var org = await fx.VerifiedUser("Ngozi");
        var ada = await fx.VerifiedUser("Ada", identityLevel: "Member");
        var sly = await fx.VerifiedUser("Sly", identityLevel: "Member");
        await sly.PatchAsJsonAsync("/api/v1/me", new { displayName = "=HYPERLINK(evil)" });
        var rules = new { contributionAmountMinor = 10_000_00L, currency = "NGN", frequency = "Weekly", memberCount = 2, startDate = Today, payoutOffsetDays = 0, payoutOrder = "JoinOrder" };
        var g = (await Json(await org.PostAsJsonAsync("/api/v1/groups", new { name = "Report Ajo", rules }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var v = (await Json(await org.PostAsync($"/api/v1/groups/{g}/publish", null), HttpStatusCode.OK)).GetProperty("currentSchemeVersionId").GetGuid();
        var token = (await Json(await org.PostAsJsonAsync($"/api/v1/groups/{g}/invitations", new { validityDays = 7 }), HttpStatusCode.Created)).GetProperty("token").GetString();
        foreach (var m in new[] { ada, sly }) await Json(await m.PostAsJsonAsync($"/api/v1/invitations/{token}/join", new { acceptedSchemeVersionId = v }), HttpStatusCode.Created);
        await Json(await org.PostAsync($"/api/v1/groups/{g}/start", null), HttpStatusCode.OK);
        var adaRound1 = (await Json(await ada.GetAsync($"/api/v1/groups/{g}/contributions"), HttpStatusCode.OK)).GetProperty("items")[0].GetProperty("id").GetGuid();
        await Json(await org.PostAsJsonAsync($"/api/v1/groups/{g}/contributions/{adaRound1}/records",
            new { amountMinor = 10_000_00L, paidOn = Today, method = "BankTransfer", evidenceReference = "TRF-PRIVATE-77", note = (string?)null }), HttpStatusCode.Created);
        var payout = (await Json(await org.GetAsync($"/api/v1/groups/{g}/rounds"), HttpStatusCode.OK))[0].GetProperty("payoutId").GetGuid();
        await Json(await org.PostAsJsonAsync($"/api/v1/groups/{g}/payouts/{payout}/records",
            new { amountMinor = 20_000_00L, paidOn = Today, method = "Cash", evidenceReference = (string?)null, note = (string?)null }), HttpStatusCode.Created);
        return (g, org, ada, sly);
    }

    private static async Task<string[][]> Rows(HttpClient c, Guid g, string kind) =>
        (await Json(await c.GetAsync($"/api/v1/groups/{g}/reports/{kind}"), HttpStatusCode.OK)).GetProperty("rows").EnumerateArray()
            .Select(r => r.EnumerateArray().Select(x => x.GetString()!).ToArray()).ToArray();

    [Fact]
    public async Task Organizer_sees_the_whole_group_and_members_only_what_they_may_see()
    {
        var (g, org, ada, sly) = await Group();

        var orgSummary = await Json(await org.GetAsync($"/api/v1/groups/{g}/reports"), HttpStatusCode.OK);
        Assert.False(orgSummary.GetProperty("forYou").GetBoolean());
        Assert.Equal(10_000_00, orgSummary.GetProperty("recorded").GetProperty("amountMinor").GetInt64());
        Assert.Contains("Timeline", orgSummary.GetProperty("available").EnumerateArray().Select(x => x.GetString()));
        var adaSummary = await Json(await ada.GetAsync($"/api/v1/groups/{g}/reports"), HttpStatusCode.OK);
        Assert.True(adaSummary.GetProperty("forYou").GetBoolean());
        Assert.DoesNotContain("Timeline", adaSummary.GetProperty("available").EnumerateArray().Select(x => x.GetString()));

        // Contributions: 4 rows for the organizer, 2 (own) for a member.
        Assert.Equal(4, (await Rows(org, g, "contributions")).Length);
        var mine = await Rows(ada, g, "contributions");
        Assert.Equal(2, mine.Length);
        Assert.All(mine, r => Assert.Equal("Ada", r[1]));

        // Roster: the organizer sees emails; members don't get an email column at all.
        var orgRoster = await Json(await org.GetAsync($"/api/v1/groups/{g}/reports/roster"), HttpStatusCode.OK);
        Assert.Contains("Email", orgRoster.GetProperty("columns").EnumerateArray().Select(c => c.GetString()));
        var memberRoster = await Json(await sly.GetAsync($"/api/v1/groups/{g}/reports/roster"), HttpStatusCode.OK);
        Assert.DoesNotContain("Email", memberRoster.GetProperty("columns").EnumerateArray().Select(c => c.GetString()));
        Assert.DoesNotContain(memberRoster.GetProperty("rows").EnumerateArray(), r => r.ToString().Contains("@example.test"));

        // Payouts: Sly sees the shared schedule but not the amount recorded as paid to Ada.
        var slyPayouts = await Rows(sly, g, "payouts");
        Assert.Equal(2, slyPayouts.Length);
        Assert.All(slyPayouts.Where(r => r[2] == "Ada"), r => Assert.Equal("", r[6]));
        Assert.Contains(await Rows(org, g, "payouts"), r => r[2] == "Ada" && r[6] == "20000.00");

        // Timeline is organizer-only; strangers get nothing at all.
        await Json(await ada.GetAsync($"/api/v1/groups/{g}/reports/timeline"), HttpStatusCode.Forbidden);
        Assert.Contains(await Rows(org, g, "timeline"), r => r[1] == "group.started");
        var stranger = await fx.VerifiedUser("Stranger");
        await Json(await stranger.GetAsync($"/api/v1/groups/{g}/reports/contributions"), HttpStatusCode.NotFound);
        await Json(await org.GetAsync($"/api/v1/groups/{g}/reports/everything"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Csv_export_is_spreadsheet_safe_omits_private_fields_and_is_audited()
    {
        var (g, org, _, _) = await Group();
        var res = await org.GetAsync($"/api/v1/groups/{g}/reports/contributions/csv");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/csv", res.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("report-ajo-contributions-", res.Content.Headers.ContentDisposition?.FileNameStar ?? res.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]); // Excel reads UTF-8 correctly
        var csv = Encoding.UTF8.GetString(bytes[3..]);
        Assert.Contains("Round,Member,Due date,Amount owed (NGN),Status", csv);
        Assert.Contains("10000.00", csv);
        Assert.DoesNotContain("TRF-PRIVATE-77", csv);            // evidence references stay in the app
        Assert.Contains("'=HYPERLINK(evil)", csv);               // formula injection neutralised
        Assert.DoesNotContain(",=HYPERLINK", csv);

        var log = await Json(await org.GetAsync($"/api/v1/groups/{g}/audit-logs"), HttpStatusCode.OK);
        Assert.Contains(log.GetProperty("items").EnumerateArray(), a => a.GetProperty("action").GetString() == "report.exported");
    }

    [Fact]
    public async Task Outstanding_lists_only_unrecorded_contributions_past_their_due_date()
    {
        var (g, org, _, _) = await Group();
        Assert.Empty(await Rows(org, g, "outstanding")); // round 1 is due today, not overdue yet
        // Move Sly's round-1 due date into the past (synthetic test data) to make it overdue.
        await fx.Sql("""
            UPDATE contribution_obligations SET due_date = due_date - 3
            WHERE group_id = @g AND status = 'Due' AND round_id = (SELECT id FROM scheme_rounds WHERE group_id = @g AND round_number = 1)
            """, ("g", g));
        var overdue = await Rows(org, g, "outstanding");
        Assert.Single(overdue);
        Assert.Equal("3", overdue[0][3]);
    }
}
