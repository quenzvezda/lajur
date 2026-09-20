using System.ComponentModel.DataAnnotations;
using Lajur.Api.Auth;
using Lajur.Application.Workbooks;
using Lajur.Infrastructure.Workbooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lajur.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/workbooks")]
public sealed class WorkbooksController(IWorkbookService workbookService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkbookInfo>>> List(CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out _))
        {
            return Unauthorized();
        }

        return Ok(await workbookService.ListAsync(userId, cancellationToken));
    }

    [HttpPost("{year:int}/create")]
    public async Task<ActionResult<WorkbookCreateResult>> Create(
        [FromRoute, Range(1900, 9999)] int year,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out _))
        {
            return Unauthorized();
        }

        var result = await workbookService.CreateAsync(
            userId,
            year,
            HttpContext.TraceIdentifier,
            cancellationToken);
        return Created($"/api/v1/workbooks/{year}/export", result);
    }

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(26_214_400)]
    public async Task<ActionResult<WorkbookImportResult>> Import(
        [FromForm] IFormFile file,
        [FromForm, Range(1900, 9999)] int? year,
        [FromForm] bool replaceExisting,
        [FromForm] bool confirmNoPendingChanges,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out var deviceId))
        {
            return Unauthorized();
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkbookImportValidationException("Only .xlsm workbook uploads are accepted.");
        }

        await using var content = file.OpenReadStream();
        return Ok(await workbookService.ImportAsync(
            userId,
            deviceId,
            HttpContext.TraceIdentifier,
            content,
            file.Length,
            year,
            replaceExisting,
            confirmNoPendingChanges,
            cancellationToken));
    }

    [HttpGet("{year:int}/export")]
    public async Task<IActionResult> Export(
        [FromRoute, Range(1900, 9999)] int year,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out _))
        {
            return Unauthorized();
        }

        var download = await workbookService.ExportAsync(userId, year, cancellationToken);
        return File(download.Content, download.ContentType, download.FileName);
    }

    [HttpGet("export-all")]
    public async Task<IActionResult> ExportAll(CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out _))
        {
            return Unauthorized();
        }

        var download = await workbookService.ExportAllAsync(userId, cancellationToken);
        return File(download.Content, download.ContentType, download.FileName);
    }

    private bool TryGetIdentity(out Guid userId, out Guid deviceId) =>
        User.TryGetSessionIds(out userId, out deviceId, out _);
}
