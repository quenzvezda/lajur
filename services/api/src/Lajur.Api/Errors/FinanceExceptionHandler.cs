using Lajur.Infrastructure.Workbooks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Lajur.Api.Errors;

public sealed class FinanceExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<FinanceExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var error = Map(exception);
        if (error is null)
        {
            return false;
        }

        logger.LogWarning(
            exception,
            "Finance request failed with code {ErrorCode} and status {StatusCode}",
            error.Code,
            error.Status);
        httpContext.Response.StatusCode = error.Status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = error.Status,
                Title = error.Title,
                Detail = error.Detail,
                Extensions =
                {
                    ["code"] = error.Code,
                    ["traceId"] = httpContext.TraceIdentifier
                }
            },
            Exception = exception
        });
    }

    private static FinanceError? Map(Exception exception) => exception switch
    {
        WorkbookTransactionIdConflictException => new(
            StatusCodes.Status409Conflict,
            "Transaction ID conflict",
            "transaction_id_conflict",
            "The transaction ID already exists with different content."),
        WorkbookRevisionConflictException => new(
            StatusCodes.Status409Conflict,
            "Transaction revision conflict",
            "transaction_revision_conflict",
            "The transaction has changed since the supplied revision."),
        WorkbookAlreadyExistsException => new(
            StatusCodes.Status409Conflict,
            "Workbook already exists",
            "workbook_already_exists",
            "A workbook already exists for the requested year."),
        WorkbookReplacementNotConfirmedException => new(
            StatusCodes.Status409Conflict,
            "Workbook replacement not confirmed",
            "workbook_replace_not_confirmed",
            "Explicit replacement confirmation is required for an existing year."),
        WorkbookPendingChangesNotConfirmedException => new(
            StatusCodes.Status409Conflict,
            "Pending changes not confirmed",
            "pending_changes_confirmation_required",
            "Confirm that the client has no unsynchronized local changes before importing."),
        WorkbookTransactionNotFoundException => new(
            StatusCodes.Status404NotFound,
            "Transaction not found",
            "transaction_not_found",
            "The requested transaction does not exist in this workbook."),
        FileNotFoundException => new(
            StatusCodes.Status404NotFound,
            "Workbook not found",
            "workbook_not_found",
            "No active workbook exists for the requested year."),
        WorkbookUploadTooLargeException => new(
            StatusCodes.Status413PayloadTooLarge,
            "Workbook upload too large",
            "workbook_upload_too_large",
            "The uploaded workbook exceeds the configured size limit."),
        WorkbookImportValidationException => new(
            StatusCodes.Status422UnprocessableEntity,
            "Workbook import validation failed",
            "workbook_import_invalid",
            exception.Message),
        WorkbookTemplateUnavailableException => new(
            StatusCodes.Status503ServiceUnavailable,
            "Workbook template unavailable",
            "workbook_template_unavailable",
            "The approved workbook template is not installed on the server."),
        InvalidDataException => new(
            StatusCodes.Status422UnprocessableEntity,
            "Workbook validation failed",
            "workbook_validation_failed",
            "The active workbook does not satisfy the required workbook contract."),
        ArgumentException => new(
            StatusCodes.Status422UnprocessableEntity,
            "Transaction validation failed",
            "transaction_validation_failed",
            exception.Message),
        IOException => new(
            StatusCodes.Status503ServiceUnavailable,
            "Workbook temporarily unavailable",
            "workbook_unavailable",
            "The workbook could not be accessed safely. Retry later."),
        _ => null
    };

    private sealed record FinanceError(int Status, string Title, string Code, string Detail);
}
