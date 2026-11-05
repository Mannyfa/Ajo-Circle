using AjoCircle.Api.Infrastructure;
using AjoCircle.Application;
using AjoCircle.Application.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AjoCircle.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController(AuthService auth, ICurrentUser current) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<MeDto>> Register(RegisterRequest req, CancellationToken ct)
    {
        var user = await auth.RegisterAsync(req, ct);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, SessionValidation.CreatePrincipal(user));
        return StatusCode(201, await auth.GetMeAsync(user.Id, ct));
    }

    [HttpPost("login")]
    public async Task<ActionResult<MeDto>> Login(LoginRequest req, CancellationToken ct)
    {
        var user = await auth.LoginAsync(req, ct);
        if (user is null)
            return Problem(statusCode: 401, title: "Email or password is incorrect.");
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, SessionValidation.CreatePrincipal(user));
        return await auth.GetMeAsync(user.Id, ct);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [Authorize]
    [HttpPost("verify-contact")]
    public async Task<ActionResult<MeDto>> VerifyContact(VerifyContactRequest req, CancellationToken ct)
    {
        var id = current.UserId!.Value;
        await auth.VerifyContactAsync(id, req.Code, ct);
        return await auth.GetMeAsync(id, ct);
    }

    [Authorize]
    [HttpPost("resend-verification")]
    public async Task<IActionResult> Resend(CancellationToken ct)
    {
        await auth.ResendCodeAsync(current.UserId!.Value, ct);
        return Accepted();
    }
}

[ApiController]
[Authorize]
[Route("api/v1/me")]
public sealed class MeController(AuthService auth, ICurrentUser current) : ControllerBase
{
    [HttpGet]
    public Task<MeDto> Get(CancellationToken ct) => auth.GetMeAsync(current.UserId!.Value, ct);

    [HttpPatch]
    public Task<MeDto> Update(UpdateProfileRequest req, CancellationToken ct) => auth.UpdateProfileAsync(current.UserId!.Value, req, ct);
}
