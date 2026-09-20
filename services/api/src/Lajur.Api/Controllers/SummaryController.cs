using System.ComponentModel.DataAnnotations;
using Lajur.Api.Auth;
using Lajur.Application.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lajur.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/summary")]
public sealed class SummaryController(IFinanceService financeService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<FinanceSummary>> Get(
        [FromQuery, Range(1900, 9999)] int year,
        [FromQuery, Range(1, 12)] int month,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetSessionIds(out var userId, out _, out _))
        {
            return Unauthorized();
        }

        return Ok(await financeService.GetSummaryAsync(userId, year, month, cancellationToken));
    }
}
