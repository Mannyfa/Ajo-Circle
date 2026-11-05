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
    public CapturingCodeSender Codes { get; } = new();
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
            b.ConfigureTestServices(s => s.AddSingleton<IContactCodeSender>(Codes));
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
        (await c.PostAsJsonAsync("/api/v1/auth/verify-contact", new { code = Codes.Latest(email) })).EnsureSuccessStatusCode();
        return c;
    }
}

public sealed class CapturingCodeSender : IContactCodeSender
{
    private readonly ConcurrentDictionary<string, string> _codes = new();
    public Task SendVerificationCodeAsync(string email, string displayName, string code, CancellationToken ct)
    {
        _codes[email] = code;
        return Task.CompletedTask;
    }
    public string Latest(string email) => _codes[email];
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
