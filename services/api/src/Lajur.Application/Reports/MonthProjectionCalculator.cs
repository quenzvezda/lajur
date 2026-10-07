namespace Lajur.Application.Reports;

/// <summary>
/// Mirrors the monthly sheet panels "A. PROYEKSI KAS" and "B. LAJU BELANJA" from canonical rows,
/// Master and the sheet's manual inputs. <c>today</c> plays the role of Excel's TODAY().
/// Money is whole rupiah; divisions round half away from zero at the end.
/// </summary>
public static class MonthProjectionCalculator
{
    public const string StatusNotStarted = "belum mulai";
    public const string StatusAboveHabit = "di atas kebiasaan";
    public const string StatusBelowHabit = "di bawah kebiasaan";

    /// <summary>
    /// Excel: hari berjalan = IF(TODAY()>=end, DAY(end), IF(TODAY()&lt;start, 0, DAY(TODAY())));
    /// proyeksi = belanja ÷ hari berjalan × hari dalam bulan; status vs Rekap's monthly average.
    /// </summary>
    public static SpendingPace Pace(
        int year,
        int month,
        long spentIdr,
        DateOnly today,
        long historicalMonthlyAverageIdr,
        long totalMasterBudgetIdr)
    {
        var start = new DateOnly(year, month, 1);
        var days = DateTime.DaysInMonth(year, month);
        var end = start.AddDays(days - 1);
        var elapsed = today >= end ? days : today < start ? 0 : today.Day;
        long perDay = elapsed == 0 ? 0 : RoundRupiah((decimal)spentIdr / elapsed);
        long projected = elapsed == 0 ? 0 : RoundRupiah((decimal)spentIdr * days / elapsed);
        var status = elapsed == 0
            ? StatusNotStarted
            : projected > historicalMonthlyAverageIdr ? StatusAboveHabit : StatusBelowHabit;
        return new SpendingPace(
            elapsed,
            days,
            spentIdr,
            perDay,
            projected,
            historicalMonthlyAverageIdr,
            totalMasterBudgetIdr,
            status);
    }

    public static CashProjection? Cash(
        int year,
        int month,
        IReadOnlyList<IncomeSourceInput>? sources,
        IReadOnlyList<ReportRow> rows,
        long recordedIncomeIdr,
        long? openingBalanceIdr,
        SpendingPace pace,
        DateOnly today)
    {
        if (sources is null)
        {
            return null;
        }

        var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var monthOver = today > monthEnd;
        var projections = new List<IncomeSourceProjection>(sources.Count);
        foreach (var source in sources)
        {
            if (source.IncomeCategory is null || source.PaidThresholdIdr is null || !source.EstimateRecognized)
            {
                projections.Add(new IncomeSourceProjection(
                    source.Label,
                    source.PayoutDay,
                    source.EstimateRecognized ? source.EstimateIdr : null,
                    source.IncomeCategory,
                    null,
                    null,
                    "belum dipetakan",
                    false));
                continue;
            }

            var actual = rows
                .Where(row => row.Type == FinanceReportCalculator.IncomeType && row.Category == source.IncomeCategory)
                .Sum(row => row.AmountIdr);
            var paid = actual > source.PaidThresholdIdr.Value;
            var estimate = source.EstimateIdr ?? 0; // Excel N(empty) = 0
            var remaining = paid || monthOver ? 0 : estimate;
            var status = paid ? "sudah cair"
                : monthOver ? "bulan sudah lewat"
                : estimate == 0 ? "isi estimasi dulu"
                : source.PayoutDay is { } day ? $"menunggu tgl {day}" : "menunggu";
            projections.Add(new IncomeSourceProjection(
                source.Label,
                source.PayoutDay,
                source.EstimateIdr,
                source.IncomeCategory,
                actual,
                remaining,
                status,
                true));
        }

        var complete = projections.All(item => item.Mapped);
        long? remainingTotal = complete ? projections.Sum(item => item.RemainingIdr!.Value) : null;
        long? projectedIncome = remainingTotal is { } value ? recordedIncomeIdr + value : null;
        return new CashProjection(
            projections,
            sources.All(source => source.EstimateRecognized) ? sources.Sum(source => source.EstimateIdr ?? 0) : null,
            complete ? projections.Sum(item => item.ActualIdr!.Value) : null,
            remainingTotal,
            recordedIncomeIdr,
            projectedIncome,
            pace.ProjectedMonthEndIdr,
            projectedIncome - pace.ProjectedMonthEndIdr,
            projectedIncome is { } income && openingBalanceIdr is { } opening
                ? opening + income - pace.ProjectedMonthEndIdr
                : null,
            complete);
    }

    private static long RoundRupiah(decimal value) =>
        (long)Math.Round(value, 0, MidpointRounding.AwayFromZero);
}
