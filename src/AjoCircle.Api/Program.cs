using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AjoCircle.Api.Infrastructure;
using AjoCircle.Application;
using AjoCircle.Infrastructure;
using AjoCircle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
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
    var disputeLimit = builder.Configuration.GetValue("RateLimits:DisputesPerMinute", 10);
    o.AddPolicy("disputes", c => RateLimitPartition.GetFixedWindowLimiter(Key(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = disputeLimit, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

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
app.MapGet("/api/v1/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
