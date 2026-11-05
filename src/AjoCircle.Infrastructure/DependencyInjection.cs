using AjoCircle.Application;
using AjoCircle.Domain.Identity;
using AjoCircle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AjoCircle.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var cs = config.GetConnectionString("AjoCircle")
                 ?? throw new InvalidOperationException("Connection string 'AjoCircle' is not configured.");
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(cs).UseSnakeCaseNamingConvention());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<IAppClock, AppClock>();
        services.AddSingleton<IPasswordHashing, PasswordHashing>();

        // Email/SMS vendor is an open decision (brief §16.3). Development logs codes locally; any other
        // environment refuses to send until a real provider is wired in.
        if (env.IsDevelopment()) services.AddSingleton<IContactCodeSender, DevelopmentLogCodeSender>();
        else services.AddSingleton<IContactCodeSender, UnconfiguredCodeSender>();

        services.AddScoped<AuditWriter>();
        services.AddScoped<AuthService>();
        services.AddScoped<GroupService>();
        services.AddScoped<InvitationService>();
        return services;
    }
}

public sealed class AppClock(TimeProvider time) : IAppClock
{
    // Nigeria observes WAT (UTC+1) with no daylight saving.
    private static readonly TimeZoneInfo Lagos = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
    public AppClock() : this(TimeProvider.System) { }
    public DateTimeOffset UtcNow => time.GetUtcNow();
    public DateOnly LagosToday => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, Lagos).DateTime);
}

public sealed class PasswordHashing : IPasswordHashing
{
    private readonly PasswordHasher<User> _hasher = new(); // PBKDF2-HMAC-SHA512, 100k iterations (Identity v3 format)
    public string Hash(User user, string password) => _hasher.HashPassword(user, password);
    public bool Verify(User user, string password) =>
        _hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
}

internal sealed class DevelopmentLogCodeSender(ILogger<DevelopmentLogCodeSender> log) : IContactCodeSender
{
    public Task SendVerificationCodeAsync(string email, string displayName, string code, CancellationToken ct)
    {
        log.LogWarning("[DEV ONLY] Verification code for {Email}: {Code}", email, code);
        return Task.CompletedTask;
    }
}

internal sealed class UnconfiguredCodeSender : IContactCodeSender
{
    public Task SendVerificationCodeAsync(string email, string displayName, string code, CancellationToken ct) =>
        throw new InvalidOperationException("No email/SMS provider is configured for contact verification.");
}
