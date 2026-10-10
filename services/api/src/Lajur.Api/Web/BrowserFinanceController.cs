using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Lajur.Application.Finance;
using Lajur.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lajur.Api.Web;

/// <summary>Cookie-only browser commands. Android's bearer contract remains separate.</summary>
[ApiController]
[Authorize(Policy = WebViewerSetup.Policy)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("web/books/{year:int}")]
public sealed class BrowserFinanceController(IFinanceService finance, IFinanceReportService reports, ReportClock clock) : ControllerBase
{
    [HttpGet("report")]
    public async Task<IActionResult> Report([Range(1900, 9999)] int year, CancellationToken cancellationToken)
    {
        if (!User.TryGetWebUserId(out var userId)) return Unauthorized();
        var report = await reports.GetYearReportAsync(userId, year, clock.Today(), cancellationToken);
        return report is null ? NotFound() : Ok(report);
    }

    [HttpGet("transactions/{transactionId:guid}")]
    public async Task<IActionResult> Transaction([Range(1900, 9999)] int year, Guid transactionId, CancellationToken cancellationToken)
    {
        if (!User.TryGetWebUserId(out var userId)) return Unauthorized();
        var row = await finance.GetTransactionAsync(userId, year, transactionId, cancellationToken);
        return row is null ? NotFound() : Ok(BrowserTransaction.From(row));
    }

    [HttpPost("transactions")]
    public async Task<IActionResult> Create([Range(1900, 9999)] int year, BrowserCreateTransaction request, CancellationToken cancellationToken)
    {
        if (!User.TryGetWebUserId(out var userId)) return Unauthorized();
        var draft = request.Input.ToDraft(year);
        var row = await finance.CreateTransactionAsync(userId, FinanceWriteActor.Web, HttpContext.TraceIdentifier,
            request.TransactionId, draft, cancellationToken);
        return Ok(BrowserTransaction.From(row));
    }

    [HttpPut("transactions/{transactionId:guid}")]
    public async Task<IActionResult> Update([Range(1900, 9999)] int year, Guid transactionId,
        BrowserUpdateTransaction request, CancellationToken cancellationToken)
    {
        if (!User.TryGetWebUserId(out var userId)) return Unauthorized();
        var row = await finance.UpdateTransactionAsync(userId, FinanceWriteActor.Web, HttpContext.TraceIdentifier,
            year, transactionId, request.ExpectedRevision, request.Input.ToDraft(year), cancellationToken);
        return Ok(BrowserTransaction.From(row));
    }

    [HttpDelete("transactions/{transactionId:guid}")]
    public async Task<IActionResult> Delete([Range(1900, 9999)] int year, Guid transactionId,
        [FromQuery, Range(1, int.MaxValue)] int expectedRevision, CancellationToken cancellationToken)
    {
        if (!User.TryGetWebUserId(out var userId)) return Unauthorized();
        await finance.DeleteTransactionAsync(userId, FinanceWriteActor.Web, HttpContext.TraceIdentifier,
            year, transactionId, expectedRevision, cancellationToken);
        return NoContent();
    }
}

public sealed record BrowserTransactionInput(
    DateOnly Date,
    [param: Required, StringLength(100)] string Type,
    [param: StringLength(100)] string? PaymentMethod,
    [param: Required, StringLength(100)] string Category,
    [param: Required, RegularExpression("^[0-9]{1,19}$")] string AmountIdr,
    [param: StringLength(500)] string? Description)
{
    public FinanceTransactionDraft ToDraft(int year)
    {
        if (Date.Year != year) throw new ArgumentException("Tanggal harus berada dalam tahun buku yang sedang dibuka.");
        if (!long.TryParse(AmountIdr, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            throw new ArgumentException("Nominal harus berupa rupiah bulat positif dalam batas yang didukung.");
        return new(Date, Type, PaymentMethod?.Trim() ?? string.Empty, Category, amount, Description);
    }
}

public sealed record BrowserCreateTransaction(Guid TransactionId, [param: Required] BrowserTransactionInput Input);
public sealed record BrowserUpdateTransaction([param: Range(1, int.MaxValue)] int ExpectedRevision,
    [param: Required] BrowserTransactionInput Input);

public sealed record BrowserTransaction(Guid TransactionId, int Revision, DateOnly Date, string Type,
    string PaymentMethod, string Category, string AmountIdr, string? Description)
{
    public static BrowserTransaction From(FinanceTransaction row) => new(row.TransactionId, row.Revision, row.Date,
        row.Type, row.PaymentMethod, row.Category, row.AmountIdr.ToString(CultureInfo.InvariantCulture), row.Description);
}
