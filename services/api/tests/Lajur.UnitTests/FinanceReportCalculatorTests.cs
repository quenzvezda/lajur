using Lajur.Application.Reports;

namespace Lajur.UnitTests;

public sealed class FinanceReportCalculatorTests
{
    private static readonly ReportCategory[] Categories =
    [
        new("Makan", "Pengeluaran", 1_000_000),
        new("Transport", "Pengeluaran", null),
        new("Hiburan", "Pengeluaran", 200_000),
        new("Gaji", "Pemasukan", null)
    ];

    [Fact]
    public void Monthly_totals_match_rows_and_blank_payment_is_still_counted()
    {
        var report = FinanceReportCalculator.Calculate(Snapshot(
        [
            Row(2026, 3, 1, "Pemasukan", "Gaji", 5_000_000, payment: "Bank"),
            Row(2026, 3, 2, "Pengeluaran", "Makan", 40_000, payment: ""),
            Row(2026, 3, 2, "Pengeluaran", "Transport", 60_000, payment: "Tunai")
        ]));

        var march = report.Months[2];
        Assert.Equal(5_000_000, march.IncomeIdr);
        Assert.Equal(100_000, march.ExpenseIdr);
        Assert.Equal(4_900_000, march.NetIdr);
        Assert.Equal(3, march.TransactionCount);
        Assert.Equal(0.98m, march.SavingRate);
        Assert.Equal(3, march.Rows.Count);
        Assert.All(report.Months.Where(month => month.Month != 3), month => Assert.Equal(0, month.TransactionCount));
    }

    [Fact]
    public void Category_pivot_contains_only_expense_rows_sorted_by_amount_and_sums_to_monthly_expense()
    {
        var report = FinanceReportCalculator.Calculate(Snapshot(
        [
            Row(2026, 1, 3, "Pengeluaran", "Transport", 50_000),
            Row(2026, 1, 4, "Pengeluaran", "Makan", 30_000),
            Row(2026, 1, 5, "Pengeluaran", "Makan", 30_000),
            Row(2026, 1, 6, "Pemasukan", "Makan", 999_000),
            Row(2026, 1, 7, "Pengeluaran", "Hiburan", 50_000)
        ]));

        var pivot = report.Months[0].ExpenseByCategory;
        Assert.Equal(["Makan", "Hiburan", "Transport"], pivot.Select(item => item.Category));
        Assert.Equal(60_000, pivot[0].AmountIdr);
        Assert.Equal(2, pivot[0].TransactionCount);
        Assert.Equal(1_000_000, pivot[0].MonthlyBudgetIdr);
        Assert.Null(pivot[2].MonthlyBudgetIdr);
        Assert.Equal(report.Months[0].ExpenseIdr, pivot.Sum(item => item.AmountIdr));
    }

    [Fact]
    public void Saving_rate_is_null_without_income_and_negative_when_overspending()
    {
        Assert.Null(FinanceReportCalculator.SavingRate(0, 50_000));
        Assert.Null(FinanceReportCalculator.SavingRate(0, 0));
        Assert.Equal(-0.5m, FinanceReportCalculator.SavingRate(100_000, 150_000));
        Assert.Equal(0.3333m, FinanceReportCalculator.SavingRate(300_000, 200_000));
    }

    [Fact]
    public void Balance_inputs_are_not_treated_as_zero_when_missing()
    {
        var balances = Enumerable.Repeat(new MonthBalanceInputs(null, null), 12).ToArray();
        balances[0] = new MonthBalanceInputs(2_000_000, 2_500_000);
        balances[1] = new MonthBalanceInputs(1_000_000, 0);
        balances[2] = new MonthBalanceInputs(null, 900_000);
        var report = FinanceReportCalculator.Calculate(Snapshot(
            [Row(2026, 1, 1, "Pemasukan", "Gaji", 1_000_000), Row(2026, 1, 2, "Pengeluaran", "Makan", 300_000)],
            balances));

        Assert.Equal(2_700_000, report.Months[0].EstimatedClosingIdr);
        Assert.Equal(-200_000, report.Months[0].BalanceDifferenceIdr);
        Assert.Equal(1_000_000, report.Months[1].EstimatedClosingIdr);
        Assert.Null(report.Months[1].BalanceDifferenceIdr); // Excel: IF(B1=0,"",B1-D4)
        Assert.Null(report.Months[2].EstimatedClosingIdr);
        Assert.Null(report.Months[2].BalanceDifferenceIdr);
        Assert.Equal(900_000, report.Months[2].ActualBalanceIdr);
    }

