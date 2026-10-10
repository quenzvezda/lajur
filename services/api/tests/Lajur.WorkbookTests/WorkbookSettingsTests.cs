using System.IO.Compression;
using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Lajur.Application.Finance;
using Lajur.Infrastructure.Workbooks;

namespace Lajur.WorkbookTests;

public sealed class WorkbookSettingsTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("lajur-settings-").FullName;
    private readonly WorkbookKey key = new(Guid.NewGuid(), 2026);
    private readonly string path;
    private readonly OpenXmlWorkbookStore store;
    private static readonly WorkbookAuditContext Audit = new(WorkbookChangeSource.Web, null, "settings-test");

    public WorkbookSettingsTests()
    {
        path = Path.Combine(root, "fixture.xlsm");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LajurWorkbookFixture.xlsm"), path);
        store = new(_ => path, backupDirectoryResolver: _ => Path.Combine(root, "backups"));
    }
    public void Dispose() => Directory.Delete(root, true);
    private static WorksheetPart Sheet(SpreadsheetDocument document, string name)
        => (WorksheetPart)document.WorkbookPart!.GetPartById(document.WorkbookPart.Workbook!.Sheets!.Elements<Sheet>().Single(s => s.Name == name).Id!);
    private static Dictionary<string, byte[]> Parts(string file)
    {
        using var zip = ZipFile.OpenRead(file);
        return zip.Entries.ToDictionary(e => e.FullName, e => { using var stream = e.Open(); using var result = new MemoryStream(); stream.CopyTo(result); return result.ToArray(); });
    }
    private static Dictionary<string, string> Cells(string file, string sheet)
    {
        using var document = SpreadsheetDocument.Open(file, false);
        return Sheet(document, sheet).Worksheet!.GetFirstChild<SheetData>()!.Descendants<Cell>().ToDictionary(c => c.CellReference!.Value!, c => c.OuterXml);
    }

    [Fact]
    public async Task Budget_batch_is_exact_atomic_revisioned_and_preserves_unrelated_parts()
    {
        var initialHash = SHA256.HashData(File.ReadAllBytes(path));
        var settings = await store.ReadBudgetSettingsAsync(key);
        Assert.Equal(initialHash, SHA256.HashData(File.ReadAllBytes(path)));
        var beforeParts = Parts(path);
        var beforeCells = Cells(path, "Master");
        var oldYear = await store.ReadYearAsync(key);
        var first = settings.Categories[0]; var second = settings.Categories[1];
        var command = new SaveBudgetSettings(Guid.NewGuid(), settings.Version, [new(first.Category, "9007199254740993"), new(second.Category, "0")]);
        var saved = await store.SaveBudgetSettingsAsync(key, command, Audit);
        Assert.Equal(initialHash, SHA256.HashData(File.ReadAllBytes(Assert.Single(Directory.GetFiles(Path.Combine(root, "backups"))))));
        Assert.Equal("9007199254740993", saved.Categories[0].AmountIdr);
        Assert.Equal("0", saved.Categories[1].AmountIdr);
        Assert.NotEqual(settings.Version, saved.Version);
        Assert.NotSame(oldYear, await store.ReadYearAsync(key));
        foreach (var (name, bytes) in beforeParts)
            if (name != "xl/workbook.xml" && name != "xl/worksheets/sheet14.xml" && name != "[Content_Types].xml" && !name.Contains("_rels/workbook")) Assert.Equal(bytes, Parts(path)[name]);
        var afterCells = Cells(path, "Master");
        foreach (var (reference, xml) in beforeCells) if (reference is not "C5" and not "C6") Assert.Equal(xml, afterCells[reference]);
        Assert.NotNull(await store.ReadSettingsOperationAsync(key, command.OperationId));
        var savedHash = SHA256.HashData(File.ReadAllBytes(path));
        var savedAt = File.GetLastWriteTimeUtc(path);
        await store.SaveBudgetSettingsAsync(key, command, Audit);
        Assert.Single(Directory.GetFiles(Path.Combine(root, "backups")));
        Assert.Equal(savedHash, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal(savedAt, File.GetLastWriteTimeUtc(path));
        await Assert.ThrowsAsync<WorkbookSettingsConflictException>(() => store.SaveBudgetSettingsAsync(key, command with { OperationId = Guid.NewGuid() }, Audit));
        await Assert.ThrowsAsync<WorkbookSettingsConflictException>(() => store.SaveBudgetSettingsAsync(key, command with { Changes = [new(first.Category, "1")] }, Audit));
        var cleared = await store.SaveBudgetSettingsAsync(key, new(Guid.NewGuid(), saved.Version, [new(first.Category, null)]), Audit);
        Assert.Null(cleared.Categories[0].AmountIdr);
        Assert.Equal("0", cleared.Categories[1].AmountIdr);
    }

    [Fact]
    public async Task Projection_changes_only_literal_inputs_and_refreshes_estimates_without_creating_income()
    {
        var settings = await store.ReadProjectionSettingsAsync(key, 1);
        var febVersion = (await store.ReadProjectionSettingsAsync(key, 2)).Version;
        Assert.Equal(3, settings.Sources.Count); Assert.All(settings.Sources, s => Assert.True(s.Editable));
        var before = Cells(path, "Jan");
        var beforeParts = Parts(path);
        var year = await store.ReadYearAsync(key);
        var meal = settings.Sources.Single(s => s.IsMealAllowance);
        var changes = settings.Sources.Select(s => s.IsMealAllowance ? new ProjectionChange(s.Id, 12, null, 22, "35000") : new ProjectionChange(s.Id, 25, "6000000", null, null)).ToArray();
        var command = new SaveProjectionSettings(Guid.NewGuid(), settings.Version, changes);
        var saved = await store.SaveProjectionSettingsAsync(key, 1, command, Audit);
        Assert.Equal("770000", saved.Sources.Single(s => s.IsMealAllowance).EstimateIdr);
        Assert.Equal("35000", saved.Sources.Single(s => s.IsMealAllowance).DailyRateIdr);
        var after = Cells(path, "Jan");
        foreach (var (name, bytes) in beforeParts)
            if (name != "xl/workbook.xml" && name != "xl/worksheets/sheet2.xml" && name != "[Content_Types].xml" && !name.Contains("_rels/workbook")) Assert.Equal(bytes, Parts(path)[name]);
        foreach (var (reference, xml) in before) if (reference is not "L3" and not "L4" and not "L5" and not "M4" and not "M5" and not "M10" and not "M11") Assert.Equal(xml, after[reference]);
        Assert.Equal(year.Months[0], (await store.ReadYearAsync(key)).Months[0]);
        Assert.NotEqual(settings.Version, saved.Version);
        var replay = await store.SaveProjectionSettingsAsync(key, 1, command, Audit);
        Assert.Equal(saved.Version, replay.Version);
        Assert.Equal(febVersion, (await store.ReadProjectionSettingsAsync(key, 2)).Version);
        Assert.Equal(5_000_000, (await store.ReadYearAsync(key)).IncomeSources[1]!.Single(s => s.IncomeCategory == "Gaji").EstimateIdr);
        using var document = SpreadsheetDocument.Open(path, false);
        var audit = Sheet(document, "__AuditLog").Worksheet!.GetFirstChild<SheetData>()!.Elements<Row>().Skip(1).ToArray();
        Assert.Single(audit); Assert.Equal("SETTINGS_PROJECTION_1", audit[0].Elements<Cell>().Single(c => c.CellReference == "C2").InnerText);
        Assert.Equal("WEB", audit[0].Elements<Cell>().Single(c => c.CellReference == "D2").InnerText);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("9223372036854775808")]
    public async Task Invalid_budget_batch_leaves_entire_active_file_unchanged(string amount)
    {
        var settings = await store.ReadBudgetSettingsAsync(key);
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveBudgetSettingsAsync(key, new(Guid.NewGuid(), settings.Version,
            [new(settings.Categories[0].Category, "100"), new(settings.Categories[1].Category, amount)]), Audit));
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Fact]
    public async Task Formula_backed_inputs_are_readonly_and_unknown_formula_is_never_overwritten()
    {
        using (var document = SpreadsheetDocument.Open(path, true))
        {
            Sheet(document, "Master").Worksheet!.Descendants<Cell>().Single(c => c.CellReference == "C5").CellFormula = new CellFormula("SUM(C6:C7)");
            Sheet(document, "Jan").Worksheet!.Descendants<Cell>().Single(c => c.CellReference == "M3").CellFormula = new CellFormula("$M$10+$M$11");
        }
        var budget = await store.ReadBudgetSettingsAsync(key); Assert.False(budget.Categories[0].Editable);
        var projection = await store.ReadProjectionSettingsAsync(key, 1); Assert.False(projection.Sources[0].Editable);
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveBudgetSettingsAsync(key, new(Guid.NewGuid(), budget.Version, [new(budget.Categories[0].Category, "100")]), Audit));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveProjectionSettingsAsync(key, 1, new(Guid.NewGuid(), projection.Version, [new(projection.Sources[0].Id, 10, "100", null, null)]), Audit));
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Fact]
    public async Task Concurrent_changes_conflict_and_reverted_values_do_not_reuse_old_version()
    {
        var initial = await store.ReadBudgetSettingsAsync(key);
        var category = initial.Categories[0];
        var commands = new[] { "100", "200" }.Select(value => new SaveBudgetSettings(Guid.NewGuid(), initial.Version, [new(category.Category, value)]));
        var results = await Task.WhenAll(commands.Select(async command => { try { await store.SaveBudgetSettingsAsync(key, command, Audit); return true; } catch (WorkbookSettingsConflictException) { return false; } }));
        Assert.Single(results, r => r);
        var current = await store.ReadBudgetSettingsAsync(key);
        var reverted = await store.SaveBudgetSettingsAsync(key, new(Guid.NewGuid(), current.Version, [new(category.Category, category.AmountIdr)]), Audit);
        Assert.NotEqual(initial.Version, reverted.Version);
    }

    [Theory]
    [InlineData(32, "35000")]
    [InlineData(-1, "35000")]
    [InlineData(31, "9223372036854775807")]
    public async Task Invalid_meal_days_or_overflow_roll_back_every_projection_change(int days, string rate)
    {
        var snapshot = await store.ReadProjectionSettingsAsync(key, 1);
        var meal = snapshot.Sources.Single(s => s.IsMealAllowance);
        var salary = snapshot.Sources.First(s => !s.IsMealAllowance);
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveProjectionSettingsAsync(key, 1,
            new(Guid.NewGuid(), snapshot.Version, [new(salary.Id, 25, "7000000", null, null), new(meal.Id, 10, null, days, rate)]), Audit));
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.False(Directory.Exists(Path.Combine(root, "backups")));
    }

    [Fact]
    public async Task Settings_scope_versions_do_not_conflict_with_unrelated_transaction_writes()
    {
        var budget = await store.ReadBudgetSettingsAsync(key);
        var projection = await store.ReadProjectionSettingsAsync(key, 1);
        await store.CreateTransactionAsync(key, new(Guid.NewGuid(), new(new DateOnly(2026, 10, 10), "Pengeluaran", "", "Kategori Uji 06", 100, "Synthetic"), Audit));
        Assert.Equal(budget.Version, (await store.ReadBudgetSettingsAsync(key)).Version);
        Assert.Equal(projection.Version, (await store.ReadProjectionSettingsAsync(key, 1)).Version);
    }
}
