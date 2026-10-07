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
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
