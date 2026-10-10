using Lajur.Application.Finance;
using Lajur.Infrastructure.Workbooks;

namespace Lajur.Infrastructure.Finance;

public sealed class WorkbookMasterService(OpenXmlWorkbookStore store) : IWorkbookMasterService
{
    public Task<MasterManagement> GetAsync(Guid userId, int year, CancellationToken cancellationToken = default)
        => store.ReadMasterManagementAsync(new(userId, year), cancellationToken);
    public Task<MasterManagement> ChangeAsync(Guid userId, int year, ChangeWorkbookMaster command, string correlationId, CancellationToken cancellationToken = default)
        => store.ChangeMasterAsync(new(userId, year), command, new(WorkbookChangeSource.Web, null, correlationId), cancellationToken);
}
