using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Lajur.Infrastructure.Workbooks;

namespace Lajur.WorkbookTests;

public sealed partial class OpenXmlWorkbookStoreTests
{
    private static string FixturePath => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "LajurWorkbookFixture.xlsm");

    [Fact]
    public async Task CreateTransactionAsync_AppendsCanonicalRowAndPreservesWorkbookFeatures()
    {
        using var workspace = new WorkbookWorkspace(FixturePath, 2026);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 19, 8, 30, 0, TimeSpan.Zero));
        var store = new OpenXmlWorkbookStore(workspace.RootPath, clock);
        var transactionId = Guid.Parse("66efec91-3bb0-4558-87c3-48f14da95c20");
        var vbaBefore = ReadVbaHash(workspace.WorkbookPath);
        var rightSideBefore = ReadRightSideCells(workspace.WorkbookPath, "Jan");
        var packageBefore = ReadPackageHashes(workspace.WorkbookPath);
        var allowedChangedParts = ReadExpectedChangedParts(workspace.WorkbookPath, "Jan", "JanTable");

        var created = await store.CreateTransactionAsync(
            workspace.Key,
            new CreateWorkbookTransactionCommand(
                transactionId,
                new WorkbookTransactionDraft(
                    new DateOnly(2026, 1, 21),
                    "Pengeluaran",
                    "Transfer Uji",
                    "Kategori Uji 06",
                    456_000,
                    "Belanja uji create"),
                new WorkbookAuditContext(
                    WorkbookChangeSource.Android,
                    "device-test",
                    "correlation-create")));

        Assert.Equal(transactionId, created.TransactionId);
        Assert.Equal(1, created.Revision);
        Assert.Equal("A5:F9", ReadTableReference(workspace.WorkbookPath, "Jan", "JanTable"));

        var rows = new OpenXmlWorkbookReader().ReadMonthlyTransactions(workspace.WorkbookPath, 1);
        Assert.Equal(4, rows.Count);
        Assert.Equal(
            new WorkbookTransactionRow(
                new DateOnly(2026, 1, 21),
                "Pengeluaran",
                "Transfer Uji",
                "Kategori Uji 06",
                456_000,
                "Belanja uji create"),
            rows[^1]);

        Assert.Equal(vbaBefore, ReadVbaHash(workspace.WorkbookPath));
        Assert.Equal(rightSideBefore, ReadRightSideCells(workspace.WorkbookPath, "Jan"));
        Assert.Equal(SheetStateValues.VeryHidden, ReadSheetState(workspace.WorkbookPath, "__AppIndex"));
        Assert.Equal(SheetStateValues.VeryHidden, ReadSheetState(workspace.WorkbookPath, "__AuditLog"));
        Assert.Contains(transactionId.ToString("D"), ReadWorksheetText(workspace.WorkbookPath, "__AppIndex"));
        Assert.Contains("CREATE", ReadWorksheetText(workspace.WorkbookPath, "__AuditLog"));
        AssertUnrelatedPackagePartsUnchanged(
            packageBefore,
            ReadPackageHashes(workspace.WorkbookPath),
            allowedChangedParts);
    }

    [Fact]
    public async Task InvalidCreate_DoesNotReplaceActiveWorkbookOrLeaveTemporaryFile()
    {
        using var workspace = new WorkbookWorkspace(FixturePath, 2026);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);
        var hashBefore = SHA256.HashData(File.ReadAllBytes(workspace.WorkbookPath));

        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateTransactionAsync(
            workspace.Key,
            new CreateWorkbookTransactionCommand(
                Guid.Parse("da14a33b-ca09-45de-83d5-698b06e7d21b"),
                new WorkbookTransactionDraft(
                    new DateOnly(2026, 4, 1),
                    "Pengeluaran",
                    "Tunai Uji",
                    "Kategori Tidak Ada",
                    10_000,
                    null),
                new WorkbookAuditContext(WorkbookChangeSource.Android, null, "invalid-create"))));

        Assert.Equal(hashBefore, SHA256.HashData(File.ReadAllBytes(workspace.WorkbookPath)));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(workspace.WorkbookPath)!, ".lajur-*.tmp.xlsm"));
    }

    [Fact]
    public async Task ConcurrentCreatesForSameWorkbook_AreSerializedWithoutLostRows()
    {
        using var workspace = new WorkbookWorkspace(FixturePath, 2026);
        Task<WorkbookTransaction> Create(Guid id, int day, string description) =>
            new OpenXmlWorkbookStore(workspace.RootPath).CreateTransactionAsync(
                workspace.Key,
                new CreateWorkbookTransactionCommand(
                    id,
                    new WorkbookTransactionDraft(
                        new DateOnly(2026, 8, day),
                        "Pengeluaran",
                        "Tunai Uji",
                        "Kategori Uji 10",
                        day * 10_000,
                        description),
                    new WorkbookAuditContext(WorkbookChangeSource.Android, "device-test", $"concurrent-{day}")));

        await Task.WhenAll(
            Create(Guid.Parse("dfb51aae-6dfc-4734-be76-a78d15bda30a"), 4, "Concurrent satu"),
            Create(Guid.Parse("91bc7456-cdf7-4d87-a547-82f108a136be"), 5, "Concurrent dua"));

        var rows = new OpenXmlWorkbookReader().ReadMonthlyTransactions(workspace.WorkbookPath, 8);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.Description == "Concurrent satu");
        Assert.Contains(rows, row => row.Description == "Concurrent dua");
    }

    [Fact]
    public async Task UpdateTransactionAsync_MovesRowAcrossMonthsAndIncrementsRevision()
    {
        using var workspace = new WorkbookWorkspace(FixturePath, 2026);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 19, 8, 30, 0, TimeSpan.Zero));
        var store = new OpenXmlWorkbookStore(workspace.RootPath, clock);
        var transactionId = Guid.Parse("fca131b3-9335-4562-bca5-8c83e49718b7");
        var vbaBefore = ReadVbaHash(workspace.WorkbookPath);
        var januaryRightSide = ReadRightSideCells(workspace.WorkbookPath, "Jan");
        var februaryRightSide = ReadRightSideCells(workspace.WorkbookPath, "Feb");

        await store.CreateTransactionAsync(
            workspace.Key,
            new CreateWorkbookTransactionCommand(
                transactionId,
                new WorkbookTransactionDraft(
                    new DateOnly(2026, 1, 22),
                    "Pengeluaran",
                    "Tunai Uji",
                    "Kategori Uji 07",
                    111_000,
                    "Sebelum update"),
                new WorkbookAuditContext(WorkbookChangeSource.Android, "device-test", "create-before-update")));

        var updated = await store.UpdateTransactionAsync(
            workspace.Key,
            new UpdateWorkbookTransactionCommand(
                transactionId,
                1,
                new WorkbookTransactionDraft(
                    new DateOnly(2026, 2, 25),
                    "Pengeluaran",
                    "Kartu Uji",
                    "Kategori Uji 08",
                    222_000,
                    "Sesudah update"),
                new WorkbookAuditContext(WorkbookChangeSource.Android, "device-test", "correlation-update")));

        Assert.Equal(2, updated.Revision);
        Assert.Equal("A5:F8", ReadTableReference(workspace.WorkbookPath, "Jan", "JanTable"));
        Assert.Equal("A5:F8", ReadTableReference(workspace.WorkbookPath, "Feb", "FebTable"));

        var reader = new OpenXmlWorkbookReader();
        Assert.Equal(3, reader.ReadMonthlyTransactions(workspace.WorkbookPath, 1).Count);
        Assert.Equal(
            new WorkbookTransactionRow(
                new DateOnly(2026, 2, 25),
                "Pengeluaran",
                "Kartu Uji",
                "Kategori Uji 08",
                222_000,
                "Sesudah update"),
            reader.ReadMonthlyTransactions(workspace.WorkbookPath, 2)[^1]);

        Assert.Equal(vbaBefore, ReadVbaHash(workspace.WorkbookPath));
        Assert.Equal(januaryRightSide, ReadRightSideCells(workspace.WorkbookPath, "Jan"));
        Assert.Equal(februaryRightSide, ReadRightSideCells(workspace.WorkbookPath, "Feb"));
        Assert.Contains("UPDATE", ReadWorksheetText(workspace.WorkbookPath, "__AuditLog"));
        Assert.Contains("correlation-update", ReadWorksheetText(workspace.WorkbookPath, "__AuditLog"));
    }

    [Fact]
    public async Task DeleteTransactionAsync_LeavesValidEmptyTableAndCreatesTombstone()
    {
        using var workspace = new WorkbookWorkspace(FixturePath, 2026);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 19, 8, 30, 0, TimeSpan.Zero));
        var store = new OpenXmlWorkbookStore(workspace.RootPath, clock);
        var transactionId = Guid.Parse("15d5a13f-7fcc-4134-b45e-2f4fa5e707f5");
        var vbaBefore = ReadVbaHash(workspace.WorkbookPath);
        var rightSideBefore = ReadRightSideCells(workspace.WorkbookPath, "Mar");

        await store.CreateTransactionAsync(
            workspace.Key,
            new CreateWorkbookTransactionCommand(
                transactionId,
                new WorkbookTransactionDraft(
                    new DateOnly(2026, 3, 8),
                    "Pengeluaran",
                    "Bank Uji",
                    "Kategori Uji 09",
                    333_000,
                    "Akan dihapus"),
                new WorkbookAuditContext(WorkbookChangeSource.Android, "device-test", "create-before-delete")));

        await store.DeleteTransactionAsync(
            workspace.Key,
            new DeleteWorkbookTransactionCommand(
                transactionId,
                1,
                new WorkbookAuditContext(WorkbookChangeSource.Android, "device-test", "correlation-delete")));

        Assert.Equal("A5:F6", ReadTableReference(workspace.WorkbookPath, "Mar", "MarTable"));
        Assert.Empty(new OpenXmlWorkbookReader().ReadMonthlyTransactions(workspace.WorkbookPath, 3));
        Assert.Equal(vbaBefore, ReadVbaHash(workspace.WorkbookPath));
        Assert.Equal(rightSideBefore, ReadRightSideCells(workspace.WorkbookPath, "Mar"));

        var indexText = ReadWorksheetText(workspace.WorkbookPath, "__AppIndex");
        Assert.Contains(transactionId.ToString("D"), indexText);
        Assert.Equal("1", ReadSystemValue(workspace.WorkbookPath, "__AppIndex", transactionId.ToString("D"), 8));
        Assert.Contains("DELETE", ReadWorksheetText(workspace.WorkbookPath, "__AuditLog"));
        Assert.Contains("correlation-delete", ReadWorksheetText(workspace.WorkbookPath, "__AuditLog"));
    }

    [Fact]
    public async Task UpdateTransactionAsync_InPlaceKeepsTableSizeAndRejectsStaleRevision()
    {
        using var workspace = new WorkbookWorkspace(FixturePath, 2026);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);
        var transactionId = Guid.Parse("8e172e29-8bde-442d-9974-7daa86f26af1");
        var audit = new WorkbookAuditContext(WorkbookChangeSource.Android, "device-test", "same-month-update");

        await store.CreateTransactionAsync(
            workspace.Key,
            new CreateWorkbookTransactionCommand(
                transactionId,
                new WorkbookTransactionDraft(
                    new DateOnly(2026, 7, 2),
                    "Pengeluaran",
                    "Tunai Uji",
                    "Kategori Uji 11",
                    50_000,
                    "Versi satu"),
                audit));

        var updated = await store.UpdateTransactionAsync(
            workspace.Key,
            new UpdateWorkbookTransactionCommand(
                transactionId,
                1,
                new WorkbookTransactionDraft(
                    new DateOnly(2026, 7, 3),
                    "Pengeluaran",
                    "Bank Uji",
                    "Kategori Uji 12",
                    60_000,
                    "Versi dua"),
                audit));

        Assert.Equal(2, updated.Revision);
        Assert.Equal("A5:F6", ReadTableReference(workspace.WorkbookPath, "July", "JulyTable"));
        Assert.Single(new OpenXmlWorkbookReader().ReadMonthlyTransactions(workspace.WorkbookPath, 7));

        var hashBeforeStaleUpdate = SHA256.HashData(File.ReadAllBytes(workspace.WorkbookPath));
        await Assert.ThrowsAsync<WorkbookRevisionConflictException>(() => store.UpdateTransactionAsync(
            workspace.Key,
            new UpdateWorkbookTransactionCommand(
                transactionId,
                1,
                new WorkbookTransactionDraft(
                    new DateOnly(2026, 7, 4),
                    "Pengeluaran",
                    "Bank Uji",
                    "Kategori Uji 12",
                    70_000,
                    "Stale"),
                audit)));
        Assert.Equal(hashBeforeStaleUpdate, SHA256.HashData(File.ReadAllBytes(workspace.WorkbookPath)));
    }

    [Fact]
    public async Task DeleteTransactionAsync_ShrinksPopulatedTable()
    {
        using var workspace = new WorkbookWorkspace(FixturePath, 2026);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);
        var transactionId = Guid.Parse("3f814c33-53c1-4f07-a16f-b0c891b6e311");
        var audit = new WorkbookAuditContext(WorkbookChangeSource.Android, null, "delete-populated");

        await store.CreateTransactionAsync(
            workspace.Key,
            new CreateWorkbookTransactionCommand(
                transactionId,
                new WorkbookTransactionDraft(
                    new DateOnly(2026, 1, 28),
                    "Pengeluaran",
                    "Tunai Uji",
                    "Kategori Uji 13",
                    80_000,
                    "Delete shrink"),
                audit));

        Assert.Equal("A5:F9", ReadTableReference(workspace.WorkbookPath, "Jan", "JanTable"));
        await store.DeleteTransactionAsync(
            workspace.Key,
            new DeleteWorkbookTransactionCommand(transactionId, 1, audit));

        Assert.Equal("A5:F8", ReadTableReference(workspace.WorkbookPath, "Jan", "JanTable"));
        Assert.Equal(3, new OpenXmlWorkbookReader().ReadMonthlyTransactions(workspace.WorkbookPath, 1).Count);
    }

    private static byte[] ReadVbaHash(string workbookPath)
    {
        using var document = SpreadsheetDocument.Open(workbookPath, false);
        using var stream = document.WorkbookPart!.VbaProjectPart!.GetStream(FileMode.Open, FileAccess.Read);
        return SHA256.HashData(stream);
    }

    private static IReadOnlyDictionary<string, string> ReadPackageHashes(string workbookPath)
    {
        using var stream = File.OpenRead(workbookPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        return archive.Entries.ToDictionary(
            entry => entry.FullName,
            entry =>
            {
                using var entryStream = entry.Open();
                return Convert.ToHexString(SHA256.HashData(entryStream));
            },
            StringComparer.Ordinal);
    }

    private static IReadOnlySet<string> ReadExpectedChangedParts(
        string workbookPath,
        string sheetName,
        string tableName)
    {
        using var document = SpreadsheetDocument.Open(workbookPath, false);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook!.Sheets!.Elements<Sheet>()
            .Single(candidate => candidate.Name?.Value == sheetName);
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
        var tablePart = worksheetPart.TableDefinitionParts
            .Single(candidate => candidate.Table?.Name?.Value == tableName);

        return new HashSet<string>(StringComparer.Ordinal)
        {
            "[Content_Types].xml",
            "xl/workbook.xml",
            "xl/_rels/workbook.xml.rels",
            worksheetPart.Uri.OriginalString.TrimStart('/'),
            tablePart.Uri.OriginalString.TrimStart('/')
        };
    }

    private static void AssertUnrelatedPackagePartsUnchanged(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after,
        IReadOnlySet<string> allowedChangedParts)
    {
        Assert.Empty(before.Keys.Except(after.Keys, StringComparer.Ordinal));

        foreach (var (partName, expectedHash) in before)
        {
            if (!allowedChangedParts.Contains(partName))
            {
                Assert.Equal(expectedHash, after[partName]);
            }
        }

        var addedParts = after.Keys.Except(before.Keys, StringComparer.Ordinal).ToArray();
        Assert.Equal(2, addedParts.Length);
        Assert.All(addedParts, part => Assert.Matches("^xl/worksheets/sheet[0-9]+\\.xml$", part));
    }

    private static IReadOnlyDictionary<string, string> ReadRightSideCells(string workbookPath, string sheetName)
    {
        using var document = SpreadsheetDocument.Open(workbookPath, false);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook!.Sheets!.Elements<Sheet>()
            .Single(candidate => candidate.Name?.Value == sheetName);
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);

        return worksheetPart.Worksheet!.GetFirstChild<SheetData>()!
            .Descendants<Cell>()
            .Where(cell => ColumnNumber(cell.CellReference?.Value) >= 7)
            .ToDictionary(cell => cell.CellReference!.Value!, cell => cell.OuterXml, StringComparer.Ordinal);
    }

    private static int ColumnNumber(string? reference)
    {
        var letters = CellReferenceRegex().Match(reference ?? string.Empty).Groups[1].Value;
        var result = 0;

        foreach (var letter in letters)
        {
            result = (result * 26) + (letter - 'A' + 1);
        }

        return result;
    }

    private static string ReadTableReference(string workbookPath, string sheetName, string tableName)
    {
        using var document = SpreadsheetDocument.Open(workbookPath, false);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook!.Sheets!.Elements<Sheet>()
            .Single(candidate => candidate.Name?.Value == sheetName);
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
        return worksheetPart.TableDefinitionParts
            .Single(candidate => candidate.Table?.Name?.Value == tableName)
            .Table!.Reference!.Value!;
    }

    private static SheetStateValues? ReadSheetState(string workbookPath, string sheetName)
    {
        using var document = SpreadsheetDocument.Open(workbookPath, false);
        return document.WorkbookPart!.Workbook!.Sheets!.Elements<Sheet>()
            .Single(candidate => candidate.Name?.Value == sheetName)
            .State?.Value;
    }

    private static string ReadWorksheetText(string workbookPath, string sheetName)
    {
        using var document = SpreadsheetDocument.Open(workbookPath, false);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook!.Sheets!.Elements<Sheet>()
            .Single(candidate => candidate.Name?.Value == sheetName);
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
        return worksheetPart.Worksheet!.InnerText;
    }

    private static string ReadSystemValue(
        string workbookPath,
        string sheetName,
        string firstColumnValue,
        int column)
    {
        using var document = SpreadsheetDocument.Open(workbookPath, false);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook!.Sheets!.Elements<Sheet>()
            .Single(candidate => candidate.Name?.Value == sheetName);
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
        var row = worksheetPart.Worksheet!.GetFirstChild<SheetData>()!.Elements<Row>()
            .Single(candidate => ReadCell(candidate.Elements<Cell>().FirstOrDefault()) == firstColumnValue);
        var cell = row.Elements<Cell>()
            .Single(candidate => ColumnNumber(candidate.CellReference?.Value) == column);
        return ReadCell(cell);
    }

    private static string ReadCell(Cell? cell) =>
        cell?.InlineString?.InnerText ?? cell?.CellValue?.InnerText ?? cell?.InnerText ?? string.Empty;

    [GeneratedRegex("^([A-Z]+)", RegexOptions.CultureInvariant)]
    private static partial Regex CellReferenceRegex();

    private sealed class WorkbookWorkspace : IDisposable
    {
        private readonly DirectoryInfo directory;

        public WorkbookWorkspace(string fixturePath, int year)
        {
            directory = Directory.CreateTempSubdirectory("lajur-workbook-store-");
            RootPath = directory.FullName;
            Key = new WorkbookKey(Guid.Parse("f76fd447-84ca-49cd-838a-8ae76bf94e63"), year);
            WorkbookPath = Path.Combine(
                RootPath,
                "users",
                Key.UserId.ToString("D"),
                "workbooks",
                $"Keuangan_Pribadi_{year}.xlsm");
            Directory.CreateDirectory(Path.GetDirectoryName(WorkbookPath)!);
            File.Copy(fixturePath, WorkbookPath);
        }

        public string RootPath { get; }

        public WorkbookKey Key { get; }

        public string WorkbookPath { get; }

        public void Dispose() => directory.Delete(recursive: true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
