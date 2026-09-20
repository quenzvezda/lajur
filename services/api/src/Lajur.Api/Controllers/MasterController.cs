using System.ComponentModel.DataAnnotations;
using Lajur.Api.Auth;
using Lajur.Application.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lajur.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/master")]
public sealed class MasterController(IFinanceService financeService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<FinanceMasterData>> Get(
        [FromQuery, Range(1900, 9999)] int year,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetSessionIds(out var userId, out _, out _))
        {
            return Unauthorized();
        }

        return Ok(await financeService.GetMasterDataAsync(userId, year, cancellationToken));
    }
}
