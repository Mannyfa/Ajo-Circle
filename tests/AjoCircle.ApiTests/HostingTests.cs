using System.Net;
using AjoCircle.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AjoCircle.ApiTests;

/// <summary>Launch readiness: things that only break once the app is deployed.</summary>
[Collection("api")]
public sealed class HostingTests(ApiFixture fx)
{
    [Fact]
    public async Task Readiness_check_reports_the_database_is_reachable()
    {
        var r = await fx.Client().GetAsync("/api/v1/health/ready");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Readiness_check_fails_without_revealing_why_when_the_database_is_down()
    {
        await using var down = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:AjoCircle", "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1");
            b.UseSetting("Notifications:WorkerEnabled", "false");
        });
        var r = await down.CreateClient().GetAsync("/api/v1/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.DoesNotContain("127.0.0.1", await r.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await down.CreateClient().GetAsync("/api/v1/health")).StatusCode);
    }

    /// <summary>
    /// The key ring lives in PostgreSQL, so data encrypted by one server instance can be read by the next one
    /// (a restart, a redeploy, or a second replica). Before this, a redeploy made encrypted identity data unreadable.
    /// </summary>
    [Fact]
    public async Task Encrypted_data_survives_a_new_server_instance()
    {
        var sealedText = fx.Factory.Services.GetRequiredService<ISecretProtector>().Protect("synthetic-secret-123");

        await using var next = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:AjoCircle", fx.ConnectionString);
            b.UseSetting("Notifications:WorkerEnabled", "false");
        });
        Assert.Equal("synthetic-secret-123", next.Services.GetRequiredService<ISecretProtector>().Unprotect(sealedText));

        // And the keys really are in the database, not in a folder on whichever machine happened to run the test.
        using var scope = fx.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AjoCircle.Infrastructure.Persistence.AppDbContext>();
        Assert.True(await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(db.DataProtectionKeys));
    }
}
