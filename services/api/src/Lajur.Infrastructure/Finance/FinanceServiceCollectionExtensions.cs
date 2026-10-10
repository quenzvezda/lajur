using Lajur.Application.Auth;
using Lajur.Application.Finance;
using Lajur.Application.Reports;
using Lajur.Application.Workbooks;
using Lajur.Infrastructure.Workbooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lajur.Infrastructure.Finance;

public static class FinanceServiceCollectionExtensions
{
    public static IServiceCollection AddLajurFinance(this IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var paths = provider.GetRequiredService<IUserStoragePathResolver>();
            var timeProvider = provider.GetRequiredService<TimeProvider>();
            var storage = provider.GetRequiredService<IOptions<StorageOptions>>().Value;
            return new OpenXmlWorkbookStore(
                key => Path.Combine(
                    paths.GetUserRoot(key.UserId),
                    "workbooks",
                    $"Keuangan_Pribadi_{key.Year}.xlsm"),
                timeProvider,
                key => Path.Combine(
                    Path.GetFullPath(storage.RootPath),
                    "backups",
                    key.UserId.ToString("N"),
                    key.Year.ToString()));
        });
        services.AddScoped<IFinanceService, WorkbookFinanceService>();
        services.AddScoped<IWorkbookSettingsService, WorkbookSettingsService>();
        services.AddScoped<IWorkbookService, WorkbookManagementService>();
        services.AddScoped<IFinanceReportService, WorkbookFinanceReportService>();
        return services;
    }
}
