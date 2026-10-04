using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Lajur.Infrastructure.Workbooks;

namespace Lajur.WorkbookTests;

public sealed class OpenXmlWorkbookDateHelperTests
{
    private static string FixturePath => Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "LajurWorkbookFixture.xlsm");

    [Fact]
    public void DetectWorkbookYear_AcceptsKnownRelativeHelperWithoutChangingSource()
    {
        using var workspace = new DateHelperWorkspace();
        WriteFormula(workspace.SourcePath, "Okt", "AG2", "$AD$1+AF2-1");
        var before = SHA256.HashData(File.ReadAllBytes(workspace.SourcePath));

        Assert.Equal(2026, new OpenXmlWorkbookReader().DetectWorkbookYear(workspace.SourcePath));

        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(workspace.SourcePath)));
    }

    [Fact]
    public async Task GenerateYearAsync_PreservesKnownRelativeHelperAndUpdatesMonthStart()
    {
        using var workspace = new DateHelperWorkspace();
        WriteFormula(workspace.SourcePath, "Okt", "AG2", "$AD$1+AF2-1");
        var key = new WorkbookKey(Guid.NewGuid(), 2027);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);

        await store.GenerateYearAsync(key, workspace.SourcePath,
            new WorkbookAuditContext(WorkbookChangeSource.System, null, "relative-date-generation"));

        var generated = workspace.WorkbookPath(key);
        Assert.Equal(2027, new OpenXmlWorkbookReader().DetectWorkbookYear(generated));
        Assert.Equal("$AD$1+AF2-1", ReadFormula(generated, "Okt", "AG2"));
        Assert.Contains("2027", ReadFormula(generated, "Okt", "AD1"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportWorkbookAsync_AcceptsMixedDateHelpersAndPreservesSourceAndPackage(bool shared)
    {
        using var workspace = new DateHelperWorkspace();
        foreach (var sheet in new[] { "Okt", "Nov", "Des" })
        {
            WriteDaySeries(workspace.SourcePath, sheet, relative: true, shared);
        }
        var before = SHA256.HashData(File.ReadAllBytes(workspace.SourcePath));
        var key = new WorkbookKey(Guid.NewGuid(), 2026);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);

        var result = await store.ImportWorkbookAsync(key, workspace.SourcePath, false,
            new WorkbookAuditContext(WorkbookChangeSource.ExcelImport, null, "relative-date-import"));

        var imported = workspace.WorkbookPath(key);
        var reader = new OpenXmlWorkbookReader();
        Assert.Equal(2026, reader.DetectWorkbookYear(imported));
        Assert.Equal(8, result.Migration.IndexedTransactionCount);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(workspace.SourcePath)));
        AssertUnrelatedPartsPreserved(workspace.SourcePath, imported);
        foreach (var sheet in MonthNames)
        {
            Assert.Equal(ReadRightSideCells(workspace.SourcePath, sheet), ReadRightSideCells(imported, sheet));
        }
    }

    [Theory]
    [InlineData(2027, false)]
    [InlineData(2028, true)]
    [InlineData(2028, false)]
    public async Task GenerateYearAsync_PatchesAllExplicitOrSharedDatesAndKeepsRelativeSeries(int year, bool shared)
    {
        using var workspace = new DateHelperWorkspace();
        for (var month = 1; month <= 12; month++)
        {
            // February deliberately uses the same relative contract to exercise leap years.
            WriteDaySeries(workspace.SourcePath, MonthNames[month - 1], relative: month == 2 || month >= 10, shared);
        }
        var before = SHA256.HashData(File.ReadAllBytes(workspace.SourcePath));
        var key = new WorkbookKey(Guid.NewGuid(), year);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);

        await store.GenerateYearAsync(key, workspace.SourcePath,
            new WorkbookAuditContext(WorkbookChangeSource.System, null, "date-series-generation"));

        var generated = workspace.WorkbookPath(key);
        var reader = new OpenXmlWorkbookReader();
        Assert.Equal(year, reader.DetectWorkbookYear(generated));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(workspace.SourcePath)));
        AssertUnrelatedPartsPreserved(workspace.SourcePath, generated, generated: true);
        using var sourceDocument = SpreadsheetDocument.Open(workspace.SourcePath, false);
        using var generatedDocument = SpreadsheetDocument.Open(generated, false);
        for (var month = 1; month <= 12; month++)
        {
            var sheet = MonthNames[month - 1];
            var relative = month == 2 || month >= 10;
            var sourceCells = GetWorksheetPart(sourceDocument, sheet).Worksheet!.Descendants<Cell>()
                .ToDictionary(cell => cell.CellReference!.Value!);
            var cells = GetWorksheetPart(generatedDocument, sheet).Worksheet!.Descendants<Cell>()
                .ToDictionary(cell => cell.CellReference!.Value!);
            Assert.Equal($"DATE({year},{month},1)", cells["AD1"].CellFormula!.Text);
            Assert.Null(cells["AD1"].CellValue);
            for (var row = 2; row <= 32; row++)
            {
                var cell = cells[$"AG{row}"];
                var expected = shared && row != 2 ? string.Empty
                    : relative ? $"$AD$1+AF{row}-1" : $"DATE({year},{month},AF{row})";
                Assert.Equal(expected, cell.CellFormula!.Text);
                Assert.Equal(sourceCells[$"AG{row}"].CellFormula!.SharedIndex?.Value, cell.CellFormula.SharedIndex?.Value);
                Assert.Equal(sourceCells[$"AG{row}"].CellFormula!.Reference?.Value, cell.CellFormula.Reference?.Value);
                Assert.Equal(sourceCells[$"AG{row}"].StyleIndex?.Value, cell.StyleIndex?.Value);
                Assert.Null(cell.CellValue);
            }
            var lastDay = DateTime.DaysInMonth(year, month);
            // Independent calendar oracle: both supported formulas use the AF day number.
            var dayNumber = int.Parse(cells[$"AF{lastDay + 1}"].CellValue!.Text, CultureInfo.InvariantCulture);
            Assert.Equal(lastDay, dayNumber);
            Assert.Equal(new DateOnly(year, month, lastDay), new DateOnly(year, month, 1).AddDays(dayNumber - 1));
            Assert.Empty(reader.ReadMonthlyTransactions(generated, month));
            Assert.Equal(ReadRightSideCells(workspace.SourcePath, sheet, excludeDateHelpers: true),
                ReadRightSideCells(generated, sheet, excludeDateHelpers: true));
        }
        Assert.Equal(year == 2028 ? 29 : 28, DateTime.DaysInMonth(year, 2));
    }

    [Theory]
    [InlineData("AG2", "$AD$2+AF2-1")]
    [InlineData("AG2", "$AD$1+AF3-1")]
    [InlineData("AG2", "$AD$1+AF2-2")]
    [InlineData("AG2", "Nov!$AD$1+AF2-1")]
    [InlineData("AG2", "IF(TRUE,DATE(2026,10,AF2),0)")]
    [InlineData("AG2", "DATE(2026,10,AF2)+1")]
    [InlineData("AG2", "DATE(2026,11,AF2)")]
    [InlineData("AG2", "DATE(2025,10,AF2)")]
    [InlineData("AG32", "$AD$1+AF31-1")]
    [InlineData("AG32", "DATE(2025,10,AF32)")]
    [InlineData("AD1", "DATE(2026,11,1)")]
    [InlineData("AD1", "DATE(2026,10,2)")]
    public async Task InvalidHelper_IsRejectedWithoutReplacingActiveOrGeneratingYear(string reference, string formula)
    {
        using var workspace = new DateHelperWorkspace();
        WriteDaySeries(workspace.SourcePath, "Okt", relative: true, shared: false);
        WriteFormula(workspace.SourcePath, "Okt", reference, formula);
        var before = SHA256.HashData(File.ReadAllBytes(workspace.SourcePath));
        var reader = new OpenXmlWorkbookReader();
        Assert.Throws<InvalidDataException>(() => reader.DetectWorkbookYear(workspace.SourcePath));
        var store = new OpenXmlWorkbookStore(workspace.RootPath);
        var existingKey = new WorkbookKey(Guid.NewGuid(), 2026);
        var active = workspace.WorkbookPath(existingKey);
        Directory.CreateDirectory(Path.GetDirectoryName(active)!);
        File.Copy(FixturePath, active);
        var activeBefore = File.ReadAllBytes(active);
        await Assert.ThrowsAsync<WorkbookImportValidationException>(() => store.ImportWorkbookAsync(
            existingKey, workspace.SourcePath, true,
            new WorkbookAuditContext(WorkbookChangeSource.ExcelImport, null, "invalid-helper-import")));
        Assert.Equal(activeBefore, File.ReadAllBytes(active));
        Assert.False(Directory.Exists(Path.Combine(workspace.RootPath, "backups")));

        var targetKey = new WorkbookKey(existingKey.UserId, 2028);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.GenerateYearAsync(
            targetKey, workspace.SourcePath,
            new WorkbookAuditContext(WorkbookChangeSource.System, null, "invalid-helper-generation")));
        Assert.False(File.Exists(workspace.WorkbookPath(targetKey)));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(active)!, ".lajur-*.tmp.xlsm"));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(workspace.SourcePath)));
    }

    [Theory]
    [InlineData("AG2:AH32", false)]
    [InlineData("AG2:AG33", false)]
    [InlineData("AG3:AG32", false)]
    [InlineData("AG2:AG32", true)]
    public void DetectWorkbookYear_RejectsOutOfBoundsOrBrokenSharedGroup(string range, bool wrongFollowerIndex)
    {
        using var workspace = new DateHelperWorkspace();
        WriteDaySeries(workspace.SourcePath, "Okt", relative: true, shared: true);
        using (var document = SpreadsheetDocument.Open(workspace.SourcePath, true))
        {
            var worksheet = GetWorksheetPart(document, "Okt").Worksheet!;
            var cells = worksheet.Descendants<Cell>().ToDictionary(cell => cell.CellReference!.Value!);
            cells["AG2"].CellFormula!.Reference = range;
            if (wrongFollowerIndex) cells["AG32"].CellFormula!.SharedIndex = 99999;
            worksheet.Save();
        }

        Assert.Throws<InvalidDataException>(() => new OpenXmlWorkbookReader().DetectWorkbookYear(workspace.SourcePath));
    }

    [Fact]
    public async Task SplitSharedGroupsAndFormulaFormatting_ArePreservedWhenGeneratingYear()
    {
        using var workspace = new DateHelperWorkspace();
        WriteDaySeries(workspace.SourcePath, "Okt", relative: true, shared: false);
        using (var document = SpreadsheetDocument.Open(workspace.SourcePath, true))
        {
            var worksheet = GetWorksheetPart(document, "Okt").Worksheet!;
            var cells = worksheet.Descendants<Cell>().ToDictionary(cell => cell.CellReference!.Value!);
            foreach (var (first, last, index) in new[] { (2, 12, 9876U), (13, 31, 9877U) })
            {
                for (var row = first; row <= last; row++)
                {
                    cells[$"AG{row}"].CellFormula = new CellFormula(row == first ? $" $ad$1 + af{row} - 1 " : "")
                        { FormulaType = CellFormulaValues.Shared, SharedIndex = index };
                    if (row == first) cells[$"AG{row}"].CellFormula!.Reference = $"AG{first}:AG{last}";
                }
            }
            cells["AD1"].CellFormula!.Text = " date( 2026 , 10 , 1 ) ";
            cells["AG32"].CellFormula!.Text = " $ad$1 + af32 - 1 ";
            worksheet.Save();
        }
        Assert.Equal(2026, new OpenXmlWorkbookReader().DetectWorkbookYear(workspace.SourcePath));
        var key = new WorkbookKey(Guid.NewGuid(), 2028);
        await new OpenXmlWorkbookStore(workspace.RootPath).GenerateYearAsync(key, workspace.SourcePath,
            new WorkbookAuditContext(WorkbookChangeSource.System, null, "split-helper-generation"));
        var generated = workspace.WorkbookPath(key);
        Assert.Equal(2028, new OpenXmlWorkbookReader().DetectWorkbookYear(generated));
        Assert.Equal(" date( 2028 , 10 , 1 ) ", ReadFormula(generated, "Okt", "AD1"));
        foreach (var row in new[] { 2, 13, 32 })
        {
            Assert.Equal(ReadFormula(workspace.SourcePath, "Okt", $"AG{row}"), ReadFormula(generated, "Okt", $"AG{row}"));
        }
    }

    [Theory]
    [InlineData("AG32", true)]
    [InlineData("AG32", false)]
    [InlineData("AD1", false)]
    [InlineData("AG2", false)]
    public void DetectWorkbookYear_RejectsMissingHelperCellOrFormula(string reference, bool removeCell)
    {
        using var workspace = new DateHelperWorkspace();
        using (var document = SpreadsheetDocument.Open(workspace.SourcePath, true))
        {
            var worksheet = GetWorksheetPart(document, "Okt").Worksheet!;
            var cell = worksheet.Descendants<Cell>().Single(cell => cell.CellReference?.Value == reference);
            if (removeCell) cell.Remove();
            else cell.CellFormula = null;
            worksheet.Save();
        }
        Assert.Throws<InvalidDataException>(() => new OpenXmlWorkbookReader().DetectWorkbookYear(workspace.SourcePath));
    }

    private static readonly string[] MonthNames =
        ["Jan", "Feb", "Mar", "April", "May", "June", "July", "Aug", "Sep", "Okt", "Nov", "Des"];

    private static void WriteDaySeries(string path, string sheetName, bool relative, bool shared)
    {
        using var document = SpreadsheetDocument.Open(path, true);
        var worksheet = GetWorksheetPart(document, sheetName).Worksheet!;
        var cells = worksheet.Descendants<Cell>().ToDictionary(cell => cell.CellReference!.Value!);
        var month = Array.IndexOf(MonthNames, sheetName) + 1;
        cells["AD1"].CellFormula = new CellFormula($"DATE(2026,{month},1)");
        for (var row = 2; row <= 32; row++)
        {
            var cell = cells[$"AG{row}"];
            var text = relative ? $"$AD$1+AF{row}-1" : $"DATE(2026,{month},AF{row})";
            cell.CellFormula = new CellFormula(shared && row != 2 ? string.Empty : text);
            if (shared)
            {
                cell.CellFormula.FormulaType = CellFormulaValues.Shared;
                cell.CellFormula.SharedIndex = 9876;
                if (row == 2) cell.CellFormula.Reference = "AG2:AG32";
            }
            // Stale caches must disappear on generation, including relative/follower cells.
            cell.CellValue = new CellValue("46000");
        }
        worksheet.Save();
    }

    private static string[] ReadRightSideCells(string path, string sheetName, bool excludeDateHelpers = false)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        return GetWorksheetPart(document, sheetName).Worksheet!.Descendants<Cell>()
            .Where(cell => cell.CellReference!.Value![0] is < 'A' or > 'F'
                || cell.CellReference.Value.Length > 2 && char.IsLetter(cell.CellReference.Value[1]))
            .Where(cell => !excludeDateHelpers || cell.CellReference!.Value != "AD1"
                && !Enumerable.Range(2, 31).Any(row => cell.CellReference.Value == $"AG{row}"))
            .Select(cell => cell.OuterXml).ToArray();
    }

    private static void AssertUnrelatedPartsPreserved(string source, string target, bool generated = false)
    {
        var changedParts = new HashSet<string>(StringComparer.Ordinal)
            { "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "[Content_Types].xml" };
        if (generated)
        {
            using var document = SpreadsheetDocument.Open(source, false);
            foreach (var sheet in MonthNames.Append("Rekap"))
            {
                var worksheet = GetWorksheetPart(document, sheet);
                changedParts.Add(worksheet.Uri.OriginalString.TrimStart('/'));
                foreach (var table in worksheet.TableDefinitionParts)
                {
                    changedParts.Add(table.Uri.OriginalString.TrimStart('/'));
                }
            }
        }
        using var sourceZip = ZipFile.OpenRead(source);
        using var targetZip = ZipFile.OpenRead(target);
        foreach (var entry in sourceZip.Entries.Where(entry => !changedParts.Contains(entry.FullName)))
        {
            using var before = entry.Open();
            var targetEntry = targetZip.GetEntry(entry.FullName);
            Assert.NotNull(targetEntry);
            using var after = targetEntry.Open();
            Assert.True(SHA256.HashData(before).SequenceEqual(SHA256.HashData(after)),
                $"Unrelated package part changed: {entry.FullName}");
        }
    }

    private static void WriteFormula(string path, string sheetName, string reference, string formula)
    {
        using var document = SpreadsheetDocument.Open(path, true);
        var part = GetWorksheetPart(document, sheetName);
        var cell = part.Worksheet!.Descendants<Cell>()
            .Single(candidate => candidate.CellReference?.Value == reference);
        cell.CellFormula ??= new CellFormula();
        cell.CellFormula.Text = formula;
        cell.CellValue = null;
        part.Worksheet.Save();
    }

    private static string ReadFormula(string path, string sheetName, string reference)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        return GetWorksheetPart(document, sheetName).Worksheet!.Descendants<Cell>()
            .Single(cell => cell.CellReference?.Value == reference).CellFormula!.Text;
    }

    private static WorksheetPart GetWorksheetPart(SpreadsheetDocument document, string sheetName)
    {
        var workbook = document.WorkbookPart!;
        var sheet = workbook.Workbook!.Sheets!.Elements<Sheet>()
            .Single(sheet => sheet.Name?.Value == sheetName);
        return (WorksheetPart)workbook.GetPartById(sheet.Id!.Value!);
    }

    private sealed class DateHelperWorkspace : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lajur-date-helper-");

        public DateHelperWorkspace() => File.Copy(FixturePath, SourcePath);

        public string RootPath => directory.FullName;
        public string SourcePath => Path.Combine(RootPath, "relative-template.xlsm");

        public string WorkbookPath(WorkbookKey key) => Path.Combine(
            RootPath, "users", key.UserId.ToString("D"), "workbooks", $"Keuangan_Pribadi_{key.Year}.xlsm");

        public void Dispose() => directory.Delete(recursive: true);
    }
}
