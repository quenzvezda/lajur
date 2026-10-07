using Lajur.Application.Reports;
using Lajur.Infrastructure.Workbooks;

namespace Lajur.Infrastructure.Finance;

public sealed class WorkbookFinanceReportService(OpenXmlWorkbookStore workbookStore) : IFinanceReportService
{
    public async Task<YearReport?> GetYearReportAsync(
        Guid userId,
        int year,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        WorkbookYearData data;
        try
        {
            data = await workbookStore.ReadYearAsync(new WorkbookKey(userId, year), cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        var snapshot = new WorkbookYearSnapshot(
            year,
            data.LastModifiedAtUtc,
            data.Master.TransactionTypes,
            data.Master.PaymentMethods,
            data.Master.Categories
                .Select(category => new ReportCategory(category.Name, category.Kind, category.MonthlyBudgetIdr))
                .ToArray(),
            data.Months
                .Select(rows => (IReadOnlyList<ReportRow>)rows
                    .Select(row => new ReportRow(
                        row.Date,
                        row.Type,
                        row.PaymentMethod,
                        row.Category,
                        row.AmountIdr,
                        row.Description))
                    .ToArray())
                .ToArray(),
            data.Balances
                .Select(balance => new MonthBalanceInputs(balance.OpeningBalanceIdr, balance.ActualBalanceIdr))
                .ToArray(),
            data.IncomeSources
                .Select(sources => sources?
                    .Select(source => new IncomeSourceInput(
                        source.Label,
                        source.PayoutDay,
                        source.EstimateIdr,
                        source.EstimateRecognized,
                        source.IncomeCategory,
                        source.PaidThresholdIdr))
                    .ToArray())
                .ToArray());
        return FinanceReportCalculator.Calculate(snapshot, today);
    }

    public async Task<IReadOnlyList<int>> ListYearsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var workbooks = await workbookStore.ListWorkbooksAsync(userId, cancellationToken);
        return workbooks.Select(workbook => workbook.Year).ToArray();
    }
}
