using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AjoCircle.Api.Infrastructure;
using AjoCircle.Application;
using AjoCircle.Infrastructure;
using AjoCircle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();

// Session cookie: HttpOnly, Secure (outside Development), SameSite=Strict, revocable via security stamp.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "ajocircle.session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")
            ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        o.ExpireTimeSpan = TimeSpan.FromDays(7);
        o.SlidingExpiration = true;
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
        o.Events.OnValidatePrincipal = SessionValidation.ValidateAsync;
    });
builder.Services.AddAuthorization();

// Brief §9.1: rate-limit login, recovery, invitation and dispute endpoints.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static string Key(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var authLimit = builder.Configuration.GetValue("RateLimits:AuthPerMinute", 10);
    var inviteLimit = builder.Configuration.GetValue("RateLimits:InvitationsPerMinute", 30);
    o.AddPolicy("auth", c => RateLimitPartition.GetFixedWindowLimiter(Key(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authLimit, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("invitations", c => RateLimitPartition.GetFixedWindowLimiter(Key(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = inviteLimit, Window = TimeSpan.FromMinutes(1) }));
    var verifyLimit = builder.Configuration.GetValue("RateLimits:VerificationPerMinute", 5);
    o.AddPolicy("verification", c => RateLimitPartition.GetFixedWindowLimiter(Key(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = verifyLimit, Window = TimeSpan.FromMinutes(1) }));
    var disputeLimit = builder.Configuration.GetValue("RateLimits:DisputesPerMinute", 10);
    o.AddPolicy("disputes", c => RateLimitPartition.GetFixedWindowLimiter(Key(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = disputeLimit, Window = TimeSpan.FromMinutes(1) }));
});

// Behind a reverse proxy (Cloudflare, nginx, a platform load balancer) the TCP peer is the proxy, so every visitor
// would share one rate-limit bucket. ReverseProxy:Enabled reads the visitor's address from a forwarded header, but
// only when the request came from a trusted network, so a client can't spoof its address by sending the header itself.
var proxy = builder.Configuration.GetSection("ReverseProxy");
var proxyEnabled = proxy.GetValue("Enabled", false);
if (proxyEnabled)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardedForHeaderName = proxy["ClientIpHeader"] is { Length: > 0 } h ? h : ForwardedHeadersDefaults.XForwardedForHeaderName;
        o.ForwardLimit = proxy.GetValue("ForwardLimit", 1);
        foreach (var cidr in proxy.GetSection("TrustedNetworks").Get<string[]>() ?? [])
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr.Trim()));
    });
}

var app = builder.Build();

// Staff accounts are granted from the command line, never through the web API:
//   dotnet run --project src/AjoCircle.Api -- grant-operations someone@example.com
//   dotnet run --project src/AjoCircle.Api -- revoke-operations someone@example.com
if (args.Length == 2 && args[0] is "grant-operations" or "revoke-operations")
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (app.Configuration.GetValue("Database:MigrateOnStartup", false)) db.Database.Migrate();
    var email = AjoCircle.Domain.Identity.User.NormalizeEmail(args[1]);
    var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
    if (user is null) { Console.Error.WriteLine($"No account with email {email}."); return 1; }
    if (args[0] == "grant-operations") user.GrantOperations(); else user.RevokeOperations();
    var audit = new AuditWriter(db, scope.ServiceProvider.GetRequiredService<IAppClock>());
    audit.Write(null, args[0] == "grant-operations" ? "operations.role_granted" : "operations.role_revoked", "user", user.Id, null, new { via = "cli" });
    await db.SaveChangesAsync();
    Console.WriteLine($"{args[0]}: {email} done. They must set up two-step sign-in before using the operations workspace.");
    return 0;
}

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

if (proxyEnabled) app.UseForwardedHeaders(); // first, so rate limits, HSTS and logs see the real client
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
else app.UseHsts();

app.UseAuthentication();
app.UseMiddleware<CsrfHeaderMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
// Liveness: the process is up. Readiness: it can also reach PostgreSQL, so a load balancer or uptime monitor stops
// sending traffic (or alerts) when the database is down. Neither response says anything about the failure.
app.MapGet("/api/v1/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/v1/health/ready", async (AppDbContext db, CancellationToken ct) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(3));
    bool up;
    try { up = await db.Database.CanConnectAsync(timeout.Token); } catch { up = false; }
    return up ? Results.Ok(new { status = "ok" })
              : Results.Json(new { status = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();
return 0;

public partial class Program;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public DateTimeOffset? MfaVerifiedAt =>
        long.TryParse(accessor.HttpContext?.User.FindFirstValue(SessionValidation.MfaClaim), out var s) ? DateTimeOffset.FromUnixTimeSeconds(s) : null;
}
