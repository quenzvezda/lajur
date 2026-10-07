using System.Security.Cryptography;
using Lajur.Infrastructure.Workbooks;

namespace Lajur.WorkbookTests;

public sealed class OpenXmlWorkbookYearReadTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "LajurWorkbookFixture.xlsm");

    [Fact]
    public void ReadMonthBalanceInputs_ReadsLabelledManualInputs()
    {
        var balances = new OpenXmlWorkbookReader().ReadMonthBalanceInputs(FixturePath, 1);

        Assert.Equal(2_000_000, balances.OpeningBalanceIdr);
        Assert.Equal(2_500_000, balances.ActualBalanceIdr);
    }

    [Fact]
    public void ReadIncomeSourceInputs_MapsKnownFormulaShapes()
    {
        var sources = new OpenXmlWorkbookReader().ReadIncomeSourceInputs(FixturePath, 1);

        Assert.NotNull(sources);
        Assert.Collection(
            sources,
            meal =>
            {
                // =$M$10*$M$11 with an empty working-days cell: recognized, counts as 0 like Excel.
                Assert.Equal(10, meal.PayoutDay);
                Assert.Null(meal.EstimateIdr);
                Assert.True(meal.EstimateRecognized);
                Assert.Equal("Uang Makan", meal.IncomeCategory);
                Assert.Equal(0, meal.PaidThresholdIdr);
            },
            salary =>
            {
                Assert.Equal(5_000_000, salary.EstimateIdr);
                Assert.Equal("Gaji", salary.IncomeCategory);
                Assert.Equal(1_000_000, salary.PaidThresholdIdr);
            },
            freelance =>
            {
                Assert.Equal(20, freelance.PayoutDay);
                Assert.Equal(1_000_000, freelance.EstimateIdr);
                Assert.Equal("Freelance", freelance.IncomeCategory);
            });
    }

    [Fact]
    public async Task ReadYearAsync_ReturnsAllMonthsAndDoesNotModifyTheWorkbook()
    {
        var root = Directory.CreateTempSubdirectory("lajur-year-read-");
        try
        {
            var key = new WorkbookKey(Guid.NewGuid(), 2026);
            var path = Path.Combine(root.FullName, "users", key.UserId.ToString("D"), "workbooks", "Keuangan_Pribadi_2026.xlsm");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(FixturePath, path);
            var hashBefore = SHA256.HashData(await File.ReadAllBytesAsync(path));
            var writeTimeBefore = File.GetLastWriteTimeUtc(path);

            var data = await new OpenXmlWorkbookStore(root.FullName).ReadYearAsync(key);

            Assert.Equal(12, data.Months.Count);
            Assert.Equal(12, data.Balances.Count);
            Assert.NotEmpty(data.Master.Categories);
            Assert.NotEmpty(data.Months[0]);
            Assert.All(data.Months.SelectMany(rows => rows), row => Assert.Equal(2026, row.Date.Year));
            Assert.Equal(hashBefore, SHA256.HashData(await File.ReadAllBytesAsync(path)));
            Assert.Equal(writeTimeBefore, File.GetLastWriteTimeUtc(path));
            Assert.All(data.IncomeSources, Assert.NotNull);

            var store = new OpenXmlWorkbookStore(root.FullName);
            var first = await store.ReadYearAsync(key);
            Assert.Same(first, await store.ReadYearAsync(key));
            File.SetLastWriteTimeUtc(path, writeTimeBefore.AddMinutes(1));
            Assert.NotSame(first, await store.ReadYearAsync(key));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
