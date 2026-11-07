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
            b.UseSetting("Notifications:WorkerEnabled", "false"); // tests drive the dispatcher/scheduler explicitly
            b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(Emails));
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

    /// <summary>Registers and verifies a fresh synthetic user; returns their signed-in client.</summary>
    public async Task<HttpClient> VerifiedUser(string name)
    {
        var c = Client();
        var email = $"{name.ToLowerInvariant()}.{Guid.NewGuid():N}@example.test";
        (await c.PostAsJsonAsync("/api/v1/auth/register", new { email, displayName = name, password = "correct-horse-battery", acceptTerms = true }))
            .EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/v1/auth/verify-contact", new { code = Emails.LatestCode(email) })).EnsureSuccessStatusCode();
        return c;
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

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
