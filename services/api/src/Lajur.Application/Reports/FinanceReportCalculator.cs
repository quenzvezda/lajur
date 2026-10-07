namespace Lajur.Application.Reports;

/// <summary>
/// Recomputes the known workbook aggregates (monthly header, Rekap, category pivot) from canonical
/// A:F rows and Master. Cached Excel formula/pivot values are never trusted because they can be stale
/// after API writes.
/// </summary>
public static class FinanceReportCalculator
{
    public const string IncomeType = "Pemasukan";
    public const string ExpenseType = "Pengeluaran";

    public static YearReport Calculate(WorkbookYearSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Months.Count != 12 || snapshot.Balances.Count != 12)
        {
            throw new ArgumentException("A year snapshot must contain exactly 12 months.", nameof(snapshot));
        }

        var budgets = new Dictionary<string, long?>(StringComparer.Ordinal);
        foreach (var category in snapshot.Categories)
        {
            budgets.TryAdd(category.Name, category.MonthlyBudgetIdr);
        }

        var months = new List<MonthReport>(12);
        long cumulative = 0;
        checked
        {
            for (var index = 0; index < 12; index++)
            {
                var rows = snapshot.Months[index];
                var balance = snapshot.Balances[index];
                var income = Sum(rows, IncomeType);
                var expense = Sum(rows, ExpenseType);
                var net = income - expense;
                cumulative += net;
                long? closing = balance.OpeningBalanceIdr is { } opening ? opening + income - expense : null;
                long? difference = balance.ActualBalanceIdr is { } actual && actual != 0 && closing is { } estimate
                    ? actual - estimate
                    : null;

                var byCategory = rows
                    .Where(IsExpense)
                    .GroupBy(row => row.Category, StringComparer.Ordinal)
                    .Select(group => new CategoryAmount(
                        group.Key,
                        group.Sum(row => row.AmountIdr),
                        group.Count(),
                        budgets.GetValueOrDefault(group.Key)))
                    .OrderByDescending(item => item.AmountIdr)
                    .ThenBy(item => item.Category, StringComparer.Ordinal)
                    .ToArray();

                months.Add(new MonthReport(
                    index + 1,
                    income,
                    expense,
                    net,
                    cumulative,
                    SavingRate(income, expense),
                    rows.Count,
                    balance.OpeningBalanceIdr,
                    closing,
                    balance.ActualBalanceIdr,
                    difference,
                    byCategory,
                    rows));
            }

            var totalIncome = months.Sum(month => month.IncomeIdr);
            var totalExpense = months.Sum(month => month.ExpenseIdr);
            var filledMonths = months.Count(month => month.ExpenseIdr > 0);

            return new YearReport(
                snapshot.Year,
                snapshot.LastModifiedAtUtc,
                snapshot.TransactionTypes,
                snapshot.PaymentMethods,
                snapshot.Categories,
                months,
                totalIncome,
                totalExpense,
                totalIncome - totalExpense,
                SavingRate(totalIncome, totalExpense),
                months.Sum(month => month.TransactionCount),
                filledMonths,
                Average(totalExpense, filledMonths),
                ExpenseMatrix(snapshot, filledMonths),
                ExpenseByPayment(snapshot));
        }
    }

    /// <summary>(income - expense) / income rounded to 4 decimals; null when income is zero (Excel shows blank).</summary>
    public static decimal? SavingRate(long incomeIdr, long expenseIdr) =>
        incomeIdr == 0
            ? null
            : Math.Round((decimal)(incomeIdr - expenseIdr) / incomeIdr, 4, MidpointRounding.AwayFromZero);

    /// <summary>Whole-rupiah average, half away from zero; zero when no month is filled (Excel IFERROR → 0).</summary>
    public static long Average(long totalIdr, int filledMonths) =>
        filledMonths == 0
            ? 0
            : (long)Math.Round((decimal)totalIdr / filledMonths, 0, MidpointRounding.AwayFromZero);

    private static IReadOnlyList<CategoryYearRow> ExpenseMatrix(WorkbookYearSnapshot snapshot, int filledMonths)
    {
        var order = new List<string>();
        var masterExpense = new HashSet<string>(StringComparer.Ordinal);
        var budgets = new Dictionary<string, long?>(StringComparer.Ordinal);
        foreach (var category in snapshot.Categories.Where(category =>
                     string.Equals(category.Kind, ExpenseType, StringComparison.Ordinal)))
        {
            if (masterExpense.Add(category.Name))
            {
                order.Add(category.Name);
                budgets[category.Name] = category.MonthlyBudgetIdr;
            }
        }

        var amounts = new Dictionary<string, long[]>(StringComparer.Ordinal);
        foreach (var name in order)
        {
            amounts[name] = new long[12];
        }

        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < 12; index++)
        {
            foreach (var row in snapshot.Months[index].Where(IsExpense))
            {
                if (!amounts.TryGetValue(row.Category, out var monthly))
                {
                    monthly = new long[12];
                    amounts[row.Category] = monthly;
                    unknown.Add(row.Category);
                }

                monthly[index] = checked(monthly[index] + row.AmountIdr);
            }
        }

        return order.Concat(unknown)
            .Select(name =>
            {
                var monthly = amounts[name];
                var total = monthly.Sum();
                return new CategoryYearRow(
                    name,
                    masterExpense.Contains(name),
                    budgets.GetValueOrDefault(name),
                    monthly,
                    total,
                    Average(total, filledMonths));
            })
            .ToArray();
    }

    private static IReadOnlyList<PaymentAmount> ExpenseByPayment(WorkbookYearSnapshot snapshot)
    {
        var totals = new Dictionary<string, (long Amount, int Count)>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var method in snapshot.PaymentMethods)
        {
            if (totals.TryAdd(method, (0, 0)))
            {
                order.Add(method);
            }
        }

        var extra = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var row in snapshot.Months.SelectMany(rows => rows).Where(IsExpense))
        {
            var key = row.PaymentMethod;
            if (!totals.TryGetValue(key, out var current))
            {
                extra.Add(key);
            }

            totals[key] = (checked(current.Amount + row.AmountIdr), current.Count + 1);
        }

        return order.Concat(extra)
            .Select(method => new PaymentAmount(method, totals[method].Amount, totals[method].Count))
            .ToArray();
    }

    private static long Sum(IEnumerable<ReportRow> rows, string type) =>
        rows.Where(row => string.Equals(row.Type, type, StringComparison.Ordinal)).Sum(row => row.AmountIdr);

    private static bool IsExpense(ReportRow row) => string.Equals(row.Type, ExpenseType, StringComparison.Ordinal);
}
