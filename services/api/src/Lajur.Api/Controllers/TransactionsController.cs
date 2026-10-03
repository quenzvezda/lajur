using System.ComponentModel.DataAnnotations;
using Lajur.Api.Auth;
using Lajur.Application.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lajur.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/transactions")]
public sealed class TransactionsController(IFinanceService financeService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FinanceTransaction>>> List(
        [FromQuery, Range(1900, 9999)] int year,
        [FromQuery, Range(1, 12)] int month,
        [FromQuery, StringLength(200)] string? query,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out _))
        {
            return Unauthorized();
        }

        return Ok(await financeService.ListTransactionsAsync(
            userId,
            year,
            month,
            query,
            cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<FinanceTransaction>> Create(
        CreateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out var deviceId))
        {
            return Unauthorized();
        }

        var created = await financeService.CreateTransactionAsync(
            userId,
            deviceId,
            HttpContext.TraceIdentifier,
            request.TransactionId,
            request.ToDraft(),
            cancellationToken);
        return Created($"/api/v1/transactions/{created.TransactionId:D}?year={created.Date.Year}", created);
    }

    [HttpPut("{transactionId:guid}")]
    public async Task<ActionResult<FinanceTransaction>> Update(
        Guid transactionId,
        [FromQuery, Range(1900, 9999)] int year,
        UpdateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out var deviceId))
        {
            return Unauthorized();
        }

        return Ok(await financeService.UpdateTransactionAsync(
            userId,
            deviceId,
            HttpContext.TraceIdentifier,
            year,
            transactionId,
            request.ExpectedRevision,
            request.ToDraft(),
            cancellationToken));
    }

    [HttpDelete("{transactionId:guid}")]
    public async Task<IActionResult> Delete(
        Guid transactionId,
        [FromQuery, Range(1900, 9999)] int year,
        [FromQuery, Range(1, int.MaxValue)] int expectedRevision,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out var deviceId))
        {
            return Unauthorized();
        }

        await financeService.DeleteTransactionAsync(
            userId,
            deviceId,
            HttpContext.TraceIdentifier,
            year,
            transactionId,
            expectedRevision,
            cancellationToken);
        return NoContent();
    }

    private bool TryGetIdentity(out Guid userId, out Guid deviceId) =>
        User.TryGetSessionIds(out userId, out deviceId, out _);
}

public sealed record CreateTransactionRequest(
    Guid TransactionId,
    DateOnly Date,
    [param: Required, StringLength(100)] string Type,
    [param: StringLength(100)] string? PaymentMethod,
    [param: Required, StringLength(100)] string Category,
    [param: Range(1, long.MaxValue)] long AmountIdr,
    [param: StringLength(500)] string? Description)
{
    public FinanceTransactionDraft ToDraft() =>
        new(Date, Type, PaymentMethod?.Trim() ?? string.Empty, Category, AmountIdr, Description);
}

public sealed record UpdateTransactionRequest(
    [param: Range(1, int.MaxValue)] int ExpectedRevision,
    DateOnly Date,
    [param: Required, StringLength(100)] string Type,
    [param: StringLength(100)] string? PaymentMethod,
    [param: Required, StringLength(100)] string Category,
    [param: Range(1, long.MaxValue)] long AmountIdr,
    [param: StringLength(500)] string? Description)
{
    public FinanceTransactionDraft ToDraft() =>
        new(Date, Type, PaymentMethod?.Trim() ?? string.Empty, Category, AmountIdr, Description);
}
