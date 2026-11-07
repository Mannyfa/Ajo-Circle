using AjoCircle.Application;
using AjoCircle.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AjoCircle.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class GroupsController(GroupService groups, InvitationService invitations, SavingsService savings) : ControllerBase
{
    /// <summary>Stateless calculation for the create-group wizard. Server totals are authoritative.</summary>
    [HttpPost("schemes/rotating/preview")]
    public SchemePreviewDto PreviewRules(RotatingRulesDto rules) => groups.Preview(rules);

    [HttpPost("schemes/fixed-term/preview")]
    public SchemePreviewDto PreviewFixedTerm(FixedTermRulesDto rules) => groups.PreviewFixedTerm(rules);

    [HttpGet("groups")]
    public Task<IReadOnlyList<GroupListItemDto>> List(CancellationToken ct) => groups.ListMineAsync(ct);

    [HttpPost("groups/fixed-term")]
    public async Task<ActionResult<GroupDetailDto>> CreateFixedTerm(CreateFixedTermGroupRequest req, CancellationToken ct)
    {
        var id = await savings.CreateAsync(req, ct);
        return CreatedAtAction(nameof(Get), new { groupId = id }, await groups.GetAsync(id, ct));
    }

    [HttpPatch("groups/{groupId:guid}/fixed-term")]
    public async Task<GroupDetailDto> UpdateFixedTermDraft(Guid groupId, CreateFixedTermGroupRequest req, CancellationToken ct)
    {
        await savings.UpdateDraftAsync(groupId, req, ct);
        return await groups.GetAsync(groupId, ct);
    }

    [HttpPut("groups/{groupId:guid}/shares")]
    public async Task<IReadOnlyList<MemberDto>> AssignShares(Guid groupId, AssignSharesRequest req, CancellationToken ct)
    {
        await savings.AssignSharesAsync(groupId, req, ct);
        return await groups.MembersAsync(groupId, ct);
    }

    /// <summary>A member leaves a running fixed-term group early (themselves, or recorded by the organizer).</summary>
    [HttpPost("groups/{groupId:guid}/members/{membershipId:guid}/exit")]
    public async Task<GroupDetailDto> Exit(Guid groupId, Guid membershipId, ExitMemberRequest req, CancellationToken ct)
    {
        await savings.ExitAsync(groupId, membershipId, req, ct);
        return await groups.GetAsync(groupId, ct);
    }

    /// <summary>Ends a fixed-term group's term and works out what is owed back to each member.</summary>
    [HttpPost("groups/{groupId:guid}/complete")]
    public async Task<GroupDetailDto> Complete(Guid groupId, CancellationToken ct)
    {
        await savings.CompleteAsync(groupId, ct);
        return await groups.GetAsync(groupId, ct);
    }

    [HttpGet("groups/{groupId:guid}/payouts")]
    public Task<IReadOnlyList<PayoutListItemDto>> Payouts(Guid groupId, CancellationToken ct) => groups.PayoutsAsync(groupId, ct);

    [HttpPost("groups")]
    public async Task<ActionResult<GroupDetailDto>> Create(CreateRotatingGroupRequest req, CancellationToken ct)
    {
        var id = await groups.CreateRotatingAsync(req, ct);
        return CreatedAtAction(nameof(Get), new { groupId = id }, await groups.GetAsync(id, ct));
    }

    [HttpGet("groups/{groupId:guid}")]
    public Task<GroupDetailDto> Get(Guid groupId, CancellationToken ct) => groups.GetAsync(groupId, ct);

    [HttpPatch("groups/{groupId:guid}")]
    public async Task<GroupDetailDto> UpdateDraft(Guid groupId, UpdateDraftGroupRequest req, CancellationToken ct)
    {
        await groups.UpdateDraftAsync(groupId, req, ct);
        return await groups.GetAsync(groupId, ct);
    }

    [HttpPost("groups/{groupId:guid}/publish")]
    public async Task<GroupDetailDto> Publish(Guid groupId, CancellationToken ct)
    {
        await groups.PublishAsync(groupId, ct);
        return await groups.GetAsync(groupId, ct);
    }

    [HttpPost("groups/{groupId:guid}/close-enrolment")]
    public async Task<GroupDetailDto> CloseEnrolment(Guid groupId, CancellationToken ct)
    {
        await groups.CloseEnrolmentAsync(groupId, ct);
        return await groups.GetAsync(groupId, ct);
    }

    [HttpPost("groups/{groupId:guid}/start")]
    public async Task<GroupDetailDto> Start(Guid groupId, CancellationToken ct)
    {
        await groups.StartAsync(groupId, ct);
        return await groups.GetAsync(groupId, ct);
    }

    [HttpPost("groups/{groupId:guid}/cancel")]
    public async Task<GroupDetailDto> Cancel(Guid groupId, CancelGroupRequest req, CancellationToken ct)
    {
        await groups.CancelAsync(groupId, req, ct);
        return await groups.GetAsync(groupId, ct);
    }

    [HttpGet("groups/{groupId:guid}/schedule/preview")]
    public Task<SchemePreviewDto> SchedulePreview(Guid groupId, CancellationToken ct) => groups.SchedulePreviewAsync(groupId, ct);

    [HttpGet("groups/{groupId:guid}/scheme-versions")]
    public Task<IReadOnlyList<SchemeVersionDto>> SchemeVersions(Guid groupId, CancellationToken ct) => groups.SchemeVersionsAsync(groupId, ct);

    [HttpGet("groups/{groupId:guid}/members")]
    public Task<IReadOnlyList<MemberDto>> Members(Guid groupId, CancellationToken ct) => groups.MembersAsync(groupId, ct);

    [HttpPut("groups/{groupId:guid}/payout-positions")]
    public async Task<IReadOnlyList<MemberDto>> AssignPayoutOrder(Guid groupId, AssignPayoutOrderRequest req, CancellationToken ct)
    {
        await groups.AssignPayoutOrderAsync(groupId, req, ct);
        return await groups.MembersAsync(groupId, ct);
    }

    [HttpGet("groups/{groupId:guid}/rounds")]
    public Task<IReadOnlyList<RoundDto>> Rounds(Guid groupId, CancellationToken ct) => groups.RoundsAsync(groupId, ct);

    [HttpGet("groups/{groupId:guid}/contributions")]
    public Task<PagedResult<ObligationDto>> Contributions(Guid groupId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        groups.ContributionsAsync(groupId, page, pageSize, ct);

    [HttpGet("groups/{groupId:guid}/audit-logs")]
    public Task<PagedResult<AuditLogDto>> AuditLogs(Guid groupId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        groups.AuditLogsAsync(groupId, page, pageSize, ct);

    // ---- Invitations (organizer) ----

    [HttpGet("groups/{groupId:guid}/invitations")]
    public Task<IReadOnlyList<InvitationDto>> Invitations(Guid groupId, CancellationToken ct) => invitations.ListAsync(groupId, ct);

    [EnableRateLimiting("invitations")]
    [HttpPost("groups/{groupId:guid}/invitations")]
    public async Task<ActionResult<CreatedInvitationDto>> CreateInvitation(Guid groupId, CreateInvitationRequest req, CancellationToken ct) =>
        StatusCode(201, await invitations.CreateAsync(groupId, req, ct));

    [HttpDelete("groups/{groupId:guid}/invitations/{invitationId:guid}")]
    public async Task<IActionResult> RevokeInvitation(Guid groupId, Guid invitationId, CancellationToken ct)
    {
        await invitations.RevokeAsync(groupId, invitationId, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/invitations")]
[EnableRateLimiting("invitations")]
public sealed class InvitationsController(InvitationService invitations) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("{token}")]
    public Task<PublicInvitationDto> View(string token, CancellationToken ct) => invitations.PublicViewAsync(token, ct);

    [Authorize]
    [HttpPost("{token}/join")]
    public async Task<ActionResult<JoinResultDto>> Join(string token, JoinGroupRequest req, CancellationToken ct) =>
        StatusCode(201, await invitations.JoinAsync(token, req, ct));
}
