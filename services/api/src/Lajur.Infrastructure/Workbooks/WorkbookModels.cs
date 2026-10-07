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

public sealed record WorkbookMigrationResult(
    int IndexedTransactionCount,
    int UpdatedTransactionCount,
    int TombstonedTransactionCount);

public sealed record StoredWorkbookInfo(
    int Year,
    long SizeBytes,
    DateTimeOffset LastModifiedAtUtc);

public sealed record WorkbookYearData(
    DateTimeOffset LastModifiedAtUtc,
    WorkbookMasterData Master,
    IReadOnlyList<IReadOnlyList<WorkbookTransactionRow>> Months,
    IReadOnlyList<WorkbookMonthBalanceInputs> Balances,
    IReadOnlyList<IReadOnlyList<WorkbookIncomeSourceInput>?> IncomeSources);

public sealed record StoredWorkbookImportResult(
    WorkbookMigrationResult Migration,
    bool ReplacedExisting,
    string? BackupFileName);

public class WorkbookConflictException(string message) : Exception(message);

public sealed class WorkbookTransactionIdConflictException(string message)
    : WorkbookConflictException(message);

public sealed class WorkbookRevisionConflictException(string message)
    : WorkbookConflictException(message);

public sealed class WorkbookTransactionNotFoundException(Guid transactionId)
    : Exception($"Transaction '{transactionId:D}' was not found in the workbook.");

public sealed class WorkbookAlreadyExistsException(int year)
    : WorkbookConflictException($"A workbook already exists for year {year}.");

public sealed class WorkbookReplacementNotConfirmedException(int year)
    : WorkbookConflictException($"Replacing the workbook for year {year} was not confirmed.");

public sealed class WorkbookPendingChangesNotConfirmedException()
    : WorkbookConflictException("The client did not confirm that no local changes are pending.");

public sealed class WorkbookTemplateUnavailableException(string message) : Exception(message);

public sealed class WorkbookUploadTooLargeException(long maximumBytes)
    : Exception($"The workbook upload exceeds the {maximumBytes} byte limit.");

public sealed class WorkbookImportValidationException(
    string message,
    Exception? innerException = null,
    IReadOnlyList<WorkbookImportIssue>? issues = null)
    : Exception(message, innerException)
{
    public WorkbookImportValidationException(IReadOnlyList<WorkbookImportIssue> issues)
        : this(DescribeIssues(issues), null, issues)
    {
    }

    /// <summary>Row-level problems found by the import preflight; empty for structural failures.</summary>
    public IReadOnlyList<WorkbookImportIssue> Issues { get; } = issues ?? [];

    public const int MaximumListedIssues = 25;

    private static string DescribeIssues(IReadOnlyList<WorkbookImportIssue> issues)
    {
        var rows = issues.Select(issue => (issue.Sheet, issue.Row)).Distinct().Count();
        var lines = issues.Take(MaximumListedIssues)
            .Select(issue => $"• {issue.Sheet} baris {issue.Row} (kolom {issue.Column}): {issue.Message}");
        var more = issues.Count > MaximumListedIssues
            ? $"\n…dan {issues.Count - MaximumListedIssues} masalah lain."
            : string.Empty;
        return $"Workbook belum bisa diimpor: {rows} baris perlu diperbaiki di Excel, lalu impor ulang.\n"
               + string.Join("\n", lines) + more;
    }
}
