namespace Lajur.Application.Workbooks;

public sealed record WorkbookInfo(
    int Year,
    long SizeBytes,
    DateTimeOffset LastModifiedAtUtc);

public sealed record WorkbookCreateResult(
    int Year,
    string FileName,
    int IndexedTransactionCount);

public sealed record WorkbookImportResult(
    int Year,
    string FileName,
    int IndexedTransactionCount,
    int UpdatedTransactionCount,
    int TombstonedTransactionCount,
    bool ReplacedExisting,
    string? BackupFileName);

public sealed record WorkbookDownload(
    string FileName,
    string ContentType,
    byte[] Content);

public interface IWorkbookService
{
    Task<IReadOnlyList<WorkbookInfo>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task<WorkbookCreateResult> CreateAsync(
        Guid userId,
        int year,
        string correlationId,
        CancellationToken cancellationToken);

    Task<WorkbookImportResult> ImportAsync(
        Guid userId,
        Guid deviceId,
        string correlationId,
        Stream content,
        long? declaredLength,
        int? confirmedYear,
        bool replaceExisting,
        bool confirmNoPendingChanges,
        CancellationToken cancellationToken);

    Task<WorkbookDownload> ExportAsync(Guid userId, int year, CancellationToken cancellationToken);

    Task<WorkbookDownload> ExportAllAsync(Guid userId, CancellationToken cancellationToken);
}
