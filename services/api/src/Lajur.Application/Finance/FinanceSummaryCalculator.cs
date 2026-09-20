namespace Lajur.Application.Finance;

public sealed record FinanceAmount(string Type, long AmountIdr);

public static class FinanceSummaryCalculator
{
    public static FinanceSummary Calculate(
        int year,
        int month,
        IEnumerable<FinanceAmount> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        long income = 0;
        long expense = 0;
        var count = 0;

        checked
        {
            foreach (var row in rows)
            {
                count++;
                if (string.Equals(row.Type, "Pemasukan", StringComparison.Ordinal))
                {
                    income += row.AmountIdr;
                }
                else if (string.Equals(row.Type, "Pengeluaran", StringComparison.Ordinal))
                {
                    expense += row.AmountIdr;
                }
            }
        }

        return new FinanceSummary(year, month, income, expense, count);
    }
}
