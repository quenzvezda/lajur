using System.IO.Compression;
using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Lajur.Infrastructure.Workbooks;

namespace Lajur.WorkbookTests;

public sealed class OpenXmlWorkbookLifecycleTests
{
    private static string FixturePath => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "LajurWorkbookFixture.xlsm");

    [Fact]
    public async Task GenerateYearAsync_CreatesCleanTargetYearAndPreservesVba()
    {
        using var workspace = new LifecycleWorkspace();
        var key = new WorkbookKey(Guid.NewGuid(), 2027);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);
        var vbaBefore = ReadZipEntryHash(FixturePath, "xl/vbaProject.bin");

        var result = await store.GenerateYearAsync(
            key,
            FixturePath,
            new WorkbookAuditContext(WorkbookChangeSource.System, null, "generate-2027"));

        var path = workspace.WorkbookPath(key);
        Assert.True(File.Exists(path));
        Assert.Equal(0, result.IndexedTransactionCount);
        Assert.Equal(2027, new OpenXmlWorkbookReader().DetectWorkbookYear(path));
        Assert.Equal(vbaBefore, ReadZipEntryHash(path, "xl/vbaProject.bin"));
        Assert.All(
            Enumerable.Range(1, 12),
            month => Assert.Empty(new OpenXmlWorkbookReader().ReadMonthlyTransactions(path, month)));
        Assert.Contains("SYSTEM_MIGRATION", ReadSheetText(path, "__AuditLog"));
        Assert.Equal(SheetStateValues.VeryHidden, ReadSheetState(path, "__AppIndex"));
        Assert.Equal(SheetStateValues.VeryHidden, ReadSheetState(path, "__AuditLog"));

        await Assert.ThrowsAsync<WorkbookAlreadyExistsException>(() => store.GenerateYearAsync(
            key,
            FixturePath,
            new WorkbookAuditContext(WorkbookChangeSource.System, null, "duplicate-generate")));
    }

    [Fact]
    public async Task ImportWorkbookAsync_MigratesEveryLegacyRowWithOneSystemAudit()
    {
        using var workspace = new LifecycleWorkspace();
        var key = new WorkbookKey(Guid.NewGuid(), 2026);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);
        var reader = new OpenXmlWorkbookReader();
        var legacyRowCount = Enumerable.Range(1, 12)
            .Sum(month => reader.ReadMonthlyTransactions(FixturePath, month).Count);
        var vbaBefore = ReadZipEntryHash(FixturePath, "xl/vbaProject.bin");

        var imported = await store.ImportWorkbookAsync(
            key,
            FixturePath,
            replaceExisting: false,
            new WorkbookAuditContext(WorkbookChangeSource.ExcelImport, "device", "legacy-import"));

        var activePath = workspace.WorkbookPath(key);
        Assert.False(imported.ReplacedExisting);
        Assert.Equal(legacyRowCount, imported.Migration.IndexedTransactionCount);
        Assert.Equal(vbaBefore, ReadZipEntryHash(activePath, "xl/vbaProject.bin"));
        Assert.Equal(
            legacyRowCount,
            Enumerable.Range(1, 12).Sum(month =>
                reader.ReadApplicationTransactions(activePath, 2026, month).Count));
        var audit = ReadSheetText(activePath, "__AuditLog");
        Assert.Equal(1, CountOccurrences(audit, "SYSTEM_MIGRATION"));
        Assert.DoesNotContain("CREATE", audit);
    }

    [Fact]
    public async Task FailedImport_LeavesActiveWorkbookAndBackupsUnchanged()
    {
        using var workspace = new LifecycleWorkspace();
        var key = new WorkbookKey(Guid.NewGuid(), 2026);
        var activePath = workspace.SeedActive(key, FixturePath);
        var before = SHA256.HashData(await File.ReadAllBytesAsync(activePath));
        var invalidPath = Path.Combine(workspace.RootPath, "invalid.xlsm");
        await File.WriteAllTextAsync(invalidPath, "not a workbook");
        var store = new OpenXmlWorkbookStore(workspace.RootPath);

        await Assert.ThrowsAsync<WorkbookImportValidationException>(() => store.ImportWorkbookAsync(
            key,
            invalidPath,
            replaceExisting: true,
            new WorkbookAuditContext(WorkbookChangeSource.ExcelImport, "device", "invalid-import")));

        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(activePath)));
        var backupRoot = Path.Combine(workspace.RootPath, "backups");
        Assert.False(Directory.Exists(backupRoot));
    }

    [Fact]
    public async Task Replacement_CreatesProtectedBackupBeforePromotion()
    {
        using var workspace = new LifecycleWorkspace();
        var key = new WorkbookKey(Guid.NewGuid(), 2026);
        var activePath = workspace.SeedActive(key, FixturePath);
        var original = await File.ReadAllBytesAsync(activePath);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);

        var imported = await store.ImportWorkbookAsync(
            key,
            FixturePath,
            replaceExisting: true,
            new WorkbookAuditContext(WorkbookChangeSource.ExcelImport, "device", "replacement"));

        Assert.True(imported.ReplacedExisting);
        Assert.NotNull(imported.BackupFileName);
        var backupPath = Path.Combine(
            workspace.RootPath,
            "backups",
            key.UserId.ToString("D"),
            key.Year.ToString(),
            imported.BackupFileName!);
        Assert.True(File.Exists(backupPath));
        Assert.Equal(original, await File.ReadAllBytesAsync(backupPath));
    }

    [Fact]
    public async Task Reimport_ReconcilesExternallyEditedRowAndKeepsTransactionIdentity()
    {
        using var workspace = new LifecycleWorkspace();
        var key = new WorkbookKey(Guid.NewGuid(), 2026);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);
        var audit = new WorkbookAuditContext(WorkbookChangeSource.ExcelImport, "device", "initial-import");
        await store.ImportWorkbookAsync(key, FixturePath, replaceExisting: false, audit);
        var activePath = workspace.WorkbookPath(key);
        var reader = new OpenXmlWorkbookReader();
        var before = reader.ReadApplicationTransactions(activePath, 2026, 1).First();
        var editedPath = Path.Combine(workspace.RootPath, "edited.xlsm");
        File.Copy(activePath, editedPath);
        WriteDescription(editedPath, "Jan", 6, "Edited in desktop Excel");

        var result = await store.ImportWorkbookAsync(
            key,
            editedPath,
            replaceExisting: true,
            new WorkbookAuditContext(WorkbookChangeSource.ExcelImport, "device", "edited-import"));

        Assert.Equal(1, result.Migration.UpdatedTransactionCount);
        var after = reader.ReadApplicationTransactions(activePath, 2026, 1)
            .Single(transaction => transaction.TransactionId == before.TransactionId);
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Equal("Edited in desktop Excel", after.Description);
    }

    [Fact]
    public async Task Import_BlankPaymentIsValidAndExternalPaymentEditKeepsIdentity()
    {
        using var workspace = new LifecycleWorkspace();
        var source = Path.Combine(workspace.RootPath, "optional-payment.xlsm");
        File.Copy(FixturePath, source);
        WriteText(source, "Jan", "C6", "");
        var sourceBefore = SHA256.HashData(File.ReadAllBytes(source));
        var reader = new OpenXmlWorkbookReader();
        Assert.Equal("", reader.ReadMonthlyTransactions(source, 1)[0].PaymentMethod);
        var key = new WorkbookKey(Guid.NewGuid(), 2026);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);
        var audit = new WorkbookAuditContext(WorkbookChangeSource.ExcelImport, "device", "optional-payment-import");
        await store.ImportWorkbookAsync(key, source, false, audit);
        var active = workspace.WorkbookPath(key);
        var before = reader.ReadApplicationTransactions(active, 2026, 1).First();
        Assert.Equal("", before.PaymentMethod);
        Assert.Equal(sourceBefore, SHA256.HashData(File.ReadAllBytes(source)));
        Assert.Equal(ReadZipEntryHash(source, "xl/vbaProject.bin"), ReadZipEntryHash(active, "xl/vbaProject.bin"));
        var edited = Path.Combine(workspace.RootPath, "filled-payment.xlsm");
        File.Copy(active, edited);
        WriteText(edited, "Jan", "C6", "Bank Uji");
        var result = await store.ImportWorkbookAsync(key, edited, true, audit);
        Assert.Equal(1, result.Migration.UpdatedTransactionCount);
        var after = reader.ReadApplicationTransactions(active, 2026, 1);
        Assert.Equal(3, after.Count);
        var sameTransaction = Assert.Single(after, row => row.TransactionId == before.TransactionId);
        Assert.Equal(before.Revision + 1, sameTransaction.Revision);
        Assert.Equal("Bank Uji", sameTransaction.PaymentMethod);
        Assert.Equal(before.AmountIdr, sameTransaction.AmountIdr);
    }

    [Theory]
    [InlineData("A6")]
    [InlineData("B6")]
    [InlineData("D6")]
    [InlineData("E6")]
    public async Task Import_StillRejectsOtherMissingRequiredFields(string reference)
    {
        using var workspace = new LifecycleWorkspace();
        var source = Path.Combine(workspace.RootPath, "invalid-row.xlsm");
        File.Copy(FixturePath, source);
        WriteText(source, "Jan", reference, "");
        var key = new WorkbookKey(Guid.NewGuid(), 2026);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);
        await Assert.ThrowsAsync<WorkbookImportValidationException>(() => store.ImportWorkbookAsync(
            key, source, false, new WorkbookAuditContext(WorkbookChangeSource.ExcelImport, null, "required-field")));
        Assert.False(File.Exists(workspace.WorkbookPath(key)));
    }

    [Fact]
    public async Task Import_ReportsEveryInvalidRowWithLocationInsteadOfOnlyTheFirst()
    {
        using var workspace = new LifecycleWorkspace();
        var source = Path.Combine(workspace.RootPath, "many-invalid-rows.xlsm");
        File.Copy(FixturePath, source);
        WriteText(source, "Jan", "C6", "Metode Hilang");
        WriteText(source, "Jan", "D7", "Kategori Uji 03");
        WriteText(source, "Jan", "E8", "abc");
        WriteText(source, "Jan", "B8", "Pengeluaran ");
        var sourceBefore = SHA256.HashData(File.ReadAllBytes(source));
        var key = new WorkbookKey(Guid.NewGuid(), 2026);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);

        var exception = await Assert.ThrowsAsync<WorkbookImportValidationException>(() => store.ImportWorkbookAsync(
            key, source, false, new WorkbookAuditContext(WorkbookChangeSource.ExcelImport, null, "many-invalid")));

        Assert.Collection(
            exception.Issues.OrderBy(issue => issue.Row).ThenBy(issue => issue.Column),
            issue => Assert.Equal(("Jan", 6u, "C"), (issue.Sheet, issue.Row, issue.Column)),
            issue =>
            {
                Assert.Equal(("Jan", 7u, "D"), (issue.Sheet, issue.Row, issue.Column));
                Assert.Contains("terdaftar sebagai Pengeluaran di Master, tetapi baris ini bertipe Pemasukan", issue.Message, StringComparison.Ordinal);
            },
            issue =>
            {
                Assert.Equal(("Jan", 8u, "B"), (issue.Sheet, issue.Row, issue.Column));
                Assert.Contains("spasi berlebih", issue.Message, StringComparison.Ordinal);
            },
            issue => Assert.Equal(("Jan", 8u, "E"), (issue.Sheet, issue.Row, issue.Column)));
        Assert.Contains("3 baris perlu diperbaiki", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Jan baris 7 (kolom D)", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(workspace.WorkbookPath(key)));
        Assert.Equal(sourceBefore, SHA256.HashData(File.ReadAllBytes(source)));
    }

    private static byte[] ReadZipEntryHash(string workbookPath, string entryName)
    {
        using var stream = File.OpenRead(workbookPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entryStream = archive.GetEntry(entryName)!.Open();
        return SHA256.HashData(entryStream);
    }

    private static SheetStateValues? ReadSheetState(string workbookPath, string sheetName)
    {
        using var document = SpreadsheetDocument.Open(workbookPath, false);
        return document.WorkbookPart!.Workbook!.Sheets!.Elements<Sheet>()
            .Single(sheet => sheet.Name?.Value == sheetName)
            .State?.Value;
    }

    private static string ReadSheetText(string workbookPath, string sheetName)
    {
        using var document = SpreadsheetDocument.Open(workbookPath, false);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook!.Sheets!.Elements<Sheet>()
            .Single(candidate => candidate.Name?.Value == sheetName);
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
        return worksheetPart.Worksheet!.InnerText;
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(search, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += search.Length;
        }

        return count;
    }

    private static void WriteDescription(string path, string sheetName, uint rowNumber, string description)
        => WriteText(path, sheetName, $"F{rowNumber}", description);

    private static void WriteText(string path, string sheetName, string reference, string value)
    {
        using var document = SpreadsheetDocument.Open(path, true);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook!.Sheets!.Elements<Sheet>()
            .Single(candidate => candidate.Name?.Value == sheetName);
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
        var cell = worksheetPart.Worksheet!.Descendants<Cell>()
            .Single(candidate => candidate.CellReference?.Value == reference);
        cell.CellValue = null;
        cell.DataType = CellValues.InlineString;
        cell.InlineString = new InlineString(new Text(value));
        worksheetPart.Worksheet.Save();
    }

    private sealed class LifecycleWorkspace : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lajur-lifecycle-");

        public string RootPath => directory.FullName;

        public string WorkbookPath(WorkbookKey key) => Path.Combine(
            RootPath,
            "users",
            key.UserId.ToString("D"),
            "workbooks",
            $"Keuangan_Pribadi_{key.Year}.xlsm");

        public string SeedActive(WorkbookKey key, string source)
        {
            var path = WorkbookPath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(source, path);
            return path;
        }

        public void Dispose() => directory.Delete(recursive: true);
    }
}
