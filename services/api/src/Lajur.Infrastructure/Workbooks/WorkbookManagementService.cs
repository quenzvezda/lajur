using System.IO.Compression;
using Lajur.Application.Auth;
using Lajur.Application.Workbooks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lajur.Infrastructure.Workbooks;

public sealed class WorkbookManagementService(
    OpenXmlWorkbookStore store,
    IOptions<StorageOptions> storageOptions,
    ILogger<WorkbookManagementService> logger) : IWorkbookService
{
    private const string WorkbookContentType = "application/vnd.ms-excel.sheet.macroEnabled.12";
    private readonly string storageRoot = Path.GetFullPath(storageOptions.Value.RootPath);

    public async Task<IReadOnlyList<WorkbookInfo>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var workbooks = await store.ListWorkbooksAsync(userId, cancellationToken);
        return workbooks.Select(item => new WorkbookInfo(
            item.Year,
            item.SizeBytes,
            item.LastModifiedAtUtc)).ToArray();
    }

    public async Task<WorkbookCreateResult> CreateAsync(
        Guid userId,
        int year,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var templatePath = Path.Combine(storageRoot, "templates", "finance-template.xlsm");
        var result = await store.GenerateYearAsync(
            new WorkbookKey(userId, year),
            templatePath,
            new WorkbookAuditContext(WorkbookChangeSource.System, null, correlationId),
            cancellationToken);
        logger.LogInformation(
            "Generated workbook for user {UserId}, year {WorkbookYear}, indexed rows {IndexedCount}",
            userId,
            year,
            result.IndexedTransactionCount);
        return new WorkbookCreateResult(
            year,
            WorkbookFileName(year),
            result.IndexedTransactionCount);
    }

    public async Task<WorkbookImportResult> ImportAsync(
        Guid userId,
        Guid deviceId,
        string correlationId,
        Stream content,
        long? declaredLength,
        int? confirmedYear,
        bool replaceExisting,
        bool confirmNoPendingChanges,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!confirmNoPendingChanges)
        {
            throw new WorkbookPendingChangesNotConfirmedException();
        }

        if (declaredLength > WorkbookPackageSecurity.MaximumUploadBytes)
        {
            throw new WorkbookUploadTooLargeException(WorkbookPackageSecurity.MaximumUploadBytes);
        }

        if (confirmedYear is < 1900 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmedYear), "Confirmed year must be between 1900 and 9999.");
        }

        var stagingDirectory = Path.Combine(storageRoot, "staging", userId.ToString("N"));
        try
        {
            Directory.CreateDirectory(stagingDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ServerStorageUnavailableException("staging", exception);
        }

        var stagedPath = Path.Combine(stagingDirectory, $"{Guid.NewGuid():N}.xlsm");
        try
        {
            await CopyWithLimitAsync(content, stagedPath, cancellationToken);
            WorkbookPackageSecurity.Validate(stagedPath);
            int detectedYear;
            try
            {
                OpenXmlWorkbookStore.ThrowIfImportRowsInvalid(stagedPath, confirmedYear);
                detectedYear = new OpenXmlWorkbookReader().DetectWorkbookYear(stagedPath);
            }
            catch (InvalidDataException exception)
            {
                throw new WorkbookImportValidationException(
                    $"Struktur workbook tidak sesuai kontrak Lajur: {exception.Message}",
                    exception);
            }

            var year = confirmedYear ?? detectedYear;
            if (year != detectedYear)
            {
                throw new WorkbookImportValidationException(
                    $"Workbook berisi tahun {detectedYear}, bukan tahun {year} yang dikonfirmasi.");
            }

            var stored = await store.ImportWorkbookAsync(
                new WorkbookKey(userId, year),
                stagedPath,
                replaceExisting,
                new WorkbookAuditContext(
                    WorkbookChangeSource.ExcelImport,
                    deviceId.ToString("D"),
                    correlationId),
                cancellationToken);
            logger.LogInformation(
                "Imported workbook for user {UserId}, year {WorkbookYear}, replacing {ReplacedExisting}, indexed {IndexedCount}, updated {UpdatedCount}, tombstoned {TombstonedCount}",
                userId,
                year,
                stored.ReplacedExisting,
                stored.Migration.IndexedTransactionCount,
                stored.Migration.UpdatedTransactionCount,
                stored.Migration.TombstonedTransactionCount);
            return new WorkbookImportResult(
                year,
                WorkbookFileName(year),
                stored.Migration.IndexedTransactionCount,
                stored.Migration.UpdatedTransactionCount,
                stored.Migration.TombstonedTransactionCount,
                stored.ReplacedExisting,
                stored.BackupFileName);
        }
        finally
        {
            if (File.Exists(stagedPath))
            {
                File.Delete(stagedPath);
            }
        }
    }

    public async Task<WorkbookDownload> ExportAsync(
        Guid userId,
        int year,
        CancellationToken cancellationToken)
    {
        var content = await store.ExportWorkbookAsync(new WorkbookKey(userId, year), cancellationToken);
        return new WorkbookDownload(WorkbookFileName(year), WorkbookContentType, content);
    }

    public async Task<WorkbookDownload> ExportAllAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var workbooks = await store.ListWorkbooksAsync(userId, cancellationToken);
        if (workbooks.Count == 0)
        {
            throw new FileNotFoundException("No active workbooks were found for this account.");
        }

        await using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var workbook in workbooks.OrderBy(item => item.Year))
            {
                var bytes = await store.ExportWorkbookAsync(
                    new WorkbookKey(userId, workbook.Year),
                    cancellationToken);
                var entry = archive.CreateEntry(WorkbookFileName(workbook.Year), CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(bytes, cancellationToken);
            }
        }

        return new WorkbookDownload(
            "Keuangan_Pribadi_All_Years.zip",
            "application/zip",
            output.ToArray());
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > WorkbookPackageSecurity.MaximumUploadBytes)
            {
                throw new WorkbookUploadTooLargeException(WorkbookPackageSecurity.MaximumUploadBytes);
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await destination.FlushAsync(cancellationToken);
    }

    private static string WorkbookFileName(int year) => $"Keuangan_Pribadi_{year}.xlsm";
}
