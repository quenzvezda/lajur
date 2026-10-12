using System.ComponentModel.DataAnnotations;
using Lajur.Api.Auth;
using Lajur.Api.Web;
using Lajur.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lajur.Api.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/reports")]
public sealed class ReportsController(IFinanceReportService reports, ReportClock clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<YearReport>> Get([FromQuery, Range(1900, 9999)] int year,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetSessionIds(out var userId, out _, out _)) return Unauthorized();
        var report = await reports.GetYearReportAsync(userId, year, clock.Today(), cancellationToken);
        return report is null ? NotFound() : Ok(report);
    }
}
