using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AjoCircle.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace AjoCircle.ApiTests;

/// <summary>Disposable PostgreSQL (Testcontainers — requires Docker) + in-memory API host. Synthetic data only.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:17").Build();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public CapturingEmailSender Emails { get; } = new();
    public CapturingSmsSender Sms { get; } = new();
    public string ConnectionString => _pg.GetConnectionString();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:AjoCircle", _pg.GetConnectionString());
            b.UseSetting("Database:MigrateOnStartup", "true");
            b.UseSetting("RateLimits:AuthPerMinute", "10000");
            b.UseSetting("RateLimits:InvitationsPerMinute", "10000");
            b.UseSetting("RateLimits:DisputesPerMinute", "10000");
            b.UseSetting("RateLimits:VerificationPerMinute", "10000");
            b.UseSetting("Notifications:WorkerEnabled", "false"); // tests drive the dispatcher/scheduler explicitly
            b.UseSetting("Disputes:EscalationAfterDays", "0");      // the 7-day window is covered by unit tests
            b.UseSetting("Identity:DocumentRetentionDays", "0");    // tests call the purge explicitly
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IEmailSender>(Emails);
                s.AddSingleton<ISmsSender>(Sms);
            });
        });
        _ = Factory.Server; // boot + migrate
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _pg.DisposeAsync();
    }

    /// <summary>A browser-like client: keeps its own cookie jar and sends the CSRF header.</summary>
    public HttpClient Client()
    {
        var c = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        c.DefaultRequestHeaders.Add("X-AjoCircle-Request", "1");
        return c;
    }

    /// <summary>
    /// Registers a fresh synthetic user and verifies their email. By default they register as an organizer and are also
    /// marked identity-verified as an organizer directly in the database (other levels register as members), so tests about other features aren't blocked by the identity check (which has
    /// its own tests that go through the real flow).
    /// </summary>
    public async Task<HttpClient> VerifiedUser(string name, string identityLevel = "Organizer")
    {
        var c = Client();
        var email = $"{name.ToLowerInvariant()}.{Guid.NewGuid():N}@example.test";
        (await c.PostAsJsonAsync("/api/v1/auth/register", new { email, displayName = name, password = "correct-horse-battery", acceptTerms = true, role = identityLevel == "Organizer" ? "Organizer" : "Member" }))
            .EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/v1/auth/verify-contact", new { code = Emails.LatestCode(email) })).EnsureSuccessStatusCode();
        if (identityLevel != "None")
            await Sql("UPDATE users SET identity_level = @level, identity_verified_at = now(), phone_number = @phone, phone_verified_at = now() WHERE email = @email",
                ("level", identityLevel), ("phone", $"+23480{Random.Shared.NextInt64(10_000_000, 99_999_999)}"), ("email", email));
        return c;
    }

    /// <summary>An operations staff member who has set up two-step sign-in and passed it on this session.</summary>
    public async Task<(HttpClient Client, string Secret)> Operator(string name)
    {
        var c = await VerifiedUser(name, identityLevel: "None");
        var me = await c.GetFromJsonAsync<JsonElement>("/api/v1/me");
        await Sql("UPDATE users SET is_operations = true WHERE email = @email", ("email", me.GetProperty("email").GetString()!));
        var setup = await (await c.PostAsync("/api/v1/operations/mfa/setup", null)).Content.ReadFromJsonAsync<JsonElement>();
        var secret = setup.GetProperty("secret").GetString()!;
        (await c.PostAsJsonAsync("/api/v1/operations/mfa/enable", new { code = TotpNow(secret) })).EnsureSuccessStatusCode();
        return (c, secret);
    }

    public static string TotpNow(string base32Secret, int stepOffset = 0) =>
        AjoCircle.Domain.Identity.Totp.Code(FromBase32(base32Secret), AjoCircle.Domain.Identity.Totp.StepAt(DateTimeOffset.UtcNow) + stepOffset);

    private static byte[] FromBase32(string s)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var ch in s)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(ch); bits += 5;
            if (bits >= 8) { bytes.Add((byte)(buffer >> (bits - 8))); bits -= 8; }
        }
        return bytes.ToArray();
    }

    public async Task Sql(string sql, params (string Name, object Value)[] args)
    {
        await using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>Captures every email so tests can read codes and links. Thread-safe; tests run in parallel collections.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();
    public Task SendAsync(EmailMessage message, CancellationToken ct) { _sent.Enqueue(message); return Task.CompletedTask; }
    public IReadOnlyList<EmailMessage> To(string email) => _sent.Where(m => m.To == email).ToList();
    public string LatestCode(string email) =>
        System.Text.RegularExpressions.Regex.Match(To(email).Last(m => m.Subject.Contains("verification code")).TextBody, @"\b\d{6}\b").Value;
    public string LatestResetToken(string email) =>
        System.Text.RegularExpressions.Regex.Match(To(email).Last(m => m.Subject.Contains("Reset")).TextBody, @"token=([A-Za-z0-9_-]+)").Groups[1].Value;
}

public sealed class CapturingSmsSender : ISmsSender
{
    private readonly ConcurrentQueue<(string Phone, string Text)> _sent = new();
    public Task SendAsync(string e164Phone, string text, CancellationToken ct) { _sent.Enqueue((e164Phone, text)); return Task.CompletedTask; }
    public string LatestCode(string e164Phone) =>
        System.Text.RegularExpressions.Regex.Match(_sent.Last(m => m.Phone == e164Phone).Text, @"\b\d{6}\b").Value;
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
