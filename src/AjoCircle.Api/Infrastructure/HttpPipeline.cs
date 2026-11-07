using System.Security.Claims;
using AjoCircle.Application;
using AjoCircle.Domain.Common;
using AjoCircle.Domain.Identity;
using AjoCircle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AjoCircle.Api.Infrastructure;

/// <summary>Maps exceptions to RFC 7807 ProblemDetails without leaking stack traces, SQL or secrets (brief §9.1).</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        ProblemDetails pd = ex switch
        {
            DomainValidationException v => new ValidationProblemDetails(
                v.Errors.ToDictionary(e => char.ToLowerInvariant(e.Key[0]) + e.Key[1..], e => e.Value))
            { Status = 400, Title = v.Message },
            DomainConflictException c => Problem(409, c.Message, c.Code),
            DomainException d => Problem(422, d.Message, d.Code),
            NotFoundException n => Problem(404, n.Message, "not_found"),
            ForbiddenException f => Problem(403, f.Message, "forbidden"),
            UnauthenticatedException u => Problem(401, u.Message, "unauthenticated"),
            DbUpdateConcurrencyException => Problem(409, "This record was changed by someone else. Reload and try again.", "concurrency_conflict"),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                Problem(409, "This would create a duplicate record.", "duplicate"),
            BadHttpRequestException b => Problem(400, "The request could not be read.", "bad_request"),
            _ => Problem(500, "Something went wrong on our side. Please try again.", "server_error"),
        };
        if (pd.Status >= 500) log.LogError(ex, "Unhandled error on {Path}", http.Request.Path);

        pd.Extensions["traceId"] = http.TraceIdentifier;
        http.Response.StatusCode = pd.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = pd, Exception = null });
    }

    private static ProblemDetails Problem(int status, string detail, string code) =>
        new() { Status = status, Title = detail, Extensions = { ["code"] = code } };
}

/// <summary>
/// CSRF defence for cookie auth: unsafe methods must carry a custom header. Cross-site pages cannot add it
/// without a CORS preflight, and CORS is not enabled. Combined with SameSite=Strict cookies.
/// </summary>
public sealed class CsrfHeaderMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-AjoCircle-Request";

    public Task InvokeAsync(HttpContext http)
    {
        if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method) && !HttpMethods.IsOptions(http.Request.Method)
            && http.Request.Path.StartsWithSegments("/api") && !http.Request.Headers.ContainsKey(HeaderName))
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return http.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = 403, Title = "Missing request header.", Extensions = { ["code"] = "csrf_header_missing" },
            });
        }
        return next(http);
    }
}

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http)
    {
        var h = http.Response.Headers;
        h.XContentTypeOptions = "nosniff";
        h.XFrameOptions = "DENY";
        h["Referrer-Policy"] = "no-referrer"; // invitation tokens live in URLs; never leak them via Referer
        h["Cache-Control"] = "no-store";
        h.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        return next(http);
    }
}

public static class SessionValidation
{
    public const string StampClaim = "ajo:stamp";
    /// <summary>Unix seconds when this session passed the operations second factor.</summary>
    public const string MfaClaim = "ajo:mfa";

    public static ClaimsPrincipal CreatePrincipal(User user, DateTimeOffset? mfaVerifiedAt = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(StampClaim, user.SecurityStamp),
        };
        if (mfaVerifiedAt is { } at) claims.Add(new Claim(MfaClaim, at.ToUnixTimeSeconds().ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// <summary>Rejects sessions whose user is locked or whose security stamp changed (e.g. password reset).</summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        var id = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = ctx.Principal?.FindFirstValue(StampClaim);
        var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var ok = Guid.TryParse(id, out var userId) && await db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.SecurityStamp == stamp && u.Status == UserStatus.Active);
        if (!ok)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
