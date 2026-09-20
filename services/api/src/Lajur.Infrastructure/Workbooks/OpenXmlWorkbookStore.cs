using System.Collections.Concurrent;

namespace Lajur.Infrastructure.Workbooks;

public sealed class OpenXmlWorkbookStore
{
    private static readonly ConcurrentDictionary<WorkbookKey, SemaphoreSlim> Locks = new();
    private readonly Func<WorkbookKey, string> workbookPathResolver;
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
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public OpenXmlWorkbookStore(
        Func<WorkbookKey, string> workbookPathResolver,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(workbookPathResolver);
        this.workbookPathResolver = workbookPathResolver;
        this.timeProvider = timeProvider ?? TimeProvider.System;
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

    private static void ValidateWorkbook(string workbookPath, int year)
    {
        var reader = new OpenXmlWorkbookReader();
        _ = reader.ReadMasterData(workbookPath, year);

        for (var month = 1; month <= 12; month++)
        {
            _ = reader.ReadMonthlyTransactions(workbookPath, month);
        }

        OpenXmlWorkbookEditor.ValidateSystemSheets(workbookPath);
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