    [Fact]
    public void Rekap_uses_cumulative_net_and_filled_month_divisor()
    {
        var report = FinanceReportCalculator.Calculate(Snapshot(
        [
            Row(2026, 1, 10, "Pemasukan", "Gaji", 1_000_000),
            Row(2026, 1, 11, "Pengeluaran", "Makan", 400_000),
            Row(2026, 2, 10, "Pemasukan", "Gaji", 1_000_000),
            Row(2026, 4, 1, "Pengeluaran", "Makan", 500_001),
            Row(2026, 4, 2, "Pengeluaran", "Kategori Lama", 100_000, payment: "Lama")
        ]));

        Assert.Equal(600_000, report.Months[0].CumulativeNetIdr);
        Assert.Equal(1_600_000, report.Months[1].CumulativeNetIdr);
        Assert.Equal(1_600_000, report.Months[2].CumulativeNetIdr);
        Assert.Equal(999_999, report.Months[3].CumulativeNetIdr);
        Assert.Equal(2, report.FilledMonthCount);
        Assert.Equal(1_000_001, report.ExpenseIdr);
        Assert.Equal(500_001, report.AverageExpensePerFilledMonthIdr); // 500,000.5 rounds away from zero
        Assert.Equal(5, report.TransactionCount);

        var makan = Assert.Single(report.ExpenseMatrix, row => row.Category == "Makan");
        Assert.True(makan.InMaster);
        Assert.Equal(400_000, makan.MonthlyAmountsIdr[0]);
        Assert.Equal(500_001, makan.MonthlyAmountsIdr[3]);
        Assert.Equal(900_001, makan.TotalIdr);
        Assert.Equal(450_001, makan.AveragePerFilledMonthIdr);

        // Master expense categories keep Master order and appear even when empty; unknown ones follow.
        Assert.Equal(["Makan", "Transport", "Hiburan", "Kategori Lama"], report.ExpenseMatrix.Select(row => row.Category));
        Assert.False(report.ExpenseMatrix[^1].InMaster);
        Assert.Equal(report.ExpenseIdr, report.ExpenseMatrix.Sum(row => row.TotalIdr));

        Assert.Equal(["Tunai", "Bank", "Lama"], report.ExpenseByPaymentMethod.Select(item => item.PaymentMethod));
        Assert.Equal(900_001, report.ExpenseByPaymentMethod[0].AmountIdr);
        Assert.Equal(0, report.ExpenseByPaymentMethod[1].AmountIdr);
    }

    [Fact]
    public void Empty_year_has_zero_totals_and_no_divide_by_zero()
    {
        var report = FinanceReportCalculator.Calculate(Snapshot([]));

        Assert.Equal(0, report.FilledMonthCount);
        Assert.Equal(0, report.AverageExpensePerFilledMonthIdr);
        Assert.Null(report.SavingRate);
        Assert.All(report.ExpenseMatrix, row => Assert.Equal(0, row.AveragePerFilledMonthIdr));
    }

    [Fact]
    public void Leap_day_row_is_reported_in_february()
    {
        var report = FinanceReportCalculator.Calculate(Snapshot(
            [Row(2028, 2, 29, "Pengeluaran", "Makan", 10_000)],
            year: 2028));

        Assert.Equal(new DateOnly(2028, 2, 29), Assert.Single(report.Months[1].Rows).Date);
        Assert.Equal(10_000, report.Months[1].ExpenseIdr);
    }

    private static WorkbookYearSnapshot Snapshot(
        IReadOnlyList<ReportRow> rows,
        IReadOnlyList<MonthBalanceInputs>? balances = null,
        int year = 2026) =>
        new(
            year,
            DateTimeOffset.UnixEpoch,
            ["Pengeluaran", "Pemasukan"],
            ["Tunai", "Bank"],
            Categories,
            Enumerable.Range(1, 12)
                .Select(month => (IReadOnlyList<ReportRow>)rows.Where(row => row.Date.Month == month).ToArray())
                .ToArray(),
            balances ?? Enumerable.Repeat(new MonthBalanceInputs(null, null), 12).ToArray());

    private static ReportRow Row(
        int year,
        int month,
        int day,
        string type,
        string category,
        long amount,
        string payment = "Tunai") =>
        new(new DateOnly(year, month, day), type, payment, category, amount, null);
}
