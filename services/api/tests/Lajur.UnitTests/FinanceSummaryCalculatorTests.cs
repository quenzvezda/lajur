using Lajur.Application.Finance;

namespace Lajur.UnitTests;

public sealed class FinanceSummaryCalculatorTests
{
    [Fact]
    public void Calculate_sums_income_and_expense_without_formula_caches()
    {
        var result = FinanceSummaryCalculator.Calculate(
            2026,
            9,
            [
                new FinanceAmount("Pemasukan", 5_000_000),
                new FinanceAmount("Pengeluaran", 125_000),
                new FinanceAmount("Pengeluaran", 75_000)
            ]);

        Assert.Equal(2026, result.Year);
        Assert.Equal(9, result.Month);
        Assert.Equal(5_000_000, result.IncomeIdr);
        Assert.Equal(200_000, result.ExpenseIdr);
        Assert.Equal(3, result.TransactionCount);
    }

    [Fact]
    public void Calculate_counts_unknown_type_without_misclassifying_its_amount()
    {
        var result = FinanceSummaryCalculator.Calculate(
            2026,
            1,
            [new FinanceAmount("FutureType", 50_000)]);

        Assert.Equal(0, result.IncomeIdr);
        Assert.Equal(0, result.ExpenseIdr);
        Assert.Equal(1, result.TransactionCount);
    }
}
