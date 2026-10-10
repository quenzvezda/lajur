using Lajur.Application.Finance;
using Lajur.Infrastructure.Workbooks;

namespace Lajur.Infrastructure.Finance;

public sealed class WorkbookSettingsService(OpenXmlWorkbookStore store) : IWorkbookSettingsService
{
    public Task<BudgetSettings> GetBudgetAsync(Guid userId, int year, CancellationToken cancellationToken = default)
        => store.ReadBudgetSettingsAsync(new(userId, year), cancellationToken);
    public Task<ProjectionSettings> GetProjectionAsync(Guid userId, int year, int month, CancellationToken cancellationToken = default)
        => store.ReadProjectionSettingsAsync(new(userId, year), month, cancellationToken);
    public Task<SettingsOperation?> GetOperationAsync(Guid userId, int year, Guid operationId, CancellationToken cancellationToken = default)
        => store.ReadSettingsOperationAsync(new(userId, year), operationId, cancellationToken);
    public Task<BudgetSettings> SaveBudgetAsync(Guid userId, int year, SaveBudgetSettings command, string correlationId, CancellationToken cancellationToken = default)
        => store.SaveBudgetSettingsAsync(new(userId, year), command, new(WorkbookChangeSource.Web, null, correlationId), cancellationToken);
    public Task<ProjectionSettings> SaveProjectionAsync(Guid userId, int year, int month, SaveProjectionSettings command, string correlationId, CancellationToken cancellationToken = default)
        => store.SaveProjectionSettingsAsync(new(userId, year), month, command, new(WorkbookChangeSource.Web, null, correlationId), cancellationToken);
}
