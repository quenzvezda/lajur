using System.Collections.Concurrent;

namespace Lajur.Infrastructure.Workbooks;

public sealed class OpenXmlWorkbookStore
{
    private static readonly ConcurrentDictionary<WorkbookKey, SemaphoreSlim> Locks = new();
    private readonly Func<WorkbookKey, string> workbookPathResolver;
    private readonly Func<WorkbookKey, string> backupDirectoryResolver;
    private readonly TimeProvider timeProvider;

    public OpenXmlWorkbookStore(string storageRoot, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRoot);
        var normalizedRoot = Path.GetFullPath(storageRoot);
        workbookPathResolver = key => Path.Combine(
            normalizedRoot,
            "users",
            key.UserId.ToString("D"),
            "workbooks",
            $"Keuangan_Pribadi_{key.Year}.xlsm");
        backupDirectoryResolver = key => Path.Combine(
            normalizedRoot,
            "backups",
            key.UserId.ToString("D"),
            key.Year.ToString());
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public OpenXmlWorkbookStore(
        Func<WorkbookKey, string> workbookPathResolver,
        TimeProvider? timeProvider = null,
        Func<WorkbookKey, string>? backupDirectoryResolver = null)
    {
        ArgumentNullException.ThrowIfNull(workbookPathResolver);
        this.workbookPathResolver = workbookPathResolver;
        this.backupDirectoryResolver = backupDirectoryResolver ?? (key => Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(ResolveWorkbookPathWithoutInstance(workbookPathResolver, key))!)!,
            "backups",
            key.Year.ToString()));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<StoredWorkbookInfo>> ListWorkbooksAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(userId);
        cancellationToken.ThrowIfCancellationRequested();
        var samplePath = ResolveWorkbookPath(new WorkbookKey(userId, 2000));
        var directory = Path.GetDirectoryName(samplePath)!;
        if (!Directory.Exists(directory))
        {
            return Task.FromResult<IReadOnlyList<StoredWorkbookInfo>>([]);
        }

        var workbooks = Directory.EnumerateFiles(directory, "Keuangan_Pribadi_*.xlsm", SearchOption.TopDirectoryOnly)
            .Select(path => (Path: path, Year: ParseWorkbookYear(Path.GetFileName(path))))
            .Where(item => item.Year is not null)
            .Select(item =>
            {
                var info = new FileInfo(item.Path);
                return new StoredWorkbookInfo(
                    item.Year!.Value,
                    info.Length,
                    new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
            })
            .OrderBy(item => item.Year)
            .ToArray();
        return Task.FromResult<IReadOnlyList<StoredWorkbookInfo>>(workbooks);
    }

    public async Task<WorkbookMigrationResult> GenerateYearAsync(
        WorkbookKey key,
        string templatePath,
        WorkbookAuditContext audit,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ValidateAudit(audit);
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        if (!File.Exists(templatePath))
        {
            throw new WorkbookTemplateUnavailableException("The approved workbook template is not installed.");
        }

        WorkbookPackageSecurity.Validate(templatePath);
        var gate = Locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var activePath = ResolveWorkbookPath(key);
            if (File.Exists(activePath))
            {
                throw new WorkbookAlreadyExistsException(key.Year);
            }

            var directory = Path.GetDirectoryName(activePath)!;
            Directory.CreateDirectory(directory);
            var tempPath = Path.Combine(directory, $".lajur-{Guid.NewGuid():N}.tmp.xlsm");
            File.Copy(templatePath, tempPath, overwrite: false);
            try
            {
                var reader = new OpenXmlWorkbookReader();
                var master = reader.ReadMasterData(tempPath, key.Year);
                var result = new OpenXmlWorkbookEditor(tempPath, master)
                    .PrepareGeneratedWorkbook(key, audit, timeProvider.GetUtcNow());
                ValidateWorkbook(tempPath, key.Year);
                File.Move(tempPath, activePath, overwrite: false);
                return result;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<StoredWorkbookImportResult> ImportWorkbookAsync(
        WorkbookKey key,
        string stagedPath,
        bool replaceExisting,
        WorkbookAuditContext audit,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ValidateAudit(audit);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedPath);
        WorkbookPackageSecurity.Validate(stagedPath);

        try
        {
            var detectedYear = new OpenXmlWorkbookReader().DetectWorkbookYear(stagedPath);
            if (detectedYear != key.Year)
            {
                throw new WorkbookImportValidationException(
                    $"Workbook year {detectedYear} does not match confirmed year {key.Year}.");
            }
        }
        catch (WorkbookImportValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
        {
            throw new WorkbookImportValidationException("The workbook year could not be validated.", exception);
        }

        var gate = Locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var activePath = ResolveWorkbookPath(key);
            var replacing = File.Exists(activePath);
            if (replacing && !replaceExisting)
            {
                throw new WorkbookReplacementNotConfirmedException(key.Year);
            }

            var directory = Path.GetDirectoryName(activePath)!;
            Directory.CreateDirectory(directory);
            var tempPath = Path.Combine(directory, $".lajur-{Guid.NewGuid():N}.tmp.xlsm");
            File.Copy(stagedPath, tempPath, overwrite: false);
            try
            {
                var reader = new OpenXmlWorkbookReader();
                var master = reader.ReadMasterData(tempPath, key.Year);
                var migration = new OpenXmlWorkbookEditor(tempPath, master)
                    .MigrateImportedWorkbook(key, audit, timeProvider.GetUtcNow());
                ValidateWorkbook(tempPath, key.Year);

                string? backupFileName = null;
                if (replacing)
                {
                    var backupDirectory = Path.GetFullPath(backupDirectoryResolver(key));
                    Directory.CreateDirectory(backupDirectory);
                    backupFileName = $"{timeProvider.GetUtcNow():yyyyMMddTHHmmssfffZ}-pre-import-{Guid.NewGuid():N}.xlsm";
                    File.Copy(activePath, Path.Combine(backupDirectory, backupFileName), overwrite: false);
                }

                File.Move(tempPath, activePath, overwrite: true);
                return new StoredWorkbookImportResult(migration, replacing, backupFileName);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        catch (WorkbookConflictException)
        {
            throw;
        }
        catch (WorkbookImportValidationException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            throw new WorkbookImportValidationException("The workbook does not satisfy the Lajur contract.", exception);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<byte[]> ExportWorkbookAsync(
        WorkbookKey key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        var gate = Locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = ResolveWorkbookPath(key);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The active workbook was not found.", path);
            }

            return await File.ReadAllBytesAsync(path, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<WorkbookMasterData> GetMasterDataAsync(
        WorkbookKey key,
        CancellationToken cancellationToken = default) =>
        ReadAsync(key, (reader, path) => reader.ReadMasterData(path, key.Year), cancellationToken);

    public Task<IReadOnlyList<WorkbookTransaction>> ListTransactionsAsync(
        WorkbookKey key,
        int month,
        CancellationToken cancellationToken = default) =>
        ReadAsync(
            key,
            (reader, path) => reader.ReadApplicationTransactions(path, key.Year, month),
            cancellationToken);

    public Task<IReadOnlyList<WorkbookTransactionRow>> ListCanonicalRowsAsync(
        WorkbookKey key,
        int month,
        CancellationToken cancellationToken = default) =>
        ReadAsync(key, (reader, path) => reader.ReadMonthlyTransactions(path, month), cancellationToken);

    public Task<WorkbookTransaction?> GetTransactionAsync(
        WorkbookKey key,
        Guid transactionId,
        CancellationToken cancellationToken = default) =>
        ReadAsync(
            key,
            (reader, path) => reader.ReadApplicationTransaction(path, transactionId),
            cancellationToken);

    public async Task<WorkbookTransaction> CreateTransactionAsync(
        WorkbookKey key,
        CreateWorkbookTransactionCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(command);
        ValidateIdentity(command.TransactionId);
        ValidateAudit(command.Audit);

        return await MutateAsync(
            key,
            (editor, timestamp) => editor.CreateTransaction(key, command, timestamp),
            cancellationToken);
    }

    public async Task<WorkbookTransaction> UpdateTransactionAsync(
        WorkbookKey key,
        UpdateWorkbookTransactionCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(command);
        ValidateIdentity(command.TransactionId);
        ValidateAudit(command.Audit);

        if (command.ExpectedRevision < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                command.ExpectedRevision,
                "Expected revision must be positive.");
        }

        return await MutateAsync(
            key,
            (editor, timestamp) => editor.UpdateTransaction(key, command, timestamp),
            cancellationToken);
    }

    public async Task DeleteTransactionAsync(
        WorkbookKey key,
        DeleteWorkbookTransactionCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(command);
        ValidateIdentity(command.TransactionId);
        ValidateAudit(command.Audit);

        if (command.ExpectedRevision < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                command.ExpectedRevision,
                "Expected revision must be positive.");
        }

        _ = await MutateAsync(
            key,
            (editor, timestamp) =>
            {
                editor.DeleteTransaction(key, command, timestamp);
                return true;
            },
            cancellationToken);
    }

    private async Task<TResult> MutateAsync<TResult>(
        WorkbookKey key,
        Func<OpenXmlWorkbookEditor, DateTimeOffset, TResult> mutation,
        CancellationToken cancellationToken)
    {
        var gate = Locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            var activePath = ResolveWorkbookPath(key);

            if (!File.Exists(activePath))
            {
                throw new FileNotFoundException("The active workbook was not found.", activePath);
            }

            var directory = Path.GetDirectoryName(activePath)!;
            var tempPath = Path.Combine(directory, $".lajur-{Guid.NewGuid():N}.tmp.xlsm");
            File.Copy(activePath, tempPath, overwrite: false);

            try
            {
                var reader = new OpenXmlWorkbookReader();
                var master = reader.ReadMasterData(tempPath, key.Year);
                var editor = new OpenXmlWorkbookEditor(tempPath, master);
                var result = mutation(editor, timeProvider.GetUtcNow());

                ValidateWorkbook(tempPath, key.Year);
                File.Move(tempPath, activePath, overwrite: true);
                return result;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<TResult> ReadAsync<TResult>(
        WorkbookKey key,
        Func<OpenXmlWorkbookReader, string, TResult> read,
        CancellationToken cancellationToken)
    {
        ValidateKey(key);
        var gate = Locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            var activePath = ResolveWorkbookPath(key);
            if (!File.Exists(activePath))
            {
                throw new FileNotFoundException("The active workbook was not found.", activePath);
            }

            return read(new OpenXmlWorkbookReader(), activePath);
        }
        finally
        {
            gate.Release();
        }
    }

    private string ResolveWorkbookPath(WorkbookKey key) => Path.GetFullPath(workbookPathResolver(key));

    private static string ResolveWorkbookPathWithoutInstance(
        Func<WorkbookKey, string> resolver,
        WorkbookKey key) => Path.GetFullPath(resolver(key));

    private static void ValidateWorkbook(string workbookPath, int year)
    {
        var reader = new OpenXmlWorkbookReader();
        _ = reader.ReadMasterData(workbookPath, year);

        for (var month = 1; month <= 12; month++)
        {
            var transactions = reader.ReadMonthlyTransactions(workbookPath, month);
            if (transactions.Any(transaction => transaction.Date.Year != year || transaction.Date.Month != month))
            {
                throw new InvalidDataException(
                    $"Monthly table {month} contains a transaction outside workbook year/month {year}-{month:00}.");
            }
        }

        OpenXmlWorkbookEditor.ValidateSystemSheets(workbookPath);
        if (reader.DetectWorkbookYear(workbookPath) != year)
        {
            throw new InvalidDataException($"Workbook content does not consistently identify year {year}.");
        }
    }

    private static int? ParseWorkbookYear(string fileName)
    {
        const string prefix = "Keuangan_Pribadi_";
        const string suffix = ".xlsm";
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal)
            || !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var value = fileName[prefix.Length..^suffix.Length];
        return int.TryParse(value, out var year) && year is >= 1900 and <= 9999 ? year : null;
    }

    private static void ValidateKey(WorkbookKey key)
    {
        ValidateIdentity(key.UserId);

        if (key.Year is < 1900 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(key), key.Year, "Workbook year must be between 1900 and 9999.");
        }
    }

    private static void ValidateIdentity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Identifiers must not be empty.");
        }
    }

    private static void ValidateAudit(WorkbookAuditContext audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentException.ThrowIfNullOrWhiteSpace(audit.CorrelationId);
    }
}
