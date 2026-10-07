namespace Lajur.Application.Reports;

/// <summary>One canonical A:F row as it exists in a monthly workbook table.</summary>
public sealed record ReportRow(
    DateOnly Date,
    string Type,
    string PaymentMethod,
    string Category,
    long AmountIdr,
    string? Description);

public sealed record ReportCategory(string Name, string Kind, long? MonthlyBudgetIdr);

/// <summary>Manual balance inputs on a monthly sheet. Null means the cell is empty or not recognized.</summary>
public sealed record MonthBalanceInputs(long? OpeningBalanceIdr, long? ActualBalanceIdr);

/// <summary>Everything a report needs, read from one workbook revision under the workbook lock.</summary>
public sealed record WorkbookYearSnapshot(
    int Year,
    DateTimeOffset LastModifiedAtUtc,
    IReadOnlyList<string> TransactionTypes,
    IReadOnlyList<string> PaymentMethods,
    IReadOnlyList<ReportCategory> Categories,
    IReadOnlyList<IReadOnlyList<ReportRow>> Months,
    IReadOnlyList<MonthBalanceInputs> Balances);

public sealed record CategoryAmount(string Category, long AmountIdr, int TransactionCount, long? MonthlyBudgetIdr);

public sealed record PaymentAmount(string PaymentMethod, long AmountIdr, int TransactionCount);

public sealed record MonthReport(
    int Month,
    long IncomeIdr,
    long ExpenseIdr,
    long NetIdr,
    long CumulativeNetIdr,
    decimal? SavingRate,
    int TransactionCount,
    long? OpeningBalanceIdr,
    long? EstimatedClosingIdr,
    long? ActualBalanceIdr,
    long? BalanceDifferenceIdr,
    IReadOnlyList<CategoryAmount> ExpenseByCategory,
    IReadOnlyList<ReportRow> Rows);

public sealed record CategoryYearRow(
    string Category,
    bool InMaster,
    long? MonthlyBudgetIdr,
    IReadOnlyList<long> MonthlyAmountsIdr,
    long TotalIdr,
    long AveragePerFilledMonthIdr);

public sealed record YearReport(
    int Year,
    DateTimeOffset LastModifiedAtUtc,
    IReadOnlyList<string> TransactionTypes,
    IReadOnlyList<string> PaymentMethods,
    IReadOnlyList<ReportCategory> Categories,
    IReadOnlyList<MonthReport> Months,
    long IncomeIdr,
    long ExpenseIdr,
    long NetIdr,
    decimal? SavingRate,
    int TransactionCount,
    int FilledMonthCount,
    long AverageExpensePerFilledMonthIdr,
    IReadOnlyList<CategoryYearRow> ExpenseMatrix,
    IReadOnlyList<PaymentAmount> ExpenseByPaymentMethod);

public interface IFinanceReportService
{
    /// <summary>Returns null when the user has no workbook for the year.</summary>
    Task<YearReport?> GetYearReportAsync(Guid userId, int year, CancellationToken cancellationToken);

    Task<IReadOnlyList<int>> ListYearsAsync(Guid userId, CancellationToken cancellationToken);
}
