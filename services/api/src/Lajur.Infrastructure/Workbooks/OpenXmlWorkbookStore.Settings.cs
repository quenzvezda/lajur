using Lajur.Application.Finance;

namespace Lajur.Infrastructure.Workbooks;

public sealed partial class OpenXmlWorkbookStore
{
    public Task<BudgetSettings> ReadBudgetSettingsAsync(WorkbookKey key, CancellationToken cancellationToken = default)
        => ReadAsync(key, (_, path) => WorkbookSettingsMap.ReadBudget(path).Snapshot, cancellationToken);

    public Task<ProjectionSettings> ReadProjectionSettingsAsync(WorkbookKey key, int month, CancellationToken cancellationToken = default)
        => ReadAsync(key, (_, path) => WorkbookSettingsMap.ReadProjection(path, month).Snapshot, cancellationToken);

    public Task<SettingsOperation?> ReadSettingsOperationAsync(WorkbookKey key, Guid operationId, CancellationToken cancellationToken = default)
        => ReadAsync(key, (_, path) => WorkbookSettingsMap.ReadOperation(path, operationId), cancellationToken);

    public Task<BudgetSettings> SaveBudgetSettingsAsync(WorkbookKey key, SaveBudgetSettings command, WorkbookAuditContext audit, CancellationToken cancellationToken = default)
    {
        ValidateKey(key); ValidateAudit(audit);
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(command.Changes);
        if (command.Changes.Any(change => change is null)) throw new ArgumentException("Perubahan budget tidak boleh kosong.");
        SettingsInput.Validate(command.OperationId, command.ExpectedVersion, command.Changes.Count);
        return MutateAsync(key, (editor, timestamp) => editor.SaveBudget(key, command, audit, timestamp), cancellationToken, backupSettings: true);
    }

    public Task<ProjectionSettings> SaveProjectionSettingsAsync(WorkbookKey key, int month, SaveProjectionSettings command, WorkbookAuditContext audit, CancellationToken cancellationToken = default)
    {
        ValidateKey(key); ValidateAudit(audit);
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(command.Changes);
        if (command.Changes.Any(change => change is null)) throw new ArgumentException("Perubahan proyeksi tidak boleh kosong.");
        SettingsInput.Validate(command.OperationId, command.ExpectedVersion, command.Changes.Count);
        return MutateAsync(key, (editor, timestamp) => editor.SaveProjection(key, month, command, audit, timestamp), cancellationToken, backupSettings: true);
    }
}
