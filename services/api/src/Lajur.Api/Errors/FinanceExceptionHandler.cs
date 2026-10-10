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

        logger.Log(
            error.Code == StorageUnavailableCode ? LogLevel.Error : LogLevel.Warning,
            exception,
            "Finance request failed with code {ErrorCode} and status {StatusCode}",
            error.Code,
            error.Status);
        httpContext.Response.StatusCode = error.Status;
        var problem = new ProblemDetails
        {
            Status = error.Status,
            Title = error.Title,
            Detail = error.Detail,
            Extensions =
            {
                ["code"] = error.Code,
                ["traceId"] = httpContext.TraceIdentifier
            }
        };
        if (exception is WorkbookImportValidationException { Issues.Count: > 0 } invalid)
        {
            problem.Extensions["issues"] = invalid.Issues
                .Select(issue => new { issue.Sheet, issue.Row, issue.Column, issue.Cell, issue.Message })
                .ToArray();
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    public const string StorageUnavailableCode = "server_storage_unavailable";

    private static FinanceError? Map(Exception exception) => exception switch
    {
        _ when StorageFault.IsServerStorageFault(exception) => new(
            StatusCodes.Status503ServiceUnavailable,
            "Server storage unavailable",
            StorageUnavailableCode,
            "Penyimpanan server tidak dapat ditulis. Ini masalah konfigurasi server, bukan workbook Anda; " +
            "mencoba ulang tidak akan membantu. Hubungi admin server."),
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
        WorkbookSettingsConflictException => new(
            StatusCodes.Status409Conflict,
            "Workbook settings conflict",
            "settings_revision_conflict",
            exception.Message),
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
            "Workbook sedang dipakai proses lain di server. Coba lagi sebentar lagi."),
        _ => null
    };

    private sealed record FinanceError(int Status, string Title, string Code, string Detail);
}
