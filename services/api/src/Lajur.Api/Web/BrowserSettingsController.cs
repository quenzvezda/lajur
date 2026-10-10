using System.ComponentModel.DataAnnotations;
using Lajur.Application.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lajur.Api.Web;

[ApiController]
[Authorize(Policy = WebViewerSetup.Policy)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("web/books/{year:int}/settings")]
public sealed class BrowserSettingsController(IWorkbookSettingsService settings) : ControllerBase
{
    [HttpGet("budget")]
    public async Task<IActionResult> Budget([Range(1900, 9999)] int year, CancellationToken cancellationToken)
        => User.TryGetWebUserId(out var userId) ? Ok(await settings.GetBudgetAsync(userId, year, cancellationToken)) : Unauthorized();

    [HttpGet("projection/{month:int}")]
    public async Task<IActionResult> Projection([Range(1900, 9999)] int year, [Range(1, 12)] int month, CancellationToken cancellationToken)
        => User.TryGetWebUserId(out var userId) ? Ok(await settings.GetProjectionAsync(userId, year, month, cancellationToken)) : Unauthorized();

    [HttpGet("operations/{operationId:guid}")]
    public async Task<IActionResult> Operation([Range(1900, 9999)] int year, Guid operationId, CancellationToken cancellationToken)
    {
        if (!User.TryGetWebUserId(out var userId)) return Unauthorized();
        var operation = await settings.GetOperationAsync(userId, year, operationId, cancellationToken);
        return operation is null ? NotFound() : Ok(operation);
    }

    [HttpPut("budget")]
    public async Task<IActionResult> SaveBudget([Range(1900, 9999)] int year, SaveBudgetSettings command, CancellationToken cancellationToken)
        => User.TryGetWebUserId(out var userId) ? Ok(await settings.SaveBudgetAsync(userId, year, command, HttpContext.TraceIdentifier, cancellationToken)) : Unauthorized();

    [HttpPut("projection/{month:int}")]
    public async Task<IActionResult> SaveProjection([Range(1900, 9999)] int year, [Range(1, 12)] int month, SaveProjectionSettings command, CancellationToken cancellationToken)
        => User.TryGetWebUserId(out var userId) ? Ok(await settings.SaveProjectionAsync(userId, year, month, command, HttpContext.TraceIdentifier, cancellationToken)) : Unauthorized();
}
