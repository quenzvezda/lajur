using Lajur.Application.Auth;
using Lajur.Application.Finance;
using Lajur.Infrastructure.Workbooks;
using Microsoft.Extensions.DependencyInjection;

namespace Lajur.Infrastructure.Finance;

public static class FinanceServiceCollectionExtensions
{
    public static IServiceCollection AddLajurFinance(this IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var paths = provider.GetRequiredService<IUserStoragePathResolver>();
            var timeProvider = provider.GetRequiredService<TimeProvider>();
            return new OpenXmlWorkbookStore(
                key => Path.Combine(
                    paths.GetUserRoot(key.UserId),
                    "workbooks",
                    $"Keuangan_Pribadi_{key.Year}.xlsm"),
                timeProvider);
        });
        services.AddScoped<IFinanceService, WorkbookFinanceService>();
        return services;
    }
}
