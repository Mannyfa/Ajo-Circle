using AjoCircle.Api.Infrastructure;
using AjoCircle.Application;
using AjoCircle.Application.Contracts;
using AjoCircle.Domain.Identity;
using AjoCircle.Domain.Records;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AjoCircle.Api.Controllers;

/// <summary>Identity verification for the signed-in user (owner decision 7).</summary>
[ApiController]
[Authorize]
[Route("api/v1/me")]
public sealed class VerificationController(IdentityService identity) : ControllerBase
{
    [HttpGet("verification")]
    public Task<VerificationStatusDto> Status(CancellationToken ct) => identity.StatusAsync(ct);

    [EnableRateLimiting("auth")]
    [HttpPost("phone")]
    public async Task<IActionResult> RequestPhoneCode(PhoneRequest req, CancellationToken ct)
    {
        await identity.RequestPhoneCodeAsync(req, ct);
        return Accepted();
    }

    [EnableRateLimiting("auth")]
    [HttpPost("phone/verify")]
    public async Task<VerificationStatusDto> VerifyPhone(VerifyContactRequest req, CancellationToken ct)
    {
        await identity.VerifyPhoneAsync(req, ct);
        return await identity.StatusAsync(ct);
    }

    /// <summary>
    /// Multipart form: level, idType, idNumber, bvn (organizers), idFront, idBack (optional), passportPhoto (organizers).
    /// The file type is taken from the bytes, not the browser's label.
    /// </summary>
    [EnableRateLimiting("verification")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [HttpPost("verification")]
    public async Task<ActionResult<VerificationStatusDto>> Submit([FromForm] SubmitVerificationForm form, CancellationToken ct)
    {
        var files = new List<UploadedFileDto>();
        async Task Add(IFormFile? f, IdentityDocumentKind kind)
        {
            if (f is null || f.Length == 0) return;
            if (f.Length > IdentitySubmission.MaxFileBytes) { files.Add(new UploadedFileDto(kind, "too-large", [])); return; }
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            files.Add(new UploadedFileDto(kind, IdentitySubmission.Sniff(bytes) ?? "unknown", bytes));
        }
        await Add(form.IdFront, IdentityDocumentKind.IdFront);
        await Add(form.IdBack, IdentityDocumentKind.IdBack);
        await Add(form.PassportPhoto, IdentityDocumentKind.PassportPhoto);
        await identity.SubmitAsync(new SubmitVerificationCommand(form.Level, form.IdType, form.IdNumber, form.Bvn, files), ct);
        return StatusCode(201, await identity.StatusAsync(ct));
    }
}

public sealed class SubmitVerificationForm
{
    public IdentityLevel Level { get; set; }
    public IdDocumentType IdType { get; set; }
    public string? IdNumber { get; set; }
    public string? Bvn { get; set; }
    public IFormFile? IdFront { get; set; }
    public IFormFile? IdBack { get; set; }
    public IFormFile? PassportPhoto { get; set; }
}

/// <summary>
/// Internal operations workspace (brief §5.4). Every endpoint checks the operations role and a recent second factor on
/// the server; for everyone else the workspace returns 404.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/operations")]
public sealed class OperationsController(OperationsService ops, DisputeService disputes, IAppClock clock) : ControllerBase
{
    // ---- Second factor ----

    [HttpPost("mfa/setup")]
    public Task<MfaSetupDto> BeginMfa(CancellationToken ct) => ops.BeginMfaSetupAsync(ct);

    [EnableRateLimiting("auth")]
    [HttpPost("mfa/enable")]
    public async Task<IActionResult> EnableMfa(MfaCodeRequest req, CancellationToken ct)
    {
        var user = await ops.EnableMfaAsync(req, ct);
        await StepUpAsync(user);
        return NoContent();
    }

    [EnableRateLimiting("auth")]
    [HttpPost("mfa/verify")]
    public async Task<IActionResult> VerifyMfa(MfaCodeRequest req, CancellationToken ct)
    {
        var user = await ops.VerifyMfaAsync(req, ct);
        await StepUpAsync(user);
        return NoContent();
    }

    /// <summary>Re-issues the session cookie marked as having passed the second factor now.</summary>
    private Task StepUpAsync(User user) =>
        HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, SessionValidation.CreatePrincipal(user, clock.UtcNow));

    // ---- Identity verification queue ----

    [HttpGet("verifications")]
    public Task<IReadOnlyList<VerificationQueueItemDto>> Verifications([FromQuery] IdentitySubmissionStatus? status, CancellationToken ct) =>
        ops.VerificationsAsync(status, ct);

    [HttpGet("verifications/{id:guid}")]
    public Task<VerificationDetailDto> Verification(Guid id, CancellationToken ct) => ops.VerificationAsync(id, ct);

    [HttpGet("verifications/{id:guid}/documents/{documentId:guid}")]
    public async Task<IActionResult> Document(Guid id, Guid documentId, CancellationToken ct)
    {
        var file = await ops.DocumentAsync(id, documentId, ct);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.ContentDisposition = "inline";
        return File(file.Content, file.ContentType);
    }

    [HttpPost("verifications/{id:guid}/reveal")]
    public Task<RevealedIdentifiersDto> Reveal(Guid id, CancellationToken ct) => ops.RevealAsync(id, ct);

    [HttpPost("verifications/{id:guid}/approve")]
    public async Task<VerificationDetailDto> Approve(Guid id, ReviewDecisionRequest req, CancellationToken ct)
    {
        await ops.ApproveAsync(id, req, ct);
        return await ops.VerificationAsync(id, ct);
    }

    [HttpPost("verifications/{id:guid}/reject")]
    public async Task<VerificationDetailDto> Reject(Guid id, ReviewDecisionRequest req, CancellationToken ct)
    {
        await ops.RejectAsync(id, req, ct);
        return await ops.VerificationAsync(id, ct);
    }

    // ---- Disputes ----

    [HttpGet("disputes")]
    public Task<IReadOnlyList<DisputeSummaryDto>> Disputes([FromQuery] DisputeStatus? status, CancellationToken ct) => disputes.OperationsQueueAsync(status, ct);

    [HttpGet("disputes/{id:guid}")]
    public Task<DisputeDetailDto> Dispute(Guid id, CancellationToken ct) => disputes.OperationsDetailAsync(id, ct);

    [HttpPost("disputes/{id:guid}/messages")]
    public async Task<DisputeDetailDto> Reply(Guid id, DisputeMessageRequest req, CancellationToken ct)
    {
        await disputes.OperationsReplyAsync(id, req, ct);
        return await disputes.OperationsDetailAsync(id, ct);
    }

    [HttpPost("disputes/{id:guid}/decision")]
    public async Task<DisputeDetailDto> Decide(Guid id, OperationsDecisionRequest req, CancellationToken ct)
    {
        await disputes.DecideAsync(id, req, ct);
        return await disputes.OperationsDetailAsync(id, ct);
    }

    // ---- Audit search ----

    [HttpGet("audit-logs")]
    public Task<PagedResult<AuditSearchItemDto>> Audit([FromQuery] Guid? groupId, [FromQuery] string? action, [FromQuery] string? actorEmail,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        ops.SearchAuditAsync(groupId, action, actorEmail, from, to, page, pageSize, ct);
}
