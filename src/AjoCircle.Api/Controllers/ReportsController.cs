using AjoCircle.Application;
using AjoCircle.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AjoCircle.Api.Controllers;

/// <summary>Group reports (brief §14.2): JSON for the screen, CSV for spreadsheets. Same visibility rules as the app.</summary>
[ApiController]
[Authorize]
[Route("api/v1/groups/{groupId:guid}/reports")]
public sealed class ReportsController(ReportService reports) : ControllerBase
{
    [HttpGet]
    public Task<ReportSummaryDto> Summary(Guid groupId, CancellationToken ct) => reports.SummaryAsync(groupId, ct);

    [HttpGet("{kind}")]
    public Task<ReportTable> Report(Guid groupId, string kind, CancellationToken ct) => reports.BuildAsync(groupId, Parse(kind), ct);

    [HttpGet("{kind}/csv")]
    public async Task<IActionResult> Csv(Guid groupId, string kind, CancellationToken ct)
    {
        var (name, content) = await reports.ExportCsvAsync(groupId, Parse(kind), ct);
        Response.Headers.CacheControl = "no-store";
        return File(content, "text/csv; charset=utf-8", name);
    }

    private static ReportKind Parse(string kind) =>
        Enum.TryParse<ReportKind>(kind, ignoreCase: true, out var k) && Enum.IsDefined(k) ? k : throw new NotFoundException("Unknown report.");
}
