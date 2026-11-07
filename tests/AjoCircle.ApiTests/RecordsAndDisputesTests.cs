using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace AjoCircle.ApiTests;

/// <summary>Slice 2: organizer-reported contributions and payouts, member confirmation, corrections and disputes.</summary>
[Collection("api")]
public sealed class RecordsAndDisputesTests(ApiFixture fx)
{
    private const long Contribution = 50_000_00L;
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1)); // Lagos

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Expected {expected}, got {(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private sealed record Setup(Guid GroupId, HttpClient Organizer, HttpClient Ada, HttpClient Bayo, JsonElement[] Rounds, JsonElement[] Obligations)
    {
        /// <summary>The round-1 obligation of the given member.</summary>
        public Guid ObligationOf(string name, int round = 1) =>
            Obligations.Single(o => o.GetProperty("memberName").GetString() == name && o.GetProperty("roundNumber").GetInt32() == round).GetProperty("id").GetGuid();
    }

    /// <summary>A started 2-member group run by an organizer who is not a member. Ada joins first so she is paid in round 1.</summary>
    private async Task<Setup> ActiveGroup()
    {
        var organizer = await fx.VerifiedUser("Ngozi");
        var ada = await fx.VerifiedUser("Ada");
        var bayo = await fx.VerifiedUser("Bayo");
        var rules = new { contributionAmountMinor = Contribution, currency = "NGN", frequency = "Weekly", memberCount = 2, startDate = Today, payoutOffsetDays = 0, payoutOrder = "JoinOrder" };
        var g = await Json(await organizer.PostAsJsonAsync("/api/v1/groups", new { name = "Surulere Weekly", rules }), HttpStatusCode.Created);
        var id = g.GetProperty("id").GetGuid();
        var published = await Json(await organizer.PostAsync($"/api/v1/groups/{id}/publish", null), HttpStatusCode.OK);
        var version = published.GetProperty("currentSchemeVersionId").GetGuid();
        var token = (await Json(await organizer.PostAsJsonAsync($"/api/v1/groups/{id}/invitations", new { validityDays = 7 }), HttpStatusCode.Created)).GetProperty("token").GetString();
        foreach (var m in new[] { ada, bayo })
            await Json(await m.PostAsJsonAsync($"/api/v1/invitations/{token}/join", new { acceptedSchemeVersionId = version }), HttpStatusCode.Created);
        await Json(await organizer.PostAsync($"/api/v1/groups/{id}/start", null), HttpStatusCode.OK);

        var rounds = (await Json(await organizer.GetAsync($"/api/v1/groups/{id}/rounds"), HttpStatusCode.OK)).EnumerateArray().ToArray();
        var obligations = (await Json(await organizer.GetAsync($"/api/v1/groups/{id}/contributions"), HttpStatusCode.OK)).GetProperty("items").EnumerateArray().ToArray();
        return new Setup(id, organizer, ada, bayo, rounds, obligations);
    }

    private static object Payment(long amount = Contribution, DateOnly? paidOn = null, string? evidence = "TRF-2026-0001") =>
        new { amountMinor = amount, paidOn = paidOn ?? Today, method = "BankTransfer", evidenceReference = evidence, note = (string?)null };

    private static Guid CurrentRecordId(JsonElement detail) =>
        detail.GetProperty("records").EnumerateArray().First(r => r.GetProperty("status").GetString() != "Reversed").GetProperty("id").GetGuid();

    [Fact]
    public async Task Organizer_records_member_confirms_and_privacy_holds()
    {
        var s = await ActiveGroup();
        var adaObligation = s.ObligationOf("Ada");
        var url = $"/api/v1/groups/{s.GroupId}/contributions/{adaObligation}";

        // Members can't record payments, even their own.
        await Json(await s.Ada.PostAsJsonAsync($"{url}/records", Payment()), HttpStatusCode.Forbidden);

        var detail = await Json(await s.Organizer.PostAsJsonAsync($"{url}/records", Payment()), HttpStatusCode.Created);
        Assert.Equal("RecordedByOrganizer", detail.GetProperty("status").GetString());
        var recordId = CurrentRecordId(detail);

        // A second record for the same obligation is refused; the first must be corrected instead.
        var dup = await Json(await s.Organizer.PostAsJsonAsync($"{url}/records", Payment()), HttpStatusCode.Conflict);
        Assert.Equal("already_recorded", dup.GetProperty("code").GetString());

        // Wrong amount is a validation error, not a silent partial payment.
        var bayoUrl = $"/api/v1/groups/{s.GroupId}/contributions/{s.ObligationOf("Bayo")}";
        var bad = await Json(await s.Organizer.PostAsJsonAsync($"{bayoUrl}/records", Payment(amount: Contribution - 1_000_00)), HttpStatusCode.BadRequest);
        Assert.True(bad.GetProperty("errors").TryGetProperty("amountMinor", out _));

        // Bayo can't see or act on Ada's obligation.
        await Json(await s.Bayo.GetAsync(url), HttpStatusCode.NotFound);
        await Json(await s.Bayo.PostAsync($"/api/v1/contribution-records/{recordId}/confirm", null), HttpStatusCode.Forbidden);
        await Json(await s.Organizer.PostAsync($"/api/v1/contribution-records/{recordId}/confirm", null), HttpStatusCode.Forbidden);
        // Someone outside the group gets 404, not a hint that the record exists.
        var stranger = await fx.VerifiedUser("Stranger");
        await Json(await stranger.PostAsync($"/api/v1/contribution-records/{recordId}/confirm", null), HttpStatusCode.NotFound);

        var adaView = await Json(await s.Ada.GetAsync(url), HttpStatusCode.OK);
        Assert.Contains("confirm", adaView.GetProperty("records")[0].GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));
        var confirmed = await Json(await s.Ada.PostAsync($"/api/v1/contribution-records/{recordId}/confirm", null), HttpStatusCode.OK);
        Assert.Equal("ConfirmedByMember", confirmed.GetProperty("status").GetString());
        await Json(await s.Ada.PostAsync($"/api/v1/contribution-records/{recordId}/confirm", null), HttpStatusCode.OK); // idempotent

        // Round totals: organizer only.
        var round1 = (await Json(await s.Organizer.GetAsync($"/api/v1/groups/{s.GroupId}/rounds"), HttpStatusCode.OK))[0].GetProperty("progress");
        Assert.Equal(2, round1.GetProperty("expected").GetInt32());
        Assert.Equal(1, round1.GetProperty("recorded").GetInt32());
        Assert.Equal(1, round1.GetProperty("confirmed").GetInt32());
        Assert.Equal(Contribution, round1.GetProperty("recordedTotal").GetProperty("amountMinor").GetInt64());
        Assert.Equal(JsonValueKind.Null, (await Json(await s.Ada.GetAsync($"/api/v1/groups/{s.GroupId}/rounds"), HttpStatusCode.OK))[0].GetProperty("progress").ValueKind);
    }

    [Fact]
    public async Task Dispute_then_correction_keeps_the_original_and_resolves_for_the_member()
    {
        var s = await ActiveGroup();
        var obligation = s.ObligationOf("Ada");
        var url = $"/api/v1/groups/{s.GroupId}/contributions/{obligation}";
        var recordId = CurrentRecordId(await Json(await s.Organizer.PostAsJsonAsync($"{url}/records", Payment(paidOn: Today)), HttpStatusCode.Created));

        // Bayo can't dispute Ada's record.
        await Json(await s.Bayo.PostAsJsonAsync($"/api/v1/contribution-records/{recordId}/dispute", new { category = "WrongDate", description = "Not my payment at all." }), HttpStatusCode.Forbidden);

        var dispute = await Json(await s.Ada.PostAsJsonAsync($"/api/v1/contribution-records/{recordId}/dispute",
            new { category = "WrongDate", description = "I paid the day before, please fix the date." }), HttpStatusCode.Created);
        var disputeId = dispute.GetProperty("summary").GetProperty("id").GetGuid();
        Assert.StartsWith("DSP-", dispute.GetProperty("summary").GetProperty("reference").GetString());
        Assert.Equal("Disputed", (await Json(await s.Ada.GetAsync(url), HttpStatusCode.OK)).GetProperty("status").GetString());

        // A second open dispute on the same record is refused.
        await Json(await s.Ada.PostAsJsonAsync($"/api/v1/contribution-records/{recordId}/dispute", new { category = "Other", description = "Another complaint here." }), HttpStatusCode.Conflict);

        // Visible to the organizer and Ada only.
        await Json(await s.Bayo.GetAsync($"/api/v1/disputes/{disputeId}"), HttpStatusCode.NotFound);
        Assert.Empty((await Json(await s.Bayo.GetAsync("/api/v1/disputes"), HttpStatusCode.OK)).EnumerateArray());
        Assert.Single((await Json(await s.Organizer.GetAsync("/api/v1/disputes?status=Open"), HttpStatusCode.OK)).EnumerateArray());

        // The organizer can reply but cannot close the dispute by overruling Ada.
        await Json(await s.Organizer.PostAsJsonAsync($"/api/v1/disputes/{disputeId}/responses", new { body = "Checking my notebook now." }), HttpStatusCode.OK);
        await Json(await s.Organizer.PostAsJsonAsync($"/api/v1/disputes/{disputeId}/withdraw", new { note = "fine" }), HttpStatusCode.Forbidden);
        await Json(await s.Ada.PostAsJsonAsync($"/api/v1/disputes/{disputeId}/responses", new { body = "Thanks — it was Monday." }), HttpStatusCode.OK);

        // Correct the date: the original is reversed (values kept), a linked replacement is created, the dispute resolves.
        var corrected = await Json(await s.Organizer.PostAsJsonAsync($"/api/v1/contribution-records/{recordId}/amendments",
            new { reason = "Ada paid a day earlier than I recorded", replacement = Payment(paidOn: Today.AddDays(-1)) }), HttpStatusCode.OK);
        Assert.Equal("RecordedByOrganizer", corrected.GetProperty("status").GetString());
        var records = corrected.GetProperty("records").EnumerateArray().ToList();
        Assert.Equal(2, records.Count);
        var original = records.Single(r => r.GetProperty("id").GetGuid() == recordId);
        Assert.Equal("Reversed", original.GetProperty("status").GetString());
        Assert.Equal(Today.ToString("yyyy-MM-dd"), original.GetProperty("paidOn").GetString());
        Assert.Equal("Ada paid a day earlier than I recorded", original.GetProperty("reversalReason").GetString());
        var replacement = records.Single(r => r.GetProperty("id").GetGuid() != recordId);
        Assert.Equal(recordId, replacement.GetProperty("replacesRecordId").GetGuid());

        var resolved = await Json(await s.Ada.GetAsync($"/api/v1/disputes/{disputeId}"), HttpStatusCode.OK);
        Assert.Equal("Resolved", resolved.GetProperty("summary").GetProperty("status").GetString());
        Assert.Equal("RecordCorrected", resolved.GetProperty("summary").GetProperty("resolution").GetString());
        Assert.Equal(replacement.GetProperty("id").GetGuid(), resolved.GetProperty("correctiveRecordId").GetGuid());
        Assert.Equal(2, resolved.GetProperty("messages").GetArrayLength());
        await Json(await s.Ada.PostAsJsonAsync($"/api/v1/disputes/{disputeId}/responses", new { body = "late" }), HttpStatusCode.Conflict);

        // A reversed record can't be corrected again.
        await Json(await s.Organizer.PostAsJsonAsync($"/api/v1/contribution-records/{recordId}/amendments", new { reason = "Second try at this" }), HttpStatusCode.Conflict);

        var audit = await Json(await s.Organizer.GetAsync($"/api/v1/groups/{s.GroupId}/audit-logs"), HttpStatusCode.OK);
        var actions = audit.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("action").GetString()).ToList();
        Assert.Contains("contribution.recorded", actions);
        Assert.Contains("dispute.opened", actions);
        Assert.Contains("contribution.corrected", actions);
        Assert.Contains("dispute.resolved", actions);
        // The dispute text and evidence reference stay out of the group log.
        Assert.DoesNotContain(audit.GetProperty("items").EnumerateArray(), a => (a.GetProperty("metadata").GetString() ?? "").Contains("Monday")
            || (a.GetProperty("metadata").GetString() ?? "").Contains("TRF-2026"));
    }

    [Fact]
    public async Task Member_can_withdraw_a_dispute_which_confirms_the_record()
    {
        var s = await ActiveGroup();
        var url = $"/api/v1/groups/{s.GroupId}/contributions/{s.ObligationOf("Bayo")}";
        var recordId = CurrentRecordId(await Json(await s.Organizer.PostAsJsonAsync($"{url}/records", Payment()), HttpStatusCode.Created));
        var d = await Json(await s.Bayo.PostAsJsonAsync($"/api/v1/contribution-records/{recordId}/dispute",
            new { category = "WrongAmount", description = "I think I paid more than this." }), HttpStatusCode.Created);
        var id = d.GetProperty("summary").GetProperty("id").GetGuid();

        var withdrawn = await Json(await s.Bayo.PostAsJsonAsync($"/api/v1/disputes/{id}/withdraw", new { note = "Checked my bank app, it's right." }), HttpStatusCode.OK);
        Assert.Equal("WithdrawnByMember", withdrawn.GetProperty("summary").GetProperty("resolution").GetString());
        Assert.Equal("ConfirmedByMember", (await Json(await s.Bayo.GetAsync(url), HttpStatusCode.OK)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Payout_outcome_is_recorded_confirmed_by_recipient_and_reversible()
    {
        var s = await ActiveGroup();
        var round1 = s.Rounds[0];
        Assert.Equal("Scheduled", round1.GetProperty("payoutStatus").GetString());
        var payoutId = round1.GetProperty("payoutId").GetGuid();
        var url = $"/api/v1/groups/{s.GroupId}/payouts/{payoutId}";

        // Recipient (Ada) and organizer can open it; Bayo can't see Ada's payout evidence.
        await Json(await s.Bayo.GetAsync(url), HttpStatusCode.NotFound);
        var pool = round1.GetProperty("scheduledPayout").GetProperty("amountMinor").GetInt64();
        await Json(await s.Organizer.PostAsJsonAsync($"{url}/records", Payment(amount: pool + 100_00)), HttpStatusCode.BadRequest);

        var detail = await Json(await s.Organizer.PostAsJsonAsync($"{url}/records", Payment(amount: pool, evidence: "POS-778")), HttpStatusCode.Created);
        Assert.Equal("RecordedByOrganizer", detail.GetProperty("status").GetString());
        var recordId = CurrentRecordId(detail);

        await Json(await s.Bayo.PostAsync($"/api/v1/payout-records/{recordId}/confirm", null), HttpStatusCode.Forbidden);
        await Json(await s.Ada.PostAsync($"/api/v1/payout-records/{recordId}/confirm", null), HttpStatusCode.NoContent);
        var adaView = await Json(await s.Ada.GetAsync(url), HttpStatusCode.OK);
        Assert.Equal("ConfirmedByRecipient", adaView.GetProperty("status").GetString());
        Assert.True(adaView.GetProperty("isYou").GetBoolean());

        // Everyone in the group sees the status on the schedule (not the evidence).
        Assert.Equal("ConfirmedByRecipient", (await Json(await s.Bayo.GetAsync($"/api/v1/groups/{s.GroupId}/rounds"), HttpStatusCode.OK))[0].GetProperty("payoutStatus").GetString());

        // Reversal without a replacement puts the payout back to Scheduled.
        await Json(await s.Organizer.PostAsJsonAsync($"/api/v1/payout-records/{recordId}/amendments", new { reason = "Recorded against the wrong round" }), HttpStatusCode.NoContent);
        Assert.Equal("Scheduled", (await Json(await s.Organizer.GetAsync(url), HttpStatusCode.OK)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Concurrent_submissions_create_only_one_record()
    {
        var s = await ActiveGroup();
        var url = $"/api/v1/groups/{s.GroupId}/contributions/{s.ObligationOf("Ada", round: 2)}/records";
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => s.Organizer.PostAsJsonAsync(url, Payment())));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task Database_rejects_edits_and_deletes_of_reported_records()
    {
        var s = await ActiveGroup();
        var url = $"/api/v1/groups/{s.GroupId}/contributions/{s.ObligationOf("Ada")}";
        var recordId = CurrentRecordId(await Json(await s.Organizer.PostAsJsonAsync($"{url}/records", Payment()), HttpStatusCode.Created));

        await using var conn = new NpgsqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        async Task Rejected(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", recordId);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
            Assert.Contains("ajo_append_only", ex.MessageText);
        }
        await Rejected("UPDATE contribution_records SET amount_minor = 1 WHERE id = @id");
        await Rejected("UPDATE contribution_records SET evidence_reference = 'edited' WHERE id = @id");
        await Rejected("DELETE FROM contribution_records WHERE id = @id");

        // Status may move, but once reversed the row is frozen.
        await Json(await s.Organizer.PostAsJsonAsync($"/api/v1/contribution-records/{recordId}/amendments", new { reason = "Entered by mistake" }), HttpStatusCode.OK);
        await Rejected("UPDATE contribution_records SET status = 'RecordedByOrganizer', reversed_at = NULL, reversed_by = NULL, reversal_reason = NULL WHERE id = @id");
        await Rejected("DELETE FROM record_amendments WHERE record_id = @id");
    }
}
