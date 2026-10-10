using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Lajur.Infrastructure.Workbooks;

namespace Lajur.WorkbookTests;

public sealed partial class OpenXmlWorkbookStoreTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AmbiguousIndexIdentityOrLocation_IsRejectedInsteadOfEditingTheWrongRow(bool duplicateId)
    {
        using var workspace = new WorkbookWorkspace(FixturePath, 2026);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);
        var draft = new WorkbookTransactionDraft(new DateOnly(2026, 10, 8), "Pengeluaran", "", "Kategori Uji 06", 15000, "Duplicate content");
        var audit = new WorkbookAuditContext(WorkbookChangeSource.Web, null, "ambiguous-index");
        await store.CreateTransactionAsync(workspace.Key, new(Guid.NewGuid(), draft, audit));
        await store.CreateTransactionAsync(workspace.Key, new(Guid.NewGuid(), draft, audit));
        using (var document = SpreadsheetDocument.Open(workspace.WorkbookPath, true))
        {
            var workbook = document.WorkbookPart!;
            var sheet = workbook.Workbook!.Sheets!.Elements<Sheet>().Single(s => s.Name == "__AppIndex");
            var part = (WorksheetPart)workbook.GetPartById(sheet.Id!.Value!);
            var worksheet = part.Worksheet ?? throw new InvalidDataException("Missing index.");
            var rows = worksheet.GetFirstChild<SheetData>()!.Elements<Row>().Skip(1).ToArray();
            var column = duplicateId ? "A" : "D";
            var source = rows[0].Elements<Cell>().Single(c => c.CellReference!.Value!.StartsWith(column, StringComparison.Ordinal));
            var target = rows[1].Elements<Cell>().Single(c => c.CellReference!.Value!.StartsWith(column, StringComparison.Ordinal));
            target.InlineString = new InlineString(new Text(source.InnerText)); target.CellValue = null; target.DataType = CellValues.InlineString;
            worksheet.Save();
        }
        var before = SHA256.HashData(File.ReadAllBytes(workspace.WorkbookPath));
        Assert.Throws<InvalidDataException>(() => new OpenXmlWorkbookReader().ReadMonthlyTransactions(workspace.WorkbookPath, 10, includeIdentity: true));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(workspace.WorkbookPath)));
    }

    [Fact]
    public async Task ReportRows_KeepDistinctIdentitiesForIdenticalTransactions_AfterDeleteAndMove()
    {
        using var workspace = new WorkbookWorkspace(FixturePath, 2026);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);
        var legacy = await store.ReadYearAsync(workspace.Key);
        Assert.All(legacy.Months.SelectMany(rows => rows), row => Assert.Null(row.TransactionId));
        var draft = new WorkbookTransactionDraft(new DateOnly(2026, 10, 8), "Pengeluaran", "", "Kategori Uji 06", 15000, "Identical rows");
        var audit = new WorkbookAuditContext(WorkbookChangeSource.Web, null, "test-web-report");
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        await store.CreateTransactionAsync(workspace.Key, new(first, draft, audit));
        await store.CreateTransactionAsync(workspace.Key, new(second, draft, audit));
        var report = await store.ReadYearAsync(workspace.Key);
        Assert.Equal(new[] { first, second }, report.Months[9].Select(row => row.TransactionId!.Value));
        await store.DeleteTransactionAsync(workspace.Key, new(first, 1, audit));
        report = await store.ReadYearAsync(workspace.Key);
        Assert.Equal(second, Assert.Single(report.Months[9]).TransactionId);
        await store.UpdateTransactionAsync(workspace.Key, new(second, 1, draft with { Date = new DateOnly(2026, 11, 9) }, audit));
        report = await store.ReadYearAsync(workspace.Key);
        Assert.Empty(report.Months[9]);
        var moved = Assert.Single(report.Months[10]);
        Assert.Equal(second, moved.TransactionId); Assert.Equal(2, moved.Revision);
        var before = SHA256.HashData(File.ReadAllBytes(workspace.WorkbookPath));
        _ = await store.ReadYearAsync(workspace.Key);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(workspace.WorkbookPath)));
    }

    [Fact]
    public async Task ReportRows_WithMismatchedVisibleContent_DoNotExposeAnEditableIdentity()
    {
        using var workspace = new WorkbookWorkspace(FixturePath, 2026);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);
        var draft = new WorkbookTransactionDraft(new DateOnly(2026, 10, 8), "Pengeluaran", "", "Kategori Uji 06", 15000, "Original");
        await store.CreateTransactionAsync(workspace.Key, new(Guid.NewGuid(), draft, new(WorkbookChangeSource.Web, null, "identity-test")));
        using (var document = SpreadsheetDocument.Open(workspace.WorkbookPath, true))
        {
            var workbook = document.WorkbookPart!;
            var sheet = workbook.Workbook!.Sheets!.Elements<Sheet>().Single(s => s.Name == "Okt");
            var part = (WorksheetPart)workbook.GetPartById(sheet.Id!.Value!);
            var worksheet = part.Worksheet ?? throw new InvalidDataException("Missing worksheet.");
            var cell = worksheet.Descendants<Cell>().Single(c => c.CellReference == "F6");
            cell.CellValue = null; cell.CellFormula = null; cell.DataType = CellValues.InlineString;
            cell.InlineString = new InlineString(new Text("Changed outside import")); worksheet.Save();
        }
        var before = SHA256.HashData(File.ReadAllBytes(workspace.WorkbookPath));
        var row = Assert.Single(new OpenXmlWorkbookReader().ReadMonthlyTransactions(workspace.WorkbookPath, 10, includeIdentity: true));
        Assert.Null(row.TransactionId); Assert.Null(row.Revision);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(workspace.WorkbookPath)));
    }

    [Fact]
    public async Task ImportedLegacyRows_HaveEditableIdentitiesWithoutWritingDuringReportReads()
    {
        using var workspace = new WorkbookWorkspace(FixturePath, 2026);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);
        await store.ImportWorkbookAsync(workspace.Key, FixturePath, true, new(WorkbookChangeSource.System, null, "migration"));
        var before = SHA256.HashData(File.ReadAllBytes(workspace.WorkbookPath));
        var data = await store.ReadYearAsync(workspace.Key);
        var rows = data.Months.SelectMany(month => month).ToArray();
        Assert.Equal(8, rows.Length);
        Assert.All(rows, row => { Assert.NotNull(row.TransactionId); Assert.Equal(1, row.Revision); });
        Assert.Equal(rows.Length, rows.Select(row => row.TransactionId).Distinct().Count());
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(workspace.WorkbookPath)));
    }
}
