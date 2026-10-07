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

/// <summary>
/// One income source of the monthly cash-projection block. <see cref="IncomeCategory"/> or
/// <see cref="PaidThresholdIdr"/> is null when the workbook formula shape is not recognized.
/// </summary>
public sealed record IncomeSourceInput(
    string Label,
    int? PayoutDay,
    long? EstimateIdr,
    bool EstimateRecognized,
    string? IncomeCategory,
    long? PaidThresholdIdr);

/// <summary>Everything a report needs, read from one workbook revision under the workbook lock.</summary>
public sealed record WorkbookYearSnapshot(
    int Year,
    DateTimeOffset LastModifiedAtUtc,
    IReadOnlyList<string> TransactionTypes,
    IReadOnlyList<string> PaymentMethods,
    IReadOnlyList<ReportCategory> Categories,
    IReadOnlyList<IReadOnlyList<ReportRow>> Months,
    IReadOnlyList<MonthBalanceInputs> Balances,
    IReadOnlyList<IReadOnlyList<IncomeSourceInput>?>? IncomeSources = null);

/// <summary>Panel "B. LAJU BELANJA": linear extrapolation of this month's spending.</summary>
public sealed record SpendingPace(
    int ElapsedDays,
    int DaysInMonth,
    long SpentIdr,
    long AveragePerDayIdr,
    long ProjectedMonthEndIdr,
    long HistoricalMonthlyAverageIdr,
    long TotalMasterBudgetIdr,
    string Status);

public sealed record IncomeSourceProjection(
    string Label,
    int? PayoutDay,
    long? EstimateIdr,
    string? IncomeCategory,
    long? ActualIdr,
    long? RemainingIdr,
    string Status,
    bool Mapped);

/// <summary>Panel "A. PROYEKSI KAS" plus "Ringkasan proyeksi". Null totals mean an unmapped input.</summary>
public sealed record CashProjection(
    IReadOnlyList<IncomeSourceProjection> Sources,
    long? EstimateTotalIdr,
    long? ActualTotalIdr,
    long? RemainingTotalIdr,
    long RecordedIncomeIdr,
    long? ProjectedIncomeIdr,
    long ProjectedExpenseIdr,
    long? ProjectedNetIdr,
    long? ProjectedClosingIdr,
    bool Complete);

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
    IReadOnlyList<ReportRow> Rows,
    SpendingPace? Pace = null,
    CashProjection? Projection = null);

public sealed record CategoryYearRow(
    string Category,
    bool InMaster,
    long? MonthlyBudgetIdr,
    IReadOnlyList<long> MonthlyAmountsIdr,
    long TotalIdr,
    long AveragePerFilledMonthIdr);

public sealed record YearReport(
    int Year,
    DateOnly Today,
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
    /// <param name="today">The user's local date; drives "hari berjalan" like Excel's TODAY().</param>
    Task<YearReport?> GetYearReportAsync(Guid userId, int year, DateOnly today, CancellationToken cancellationToken);

    Task<IReadOnlyList<int>> ListYearsAsync(Guid userId, CancellationToken cancellationToken);
}
