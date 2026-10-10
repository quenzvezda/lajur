using Lajur.Application.Finance;

namespace Lajur.Infrastructure.Workbooks;

public sealed partial class OpenXmlWorkbookStore
{
    public Task<MasterManagement> ReadMasterManagementAsync(WorkbookKey key, CancellationToken cancellationToken = default)
        => ReadAsync(key, (_, path) => WorkbookMasterMap.Read(path, key.Year).Snapshot, cancellationToken);
    public Task<MasterManagement> ChangeMasterAsync(WorkbookKey key, ChangeWorkbookMaster command, WorkbookAuditContext audit, CancellationToken cancellationToken = default)
    {
        ValidateKey(key); ValidateAudit(audit); ArgumentNullException.ThrowIfNull(command);
        SettingsInput.Validate(command.OperationId, command.ExpectedVersion, 1);
        if (command.Entity is not "category" and not "payment" || command.Action is not "create" and not "update" and not "delete")
            throw new ArgumentException("Operasi Master tidak didukung.");
        return MutateAsync(key, (editor, timestamp) => editor.ChangeMaster(key, command, audit, timestamp), cancellationToken, backupSettings: true);
    }
}
