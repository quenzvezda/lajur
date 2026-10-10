using System.ComponentModel.DataAnnotations;
using Lajur.Application.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lajur.Api.Web;

[ApiController]
[Authorize(Policy = WebViewerSetup.Policy)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("web/books/{year:int}/master")]
public sealed class BrowserMasterController(IWorkbookMasterService master) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([Range(1900, 9999)] int year, CancellationToken cancellationToken)
        => User.TryGetWebUserId(out var userId) ? Ok(await master.GetAsync(userId, year, cancellationToken)) : Unauthorized();
    [HttpPut]
    public async Task<IActionResult> Change([Range(1900, 9999)] int year, ChangeWorkbookMaster command, CancellationToken cancellationToken)
        => User.TryGetWebUserId(out var userId) ? Ok(await master.ChangeAsync(userId, year, command, HttpContext.TraceIdentifier, cancellationToken)) : Unauthorized();
}
