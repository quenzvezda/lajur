namespace Lajur.Infrastructure.Workbooks;

public readonly record struct WorkbookKey(Guid UserId, int Year);

public sealed record WorkbookTransactionDraft(
    DateOnly Date,
    string Type,
    string PaymentMethod,
    string Category,
    long AmountIdr,
    string? Description);

public sealed record WorkbookTransaction(
    Guid TransactionId,
    int Revision,
    DateOnly Date,
    string Type,
    string PaymentMethod,
    string Category,
    long AmountIdr,
    string? Description);

public enum WorkbookChangeSource
{
    Android,
    ExcelImport,
    System
}

public sealed record WorkbookAuditContext(
    WorkbookChangeSource Source,
    string? DeviceId,
    string CorrelationId);

public sealed record CreateWorkbookTransactionCommand(
    Guid TransactionId,
    WorkbookTransactionDraft Transaction,
    WorkbookAuditContext Audit);

public sealed record UpdateWorkbookTransactionCommand(
    Guid TransactionId,
    int ExpectedRevision,
    WorkbookTransactionDraft Transaction,
    WorkbookAuditContext Audit);

public sealed record DeleteWorkbookTransactionCommand(
    Guid TransactionId,
    int ExpectedRevision,
    WorkbookAuditContext Audit);

public sealed class WorkbookConflictException(string message) : Exception(message);

public sealed class WorkbookTransactionNotFoundException(Guid transactionId)
    : Exception($"Transaction '{transactionId:D}' was not found in the workbook.");
