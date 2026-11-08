using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AjoCircle.Application;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AjoCircle.ApiTests;

/// <summary>Slice 5: identity verification gating, operations workspace with MFA, dispute escalation and investigation.</summary>
[Collection("api")]
public sealed class OperationsAndIdentityTests(ApiFixture fx)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 1, 2, 3, 4, 5];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 9, 8, 7];

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Expected {expected}, got {(int)r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string Phone() => $"080{Random.Shared.Next(10_000_000, 99_999_999)}";

    private async Task VerifyPhone(HttpClient c, string phone)
    {
        await Json(await c.PostAsJsonAsync("/api/v1/me/phone", new { phone }), HttpStatusCode.Accepted);
        var e164 = "+234" + phone[1..];
        await Json(await c.PostAsJsonAsync("/api/v1/me/phone/verify", new { code = fx.Sms.LatestCode(e164) }), HttpStatusCode.OK);
    }

    private static MultipartFormDataContent Form(string level, string idType, string idNumber, string? bvn, bool photo, byte[]? idFront = null)
    {
        var f = new MultipartFormDataContent
        {
            { new StringContent(level), "level" },
            { new StringContent(idType), "idType" },
            { new StringContent(idNumber), "idNumber" },
        };
        if (bvn is not null) f.Add(new StringContent(bvn), "bvn");
        var front = new ByteArrayContent(idFront ?? Jpeg); front.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        f.Add(front, "idFront", "front.jpg");
        if (photo) { var p = new ByteArrayContent(Png); p.Headers.ContentType = new MediaTypeHeaderValue("image/png"); f.Add(p, "passportPhoto", "me.png"); }
        return f;
    }

    private static object Rules => new { contributionAmountMinor = 10_000_00L, currency = "NGN", frequency = "Weekly", memberCount = 2, startDate = Today, payoutOffsetDays = 0, payoutOrder = "JoinOrder" };

    [Fact]
    public async Task Unverified_users_can_look_but_cannot_create_or_join()
    {
        var unverified = await fx.VerifiedUser("Ifeoma", identityLevel: "None");
        var me = await Json(await unverified.GetAsync("/api/v1/me"), HttpStatusCode.OK);
        Assert.Equal("None", me.GetProperty("identityLevel").GetString());
        Assert.Equal("Member", me.GetProperty("role").GetString());
        Assert.Equal("organizer_role_required", (await Json(await unverified.PostAsJsonAsync("/api/v1/groups", new { name = "Not yet", rules = Rules }), (HttpStatusCode)422)).GetProperty("code").GetString());

        // A member-level user can join but still can't organize.
        var org = await fx.VerifiedUser("Ngozi");
        var g = (await Json(await org.PostAsJsonAsync("/api/v1/groups", new { name = "Gate Test", rules = Rules }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var v = (await Json(await org.PostAsync($"/api/v1/groups/{g}/publish", null), HttpStatusCode.OK)).GetProperty("currentSchemeVersionId").GetGuid();
        var token = (await Json(await org.PostAsJsonAsync($"/api/v1/groups/{g}/invitations", new { validityDays = 7 }), HttpStatusCode.Created)).GetProperty("token").GetString();
        Assert.Equal("identity_unverified", (await Json(await unverified.PostAsJsonAsync($"/api/v1/invitations/{token}/join", new { acceptedSchemeVersionId = v }), (HttpStatusCode)422)).GetProperty("code").GetString());

        var member = await fx.VerifiedUser("Kunle", identityLevel: "Member");
        await Json(await member.PostAsJsonAsync($"/api/v1/invitations/{token}/join", new { acceptedSchemeVersionId = v }), HttpStatusCode.Created);
        Assert.Equal("organizer_role_required", (await Json(await member.PostAsJsonAsync("/api/v1/groups", new { name = "Mine", rules = Rules }), (HttpStatusCode)422)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Organizer_verification_is_reviewed_by_operations_with_encrypted_documents_and_an_audit_trail()
    {
        var applicant = await fx.VerifiedUser("Chioma", identityLevel: "None");
        var phone = Phone();

        // Phone must be verified first; a wrong code is refused.
        Assert.Equal("phone", (await Json(await applicant.PostAsync("/api/v1/me/verification", Form("Organizer", "NationalId", "12345678901", "22233344455", true)), HttpStatusCode.BadRequest))
            .GetProperty("errors").EnumerateObject().First().Name);
        await Json(await applicant.PostAsJsonAsync("/api/v1/me/phone", new { phone }), HttpStatusCode.Accepted);
        await Json(await applicant.PostAsJsonAsync("/api/v1/me/phone/verify", new { code = "000000" }), (HttpStatusCode)422);
        await Json(await applicant.PostAsJsonAsync("/api/v1/me/phone/verify", new { code = fx.Sms.LatestCode("+234" + phone[1..]) }), HttpStatusCode.OK);

        // Validation: organizers need BVN + passport photo; a file whose bytes aren't an image is rejected whatever it claims to be.
        var bad = await Json(await applicant.PostAsync("/api/v1/me/verification", Form("Organizer", "NationalId", "123", null, false)), HttpStatusCode.BadRequest);
        foreach (var field in new[] { "idNumber", "bvn", "passportPhoto" }) Assert.True(bad.GetProperty("errors").TryGetProperty(field, out _), field);
        var fake = await Json(await applicant.PostAsync("/api/v1/me/verification",
            Form("Organizer", "NationalId", "12345678901", "22233344455", true, idFront: "<html>not an image</html>"u8.ToArray())), HttpStatusCode.BadRequest);
        Assert.True(fake.GetProperty("errors").TryGetProperty("idFront", out _));

        var status = await Json(await applicant.PostAsync("/api/v1/me/verification", Form("Organizer", "NationalId", "123 4567 8901", "222-333-444-55", true)), HttpStatusCode.Created);
        Assert.Equal("Pending", status.GetProperty("latest").GetProperty("status").GetString());
        Assert.Equal("submission_pending", (await Json(await applicant.PostAsync("/api/v1/me/verification", Form("Organizer", "NationalId", "12345678901", "22233344455", true)), HttpStatusCode.Conflict))
            .GetProperty("code").GetString());

        // Identifiers are encrypted at rest; only the last 4 digits are stored in the clear.
        await using (var conn = new NpgsqlConnection(fx.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT id_number_protected, bvn_protected, bvn_last4 FROM identity_submissions ORDER BY submitted_at DESC LIMIT 1", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            Assert.DoesNotContain("12345678901", r.GetString(0));
            Assert.DoesNotContain("22233344455", r.GetString(1));
            Assert.Equal("4455", r.GetString(2));
        }

        // The workspace doesn't exist for ordinary users; staff need the second factor first.
        await Json(await applicant.GetAsync("/api/v1/operations/verifications"), HttpStatusCode.NotFound);
        var (ops, _) = await fx.Operator("Opsy");
        var queue = await Json(await ops.GetAsync("/api/v1/operations/verifications?status=Pending"), HttpStatusCode.OK);
        var item = queue.EnumerateArray().Single(x => x.GetProperty("userName").GetString() == "Chioma");
        var id = item.GetProperty("id").GetGuid();

        var detail = await Json(await ops.GetAsync($"/api/v1/operations/verifications/{id}"), HttpStatusCode.OK);
        Assert.Equal("8901", detail.GetProperty("idNumberLast4").GetString());
        Assert.Equal(2, detail.GetProperty("documents").GetArrayLength());
        var front = detail.GetProperty("documents").EnumerateArray().Single(d => d.GetProperty("kind").GetString() == "IdFront").GetProperty("id").GetGuid();
        var file = await ops.GetAsync($"/api/v1/operations/verifications/{id}/documents/{front}");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal(Jpeg, await file.Content.ReadAsByteArrayAsync()); // decrypted for viewing only
        Assert.Equal("22233344455", (await Json(await ops.PostAsync($"/api/v1/operations/verifications/{id}/reveal", null), HttpStatusCode.OK)).GetProperty("bvn").GetString());

        await Json(await ops.PostAsJsonAsync($"/api/v1/operations/verifications/{id}/approve", new { note = "ID matches photo" }), HttpStatusCode.OK);
        var promoted = await Json(await applicant.GetAsync("/api/v1/me"), HttpStatusCode.OK);
        Assert.Equal("Organizer", promoted.GetProperty("identityLevel").GetString());
        Assert.Equal("Organizer", promoted.GetProperty("role").GetString()); // an approved organizer check is the only way a member becomes an organizer
        await Json(await applicant.PostAsJsonAsync("/api/v1/groups", new { name = "Now allowed", rules = Rules }), HttpStatusCode.Created);

        // Every look at sensitive data is in the audit log.
        var audit = await Json(await ops.GetAsync($"/api/v1/operations/audit-logs?action=operations.identity"), HttpStatusCode.OK);
        var actions = audit.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("action").GetString()).ToList();
        Assert.Contains("operations.identity_document_viewed", actions);
        Assert.Contains("operations.identity_revealed", actions);
        Assert.Contains("operations.identity_approved", actions);

        // Retention: after the decision, documents and full identifiers are purged; the outcome stays.
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<IdentityRetention>().PurgeAsync(default) >= 1);
        var after = await Json(await ops.GetAsync($"/api/v1/operations/verifications/{id}"), HttpStatusCode.OK);
        Assert.True(after.GetProperty("sensitiveDataPurged").GetBoolean());
        Assert.False(after.GetProperty("documents")[0].GetProperty("available").GetBoolean());
        await Json(await ops.GetAsync($"/api/v1/operations/verifications/{id}/documents/{front}"), HttpStatusCode.Conflict);

        // Identity documents can't be deleted, even directly in the database.
        await using var c2 = new NpgsqlConnection(fx.ConnectionString);
        await c2.OpenAsync();
        await using var del = new NpgsqlCommand("DELETE FROM identity_documents WHERE id = @id", c2);
        del.Parameters.AddWithValue("id", front);
        Assert.Contains("ajo_append_only", (await Assert.ThrowsAsync<PostgresException>(() => del.ExecuteNonQueryAsync())).MessageText);
    }

    [Fact]
    public async Task Members_only_need_phone_and_one_id_and_operations_cannot_review_themselves()
    {
        var m = await fx.VerifiedUser("Tobi", identityLevel: "None");
        await VerifyPhone(m, Phone());
        var bvn = await Json(await m.PostAsync("/api/v1/me/verification", Form("Member", "DriversLicence", "ABC12345XY", "22233344455", false)), HttpStatusCode.BadRequest);
        Assert.True(bvn.GetProperty("errors").TryGetProperty("bvn", out _)); // not collected for members
        await Json(await m.PostAsync("/api/v1/me/verification", Form("Member", "DriversLicence", "ABC12345XY", null, false)), HttpStatusCode.Created);

        var (ops, _) = await fx.Operator("Opsy");
        var id = (await Json(await ops.GetAsync("/api/v1/operations/verifications?status=Pending"), HttpStatusCode.OK)).EnumerateArray()
            .Single(x => x.GetProperty("userName").GetString() == "Tobi").GetProperty("id").GetGuid();
        await Json(await ops.PostAsJsonAsync($"/api/v1/operations/verifications/{id}/reject", new { note = "no" }), HttpStatusCode.BadRequest); // reason needed
        await Json(await ops.PostAsJsonAsync($"/api/v1/operations/verifications/{id}/reject", new { note = "The photo is too blurry to read" }), HttpStatusCode.OK);
        var mine = await Json(await m.GetAsync("/api/v1/me/verification"), HttpStatusCode.OK);
        Assert.Equal("Rejected", mine.GetProperty("latest").GetProperty("status").GetString());
        Assert.Equal("The photo is too blurry to read", mine.GetProperty("latest").GetProperty("rejectionReason").GetString());

        // A staff member who submits their own check can't approve it.
        await VerifyPhone(ops, Phone());
        await Json(await ops.PostAsync("/api/v1/me/verification", Form("Member", "InternationalPassport", "A12345678", null, false)), HttpStatusCode.Created);
        var own = (await Json(await ops.GetAsync("/api/v1/operations/verifications?status=Pending"), HttpStatusCode.OK)).EnumerateArray()
            .Single(x => x.GetProperty("userName").GetString() == "Opsy").GetProperty("id").GetGuid();
        Assert.Equal("own_submission", (await Json(await ops.PostAsJsonAsync($"/api/v1/operations/verifications/{own}/approve", new { note = (string?)null }), HttpStatusCode.Conflict))
            .GetProperty("code").GetString());
    }

    [Fact]
    public async Task Operations_needs_a_fresh_second_factor_and_codes_cant_be_replayed()
    {
        var (ops, secret) = await fx.Operator("Opsy");
        // A new session for the same person (sign in again) has no second factor yet.
        var email = (await Json(await ops.GetAsync("/api/v1/me"), HttpStatusCode.OK)).GetProperty("email").GetString();
        var fresh = fx.Client();
        await Json(await fresh.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "correct-horse-battery" }), HttpStatusCode.OK);
        Assert.False((await Json(await fresh.GetAsync("/api/v1/me"), HttpStatusCode.OK)).GetProperty("operationsSessionActive").GetBoolean());
        Assert.Equal("mfa_required", (await Json(await fresh.GetAsync("/api/v1/operations/disputes"), (HttpStatusCode)422)).GetProperty("code").GetString());

        await Json(await fresh.PostAsJsonAsync("/api/v1/operations/mfa/verify", new { code = "123456" }), (HttpStatusCode)422);
        // The code used to enable MFA was just accepted; use the next window's code, then show it can't be reused.
        var next = ApiFixture.TotpNow(secret, 1);
        await Json(await fresh.PostAsJsonAsync("/api/v1/operations/mfa/verify", new { code = next }), HttpStatusCode.NoContent);
        Assert.True((await Json(await fresh.GetAsync("/api/v1/me"), HttpStatusCode.OK)).GetProperty("operationsSessionActive").GetBoolean());
        await Json(await fresh.GetAsync("/api/v1/operations/disputes"), HttpStatusCode.OK);
        var other = fx.Client();
        await Json(await other.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "correct-horse-battery" }), HttpStatusCode.OK);
        await Json(await other.PostAsJsonAsync("/api/v1/operations/mfa/verify", new { code = next }), (HttpStatusCode)422);
        // Setting up again isn't allowed once enabled.
        await Json(await fresh.PostAsync("/api/v1/operations/mfa/setup", null), HttpStatusCode.Conflict);
    }

    private async Task<(HttpClient Org, HttpClient Ada, Guid GroupId, Guid RecordId, Guid DisputeId)> DisputedRecord(string name)
    {
        var org = await fx.VerifiedUser("Ngozi");
        var ada = await fx.VerifiedUser("Ada", identityLevel: "Member");
        var bayo = await fx.VerifiedUser("Bayo", identityLevel: "Member");
        var g = (await Json(await org.PostAsJsonAsync("/api/v1/groups", new { name, rules = Rules }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var v = (await Json(await org.PostAsync($"/api/v1/groups/{g}/publish", null), HttpStatusCode.OK)).GetProperty("currentSchemeVersionId").GetGuid();
        var token = (await Json(await org.PostAsJsonAsync($"/api/v1/groups/{g}/invitations", new { validityDays = 7 }), HttpStatusCode.Created)).GetProperty("token").GetString();
        foreach (var m in new[] { ada, bayo }) await Json(await m.PostAsJsonAsync($"/api/v1/invitations/{token}/join", new { acceptedSchemeVersionId = v }), HttpStatusCode.Created);
        await Json(await org.PostAsync($"/api/v1/groups/{g}/start", null), HttpStatusCode.OK);
        var o = (await Json(await ada.GetAsync($"/api/v1/groups/{g}/contributions"), HttpStatusCode.OK)).GetProperty("items")[0].GetProperty("id").GetGuid();
        var rec = (await Json(await org.PostAsJsonAsync($"/api/v1/groups/{g}/contributions/{o}/records",
            new { amountMinor = 10_000_00L, paidOn = Today, method = "Cash", evidenceReference = (string?)null, note = (string?)null }), HttpStatusCode.Created)).GetProperty("records")[0].GetProperty("id").GetGuid();
        var d = (await Json(await ada.PostAsJsonAsync($"/api/v1/contribution-records/{rec}/dispute",
            new { category = "DidNotHappen", description = "I never handed over this money." }), HttpStatusCode.Created)).GetProperty("summary").GetProperty("id").GetGuid();
        return (org, ada, g, rec, d);
    }

    [Fact]
    public async Task Escalated_dispute_needing_correction_is_resolved_by_the_organizers_correction()
    {
        var (org, ada, g, rec, d) = await DisputedRecord("Escalation Ajo");
        var (ops, _) = await fx.Operator("Opsy");
        await Json(await ops.GetAsync($"/api/v1/operations/disputes/{d}"), HttpStatusCode.NotFound); // not escalated yet

        var esc = await Json(await ada.PostAsync($"/api/v1/disputes/{d}/escalate", null), HttpStatusCode.OK);
        Assert.Equal("Escalated", esc.GetProperty("summary").GetProperty("status").GetString());
        await Json(await org.PostAsync($"/api/v1/disputes/{d}/escalate", null), HttpStatusCode.Conflict);
        Assert.Contains((await Json(await org.GetAsync("/api/v1/me/notifications"), HttpStatusCode.OK)).GetProperty("items").EnumerateArray(),
            n => n.GetProperty("type").GetString() == "DisputeEscalated");

        Assert.Contains(await Json(await ops.GetAsync("/api/v1/operations/disputes?status=Escalated"), HttpStatusCode.OK)
            is var q ? q.EnumerateArray() : default, x => x.GetProperty("id").GetGuid() == d);
        var detail = await Json(await ops.GetAsync($"/api/v1/operations/disputes/{d}"), HttpStatusCode.OK);
        Assert.Contains("decide", detail.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));
        await Json(await ops.PostAsJsonAsync($"/api/v1/operations/disputes/{d}/messages", new { body = "Organizer, please share your notebook entry." }), HttpStatusCode.OK);
        var thread = await Json(await ada.GetAsync($"/api/v1/disputes/{d}"), HttpStatusCode.OK);
        Assert.Contains(thread.GetProperty("messages").EnumerateArray(), m => m.GetProperty("isOperations").GetBoolean());

        await Json(await ops.PostAsJsonAsync($"/api/v1/operations/disputes/{d}/decision", new { decision = "CorrectionRequired", note = "short" }), HttpStatusCode.BadRequest);
        var decided = await Json(await ops.PostAsJsonAsync($"/api/v1/operations/disputes/{d}/decision",
            new { decision = "CorrectionRequired", note = "No evidence the payment happened; the organizer should reverse it." }), HttpStatusCode.OK);
        Assert.Equal("Escalated", decided.GetProperty("summary").GetProperty("status").GetString());
        Assert.Equal("CorrectionRequired", decided.GetProperty("escalation").GetProperty("decision").GetString());
        await Json(await ops.PostAsJsonAsync($"/api/v1/operations/disputes/{d}/decision", new { decision = "RecordUpheld", note = "Changing my mind here." }), HttpStatusCode.Conflict);

        // Investigators decide only; the organizer makes the correction, which resolves the dispute.
        await Json(await org.PostAsJsonAsync($"/api/v1/contribution-records/{rec}/amendments", new { reason = "Operations found no evidence of payment" }), HttpStatusCode.OK);
        var final = await Json(await ada.GetAsync($"/api/v1/disputes/{d}"), HttpStatusCode.OK);
        Assert.Equal("Resolved", final.GetProperty("summary").GetProperty("status").GetString());
        Assert.Equal("RecordReversed", final.GetProperty("summary").GetProperty("resolution").GetString());

        var log = await Json(await ops.GetAsync($"/api/v1/operations/audit-logs?groupId={g}&action=dispute"), HttpStatusCode.OK);
        Assert.Contains(log.GetProperty("items").EnumerateArray(), a => a.GetProperty("action").GetString() == "dispute.escalated");
    }

    [Fact]
    public async Task Upheld_record_closes_the_dispute_and_cannot_be_disputed_again()
    {
        var (_, ada, g, rec, d) = await DisputedRecord("Upheld Ajo");
        await Json(await ada.PostAsync($"/api/v1/disputes/{d}/escalate", null), HttpStatusCode.OK);
        var (ops, _) = await fx.Operator("Opsy");
        var r = await Json(await ops.PostAsJsonAsync($"/api/v1/operations/disputes/{d}/decision",
            new { decision = "RecordUpheld", note = "Bank statement shows the transfer on that date." }), HttpStatusCode.OK);
        Assert.Equal("UpheldByOperations", r.GetProperty("summary").GetProperty("resolution").GetString());

        var o = (await Json(await ada.GetAsync($"/api/v1/groups/{g}/contributions"), HttpStatusCode.OK)).GetProperty("items")[0];
        Assert.Equal("RecordedByOrganizer", o.GetProperty("status").GetString());
        Assert.Equal("record_upheld", (await Json(await ada.PostAsJsonAsync($"/api/v1/contribution-records/{rec}/dispute",
            new { category = "Other", description = "Disputing it again anyway." }), HttpStatusCode.Conflict)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Staff_cannot_investigate_a_group_they_belong_to()
    {
        var (org, ada, g, _, d) = await DisputedRecord("Conflict Ajo");
        await Json(await ada.PostAsync($"/api/v1/disputes/{d}/escalate", null), HttpStatusCode.OK);
        // Make the organizer a staff member: they must not handle their own group's case.
        var email = (await Json(await org.GetAsync("/api/v1/me"), HttpStatusCode.OK)).GetProperty("email").GetString()!;
        await fx.Sql("UPDATE users SET is_operations = true WHERE email = @email", ("email", email));
        var setup = await Json(await org.PostAsync("/api/v1/operations/mfa/setup", null), HttpStatusCode.OK);
        await Json(await org.PostAsJsonAsync("/api/v1/operations/mfa/enable", new { code = ApiFixture.TotpNow(setup.GetProperty("secret").GetString()!) }), HttpStatusCode.NoContent);
        await Json(await org.GetAsync($"/api/v1/operations/disputes/{d}"), HttpStatusCode.Forbidden);
    }
}
