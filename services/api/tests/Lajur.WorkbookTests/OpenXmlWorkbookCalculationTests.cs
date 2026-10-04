using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Lajur.Infrastructure.Workbooks;

namespace Lajur.WorkbookTests;

public sealed class OpenXmlWorkbookCalculationTests
{
    private static string FixturePath => Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "LajurWorkbookFixture.xlsm");

    [Theory]
    [InlineData(2027, true)]
    [InlineData(2028, false)]
    public async Task GenerateYear_RequestsFullCalculationOnLoadWithoutPersistentForcedMode(int year, bool forced)
    {
        using var workspace = new CalculationWorkspace(forced);
        var sourceHash = SHA256.HashData(File.ReadAllBytes(workspace.SourcePath));
        var key = new WorkbookKey(Guid.NewGuid(), year);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);

        await store.GenerateYearAsync(key, workspace.SourcePath, Audit);

        var exported = await store.ExportWorkbookAsync(key);
        var path = Path.Combine(workspace.RootPath, "exported.xlsm");
        await File.WriteAllBytesAsync(path, exported);
        AssertCalculationPolicy(path);
        Assert.Equal(year, new OpenXmlWorkbookReader().DetectWorkbookYear(path));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(workspace.SourcePath)));
        Assert.Equal(ReadVbaHash(workspace.SourcePath), ReadVbaHash(path));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Import_RequestsFullCalculationOnLoadAndOverridesLegacyForcedMode(bool forced)
    {
        using var workspace = new CalculationWorkspace(forced);
        var sourceHash = SHA256.HashData(File.ReadAllBytes(workspace.SourcePath));
        var key = new WorkbookKey(Guid.NewGuid(), 2026);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);

        await store.ImportWorkbookAsync(key, workspace.SourcePath, false, Audit);

        var exported = await store.ExportWorkbookAsync(key);
        var path = Path.Combine(workspace.RootPath, "imported.xlsm");
        await File.WriteAllBytesAsync(path, exported);
        AssertCalculationPolicy(path);
        var reader = new OpenXmlWorkbookReader();
        Assert.Equal(8, Enumerable.Range(1, 12).Sum(month => reader.ReadApplicationTransactions(path, 2026, month).Count));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(workspace.SourcePath)));
        Assert.Equal(ReadVbaHash(workspace.SourcePath), ReadVbaHash(path));
    }

    [Fact]
    public async Task SuccessfulCrudSaves_RequestOneTimeFullCalculation_AndCreateRetryDoesNotWrite()
    {
        using var workspace = new CalculationWorkspace(forced: true);
        var key = new WorkbookKey(Guid.NewGuid(), 2026);
        var path = Path.Combine(workspace.RootPath, "users", key.UserId.ToString("D"),
            "workbooks", "Keuangan_Pribadi_2026.xlsm");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(workspace.SourcePath, path);
        var vba = ReadVbaHash(path);
        var store = new OpenXmlWorkbookStore(workspace.RootPath);
        var id = Guid.NewGuid();
        var draft = new WorkbookTransactionDraft(new DateOnly(2026, 3, 12),
            "Pengeluaran", "", "Kategori Uji 10", 12_345, "Synthetic calculation regression");
        var command = new CreateWorkbookTransactionCommand(id, draft, Audit);

        var created = await store.CreateTransactionAsync(key, command);
        AssertCalculationPolicy(path);
        Assert.Equal(vba, ReadVbaHash(path));
        var beforeRetry = SHA256.HashData(File.ReadAllBytes(path));
        Assert.Equal(created, await store.CreateTransactionAsync(key, command));
        Assert.Equal(beforeRetry, SHA256.HashData(File.ReadAllBytes(path)));

        // Simulate an externally supplied legacy/manual setting before each
        // ordinary successful write. Never modify the committed fixture.
        SetLegacyCalculationPolicy(path, forced: true);
        var updated = await store.UpdateTransactionAsync(key,
            new UpdateWorkbookTransactionCommand(id, 1, draft with { AmountIdr = 23_456 }, Audit));
        Assert.Equal(2, updated.Revision);
        AssertCalculationPolicy(path);
        Assert.Equal(vba, ReadVbaHash(path));

        SetLegacyCalculationPolicy(path, forced: true);
        await store.DeleteTransactionAsync(key, new DeleteWorkbookTransactionCommand(id, 2, Audit));
        AssertCalculationPolicy(path);
        Assert.Empty(new OpenXmlWorkbookReader().ReadApplicationTransactions(path, 2026, 3));
        Assert.Equal(vba, ReadVbaHash(path));
    }

    private static WorkbookAuditContext Audit =>
        new(WorkbookChangeSource.System, null, "one-time-calculation-test");

    private static void AssertCalculationPolicy(string path)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        var properties = Assert.IsType<CalculationProperties>(document.WorkbookPart!.Workbook!.CalculationProperties);
        Assert.Equal(CalculateModeValues.Auto, properties.CalculationMode!.Value);
        Assert.True(properties.FullCalculationOnLoad!.Value);
        Assert.False(properties.ForceFullCalculation!.Value);
    }

    private static void SetLegacyCalculationPolicy(string path, bool forced)
    {
        using var document = SpreadsheetDocument.Open(path, true, new OpenSettings { AutoSave = false });
        var workbook = document.WorkbookPart!.Workbook!;
        var properties = workbook.CalculationProperties ?? workbook.AppendChild(new CalculationProperties());
        properties.CalculationMode = CalculateModeValues.Manual;
        properties.FullCalculationOnLoad = false;
        properties.ForceFullCalculation = forced;
        workbook.Save();
    }

    private static byte[] ReadVbaHash(string path)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        using var stream = document.WorkbookPart!.VbaProjectPart!.GetStream(FileMode.Open, FileAccess.Read);
        return SHA256.HashData(stream);
    }

    private sealed class CalculationWorkspace : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lajur-calculation-");

        public CalculationWorkspace(bool forced)
        {
            SourcePath = Path.Combine(RootPath, "source.xlsm");
            File.Copy(FixturePath, SourcePath);
            SetLegacyCalculationPolicy(SourcePath, forced);
        }

        public string RootPath => directory.FullName;
        public string SourcePath { get; }
        public void Dispose() => directory.Delete(recursive: true);
    }
}
