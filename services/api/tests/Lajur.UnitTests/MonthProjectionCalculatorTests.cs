using Lajur.Application.Reports;

namespace Lajur.UnitTests;

public sealed class MonthProjectionCalculatorTests
{
    [Fact]
    public void Pace_extrapolates_linearly_from_elapsed_days_like_the_workbook()
    {
        var pace = MonthProjectionCalculator.Pace(2026, 9, 1_000_000, new DateOnly(2026, 9, 10), 2_500_000, 7_000_000);

        Assert.Equal(10, pace.ElapsedDays);
        Assert.Equal(30, pace.DaysInMonth);
        Assert.Equal(100_000, pace.AveragePerDayIdr);
        Assert.Equal(3_000_000, pace.ProjectedMonthEndIdr);
        Assert.Equal(MonthProjectionCalculator.StatusAboveHabit, pace.Status);
        Assert.Equal(7_000_000, pace.TotalMasterBudgetIdr);
    }

    [Theory]
    [InlineData(2026, 9, 30, 30)] // month end reached → all days elapsed
    [InlineData(2026, 10, 7, 30)] // month already over
    [InlineData(2026, 8, 31, 0)]  // month not started yet
    public void Pace_elapsed_days_follow_the_today_rule(int year, int month, int day, int expectedElapsed)
    {
        var pace = MonthProjectionCalculator.Pace(2026, 9, 300_000, new DateOnly(year, month, day), 1_000_000, 0);

        Assert.Equal(expectedElapsed, pace.ElapsedDays);
        Assert.Equal(expectedElapsed == 0 ? MonthProjectionCalculator.StatusNotStarted : MonthProjectionCalculator.StatusBelowHabit,
            pace.Status);
        Assert.Equal(expectedElapsed == 0 ? 0 : 300_000, pace.ProjectedMonthEndIdr);
    }

    [Fact]
    public void Pace_rounds_half_away_from_zero_only_at_the_end()
    {
        // 100,001 ÷ 2 days × 31 = 1,550,015.5 → 1,550,016
        var pace = MonthProjectionCalculator.Pace(2026, 1, 100_001, new DateOnly(2026, 1, 2), 0, 0);

        Assert.Equal(50_001, pace.AveragePerDayIdr);
        Assert.Equal(1_550_016, pace.ProjectedMonthEndIdr);
    }

    [Fact]
    public void Cash_projection_matches_the_workbook_statuses_and_summary()
    {
        var sources = new[]
        {
            new IncomeSourceInput("Uang Makan", 10, 200_000, true, "Uang Makan", 0),
            new IncomeSourceInput("Gaji Utama", 25, 10_000_000, true, "Gaji", 1_000_000),
            new IncomeSourceInput("Freelance", 28, null, true, "Freelance", 0)
        };
        var rows = new[]
        {
            new ReportRow(new DateOnly(2026, 9, 10), "Pemasukan", "Bank", "Uang Makan", 200_000, null),
            new ReportRow(new DateOnly(2026, 9, 12), "Pemasukan", "Bank", "Gaji", 500_000, null),
            new ReportRow(new DateOnly(2026, 9, 12), "Pengeluaran", "Bank", "Gaji", 9_000_000, null)
        };
        var today = new DateOnly(2026, 9, 15);
        var pace = MonthProjectionCalculator.Pace(2026, 9, 1_500_000, today, 0, 0);

        var projection = MonthProjectionCalculator.Cash(2026, 9, sources, rows, 700_000, 2_000_000, pace, today)!;

        Assert.True(projection.Complete);
        Assert.Collection(
            projection.Sources,
            item => Assert.Equal(("sudah cair", 200_000L, 0L), (item.Status, item.ActualIdr!.Value, item.RemainingIdr!.Value)),
            // 500,000 received is below the 1,000,000 "paid" threshold: still waiting, full estimate remains.
            item => Assert.Equal(("menunggu tgl 25", 500_000L, 10_000_000L), (item.Status, item.ActualIdr!.Value, item.RemainingIdr!.Value)),
            item => Assert.Equal(("isi estimasi dulu", 0L, 0L), (item.Status, item.ActualIdr!.Value, item.RemainingIdr!.Value)));
        Assert.Equal(10_200_000, projection.EstimateTotalIdr);
        Assert.Equal(10_000_000, projection.RemainingTotalIdr);
        Assert.Equal(10_700_000, projection.ProjectedIncomeIdr);
        Assert.Equal(3_000_000, projection.ProjectedExpenseIdr);
        Assert.Equal(7_700_000, projection.ProjectedNetIdr);
        Assert.Equal(9_700_000, projection.ProjectedClosingIdr);
    }

    [Fact]
    public void Cash_projection_after_month_end_has_nothing_remaining()
    {
        var sources = new[] { new IncomeSourceInput("Gaji", 25, 10_000_000, true, "Gaji", 1_000_000) };
        var today = new DateOnly(2026, 10, 1);
        var pace = MonthProjectionCalculator.Pace(2026, 9, 0, today, 0, 0);

        var projection = MonthProjectionCalculator.Cash(2026, 9, sources, [], 0, null, pace, today)!;

        var source = Assert.Single(projection.Sources);
        Assert.Equal("bulan sudah lewat", source.Status);
        Assert.Equal(0, source.RemainingIdr);
        Assert.Null(projection.ProjectedClosingIdr); // opening balance missing ≠ zero
    }

    [Fact]
    public void Unrecognized_formula_is_marked_unmapped_and_blocks_dependent_totals()
    {
        var sources = new[]
        {
            new IncomeSourceInput("Gaji", 25, 10_000_000, true, "Gaji", 1_000_000),
            new IncomeSourceInput("Bonus", 1, 500_000, true, null, null)
        };
        var today = new DateOnly(2026, 9, 2);
        var pace = MonthProjectionCalculator.Pace(2026, 9, 0, today, 0, 0);

        var projection = MonthProjectionCalculator.Cash(2026, 9, sources, [], 0, 1_000_000, pace, today)!;

        Assert.False(projection.Complete);
        Assert.Equal("belum dipetakan", projection.Sources[1].Status);
        Assert.Null(projection.RemainingTotalIdr);
        Assert.Null(projection.ProjectedIncomeIdr);
        Assert.Null(projection.ProjectedClosingIdr);
        Assert.Equal(10_500_000, projection.EstimateTotalIdr);
    }

    [Fact]
    public void Missing_block_yields_no_projection()
    {
        var pace = MonthProjectionCalculator.Pace(2026, 9, 0, new DateOnly(2026, 9, 2), 0, 0);

        Assert.Null(MonthProjectionCalculator.Cash(2026, 9, null, [], 0, null, pace, new DateOnly(2026, 9, 2)));
    }
}
