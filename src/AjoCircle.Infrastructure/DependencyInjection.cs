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

        var app = config.GetSection(AppOptions.Section).Get<AppOptions>() ?? new AppOptions();
        services.AddSingleton(app);
        services.AddSingleton<EmailTemplates>();

        // Email vendor is an open decision (brief §16.3). If an SMTP server is configured, mail goes through it (locally
        // that is Mailpit). Development also logs every email, so sign-up works even when Mailpit isn't running.
        // Any other environment without SMTP refuses to send rather than pretending an email went out.
        var smtp = config.GetSection(SmtpOptions.Section).Get<SmtpOptions>();
        SmtpEmailSender? smtpSender = string.IsNullOrWhiteSpace(smtp?.Host) ? null : new SmtpEmailSender(smtp, app);
        if (env.IsDevelopment())
            services.AddSingleton<IEmailSender>(sp => new DevelopmentEmailSender(smtpSender, sp.GetRequiredService<ILogger<DevelopmentEmailSender>>()));
        else if (smtpSender is not null) services.AddSingleton<IEmailSender>(smtpSender);
        else services.AddSingleton<IEmailSender, UnconfiguredEmailSender>();

        services.AddScoped<Notifier>();
        services.AddScoped<NotificationService>();
        services.AddScoped<NotificationDispatcher>();
        services.AddScoped<ReminderScheduler>();
        if (config.GetValue("Notifications:WorkerEnabled", true)) services.AddHostedService<NotificationWorker>();

        services.AddScoped<AuditWriter>();
        services.AddScoped<AuthService>();
        services.AddScoped<SavingsLedger>();
        services.AddScoped<SavingsService>();
        services.AddScoped<GroupService>();
        services.AddScoped<InvitationService>();
        services.AddScoped<GroupAccess>();
        services.AddScoped<RecordService>();
        services.AddScoped<DisputeService>();
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

