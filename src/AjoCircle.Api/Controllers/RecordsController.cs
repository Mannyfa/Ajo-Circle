using AjoCircle.Application;
using AjoCircle.Application.Contracts;
using AjoCircle.Domain.Records;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AjoCircle.Api.Controllers;

/// <summary>
/// Organizer-reported payments (brief §9 Contributions / Payouts). Explicit commands only: there is no endpoint that
/// sets a status. Nothing here moves money.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class RecordsController(RecordService records, DisputeService disputes) : ControllerBase
{
    // ---- Contributions ----

    [HttpGet("groups/{groupId:guid}/contributions/{obligationId:guid}")]
    public Task<ObligationDetailDto> Contribution(Guid groupId, Guid obligationId, CancellationToken ct) =>
        records.ContributionAsync(groupId, obligationId, ct);

    [HttpPost("groups/{groupId:guid}/contributions/{obligationId:guid}/records")]
    public async Task<ActionResult<ObligationDetailDto>> RecordContribution(Guid groupId, Guid obligationId, RecordPaymentRequest req, CancellationToken ct)
    {
        await records.RecordContributionAsync(groupId, obligationId, req, ct);
        return StatusCode(201, await records.ContributionAsync(groupId, obligationId, ct));
    }

    [HttpPost("contribution-records/{recordId:guid}/confirm")]
    public async Task<ObligationDetailDto> ConfirmContribution(Guid recordId, CancellationToken ct)
    {
        var obligationId = await records.ConfirmContributionAsync(recordId, ct);
        return await ContributionByObligation(obligationId, ct);
    }

    [HttpPost("contribution-records/{recordId:guid}/amendments")]
    public async Task<ObligationDetailDto> CorrectContribution(Guid recordId, CorrectRecordRequest req, CancellationToken ct)
    {
        var obligationId = await records.CorrectContributionAsync(recordId, req, ct);
        return await ContributionByObligation(obligationId, ct);
    }

    [EnableRateLimiting("disputes")]
    [HttpPost("contribution-records/{recordId:guid}/dispute")]
    public async Task<ActionResult<DisputeDetailDto>> DisputeContribution(Guid recordId, RaiseDisputeRequest req, CancellationToken ct) =>
        StatusCode(201, await disputes.GetAsync(await disputes.RaiseAsync(RecordKind.ContributionRecord, recordId, req, ct), ct));

    // ---- Payouts ----

    [HttpGet("groups/{groupId:guid}/payouts/{payoutId:guid}")]
    public Task<PayoutDetailDto> Payout(Guid groupId, Guid payoutId, CancellationToken ct) => records.PayoutAsync(groupId, payoutId, ct);

    [HttpPost("groups/{groupId:guid}/payouts/{payoutId:guid}/records")]
    public async Task<ActionResult<PayoutDetailDto>> RecordPayout(Guid groupId, Guid payoutId, RecordPaymentRequest req, CancellationToken ct)
    {
        await records.RecordPayoutAsync(groupId, payoutId, req, ct);
        return StatusCode(201, await records.PayoutAsync(groupId, payoutId, ct));
    }

    [HttpPost("payout-records/{recordId:guid}/confirm")]
    public async Task<IActionResult> ConfirmPayout(Guid recordId, CancellationToken ct)
    {
        await records.ConfirmPayoutAsync(recordId, ct);
        return NoContent();
    }

    [HttpPost("payout-records/{recordId:guid}/amendments")]
    public async Task<IActionResult> CorrectPayout(Guid recordId, CorrectRecordRequest req, CancellationToken ct)
    {
        await records.CorrectPayoutAsync(recordId, req, ct);
        return NoContent();
    }

    [EnableRateLimiting("disputes")]
    [HttpPost("payout-records/{recordId:guid}/dispute")]
    public async Task<ActionResult<DisputeDetailDto>> DisputePayout(Guid recordId, RaiseDisputeRequest req, CancellationToken ct) =>
        StatusCode(201, await disputes.GetAsync(await disputes.RaiseAsync(RecordKind.PayoutRecord, recordId, req, ct), ct));

    private async Task<ObligationDetailDto> ContributionByObligation(Guid obligationId, CancellationToken ct) =>
        await records.ContributionAsync(await records.GroupOfObligationAsync(obligationId, ct), obligationId, ct);
}

[ApiController]
[Authorize]
[Route("api/v1/disputes")]
public sealed class DisputesController(DisputeService disputes) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<DisputeSummaryDto>> List([FromQuery] DisputeStatus? status, CancellationToken ct) => disputes.ListAsync(status, ct);

    [HttpGet("{disputeId:guid}")]
    public Task<DisputeDetailDto> Get(Guid disputeId, CancellationToken ct) => disputes.GetAsync(disputeId, ct);

    [EnableRateLimiting("disputes")]
    [HttpPost("{disputeId:guid}/responses")]
    public async Task<DisputeDetailDto> Reply(Guid disputeId, DisputeMessageRequest req, CancellationToken ct)
    {
        await disputes.AddMessageAsync(disputeId, req, ct);
        return await disputes.GetAsync(disputeId, ct);
    }

    /// <summary>Hands the dispute to the operations team once the organizer's response window has passed.</summary>
    [EnableRateLimiting("disputes")]
    [HttpPost("{disputeId:guid}/escalate")]
    public async Task<DisputeDetailDto> Escalate(Guid disputeId, CancellationToken ct)
    {
        await disputes.EscalateAsync(disputeId, ct);
        return await disputes.GetAsync(disputeId, ct);
    }

    [HttpPost("{disputeId:guid}/withdraw")]
    public async Task<DisputeDetailDto> Withdraw(Guid disputeId, WithdrawDisputeRequest req, CancellationToken ct)
    {
        await disputes.WithdrawAsync(disputeId, req, ct);
        return await disputes.GetAsync(disputeId, ct);
    }
}
