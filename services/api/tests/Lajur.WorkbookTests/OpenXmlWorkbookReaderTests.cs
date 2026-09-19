using System.Security.Cryptography;
using Lajur.Infrastructure.Workbooks;

namespace Lajur.WorkbookTests;

public sealed class OpenXmlWorkbookReaderTests
{
    private static string FixturePath => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "LajurWorkbookFixture.xlsm");

    [Fact]
    public void ReadMonthlyTransactions_ReadsJanuaryRowsInTableOrder()
    {
        var reader = new OpenXmlWorkbookReader();

        var transactions = reader.ReadMonthlyTransactions(FixturePath, 1);

        Assert.Collection(
            transactions,
            first => Assert.Equal(
                new WorkbookTransactionRow(
                    new DateOnly(2026, 1, 3),
                    "Pengeluaran",
                    "Tunai Uji",
                    "Kategori Uji 01",
                    125_000,
                    "Belanja uji alfa"),
                first),
            second => Assert.Equal(
                new WorkbookTransactionRow(
                    new DateOnly(2026, 1, 10),
                    "Pemasukan",
                    "Bank Uji",
                    "Pendapatan Uji 01",
                    5_000_000,
                    "Pemasukan uji alfa"),
                second),
            third => Assert.Equal(
                new WorkbookTransactionRow(
                    new DateOnly(2026, 1, 15),
                    "Pengeluaran",
                    "Dompet Uji",
                    "Kategori Uji 02",
                    75_000,
                    "Belanja uji beta"),
                third));
    }

    [Fact]
    public void ReadMonthlyTransactions_ReturnsNoRowsForEmptyMonth()
    {
        var reader = new OpenXmlWorkbookReader();

        var transactions = reader.ReadMonthlyTransactions(FixturePath, 3);

        Assert.Empty(transactions);
    }

    [Fact]
    public void ReadMonthlyTransactions_UsesExplicitMappingForAllTwelveMonths()
    {
        var reader = new OpenXmlWorkbookReader();
        var expectedCounts = new[] { 3, 2, 0, 0, 0, 0, 0, 0, 2, 0, 0, 1 };

        var actualCounts = Enumerable.Range(1, 12)
            .Select(month => reader.ReadMonthlyTransactions(FixturePath, month).Count)
            .ToArray();

        Assert.Equal(expectedCounts, actualCounts);
    }

    [Fact]
    public void ReadMonthlyTransactions_RejectsInvalidMonth()
    {
        var reader = new OpenXmlWorkbookReader();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            reader.ReadMonthlyTransactions(FixturePath, 13));
    }

    [Fact]
    public void ReadMonthlyTransactions_DoesNotModifyCopiedWorkbook()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("lajur-workbook-reader-");
        var copiedWorkbook = Path.Combine(tempDirectory.FullName, "fixture.xlsm");

        try
        {
            File.Copy(FixturePath, copiedWorkbook);
            var hashBefore = SHA256.HashData(File.ReadAllBytes(copiedWorkbook));

            var reader = new OpenXmlWorkbookReader();
            _ = reader.ReadMonthlyTransactions(copiedWorkbook, 12);

            var hashAfter = SHA256.HashData(File.ReadAllBytes(copiedWorkbook));
            Assert.Equal(hashBefore, hashAfter);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }
}
