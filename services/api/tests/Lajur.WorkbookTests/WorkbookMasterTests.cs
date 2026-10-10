using System.IO.Compression;
using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Lajur.Application.Finance;
using Lajur.Infrastructure.Workbooks;

namespace Lajur.WorkbookTests;

public sealed class WorkbookMasterTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("lajur-master-").FullName;
    private readonly WorkbookKey key = new(Guid.NewGuid(), 2026);
    private readonly string path;
    private readonly OpenXmlWorkbookStore store;
    private static readonly WorkbookAuditContext Audit = new(WorkbookChangeSource.Web, null, "master-test");
    public WorkbookMasterTests()
    {
        path = Path.Combine(root, "fixture.xlsm");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LajurWorkbookFixture.xlsm"), path);
        store = new(_ => path, backupDirectoryResolver: _ => Path.Combine(root, "backups"));
    }
    public void Dispose() => Directory.Delete(root, true);
    private static WorksheetPart Sheet(SpreadsheetDocument document, string name) => (WorksheetPart)document.WorkbookPart!.GetPartById(document.WorkbookPart.Workbook!.Sheets!.Elements<Sheet>().Single(s => s.Name == name).Id!);
    private static Dictionary<string, byte[]> Parts(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.ToDictionary(e => e.FullName, e => { using var stream = e.Open(); using var copy = new MemoryStream(); stream.CopyTo(copy); return copy.ToArray(); });
    }
    private Task<MasterManagement> Add(MasterManagement master, string name, string entity = "category", string? icon = null)
        => store.ChangeMasterAsync(key, new(Guid.NewGuid(), master.Version, entity, "create", null, name, entity == "category" ? "Pengeluaran" : null, null, icon), Audit);

    [Fact]
    public async Task Master_create_edit_delete_preserves_existing_parts_and_covers_native_report()
    {
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        var initial = await store.ReadMasterManagementAsync(key);
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.True(initial.CanAddCategory); Assert.True(initial.CanAddPayment);
        Assert.Equal(77, initial.CategoryCapacity); Assert.Equal(20, initial.PaymentCapacity);
        var original = Parts(path);
        var oldYear = await store.ReadYearAsync(key);
        var added = await Add(initial, "Biaya sintetis baru");
        Assert.True(added.Categories.Last().CanRename); Assert.True(added.Categories.Last().CanDelete);
        Assert.NotSame(oldYear, await store.ReadYearAsync(key));
        using (var document = SpreadsheetDocument.Open(path, false))
        {
            Assert.Equal("Master!$A$36", Sheet(document, "LajurMaster").Worksheet!.Descendants<Cell>().Single(c => c.CellReference == "A29").CellFormula!.Text);
            Assert.Equal("SUMIFS(JanTable[Jumlah (Rp)],JanTable[Tipe],\"Pengeluaran\",JanTable[Kategori],SUBSTITUTE(SUBSTITUTE(SUBSTITUTE($A29,\"~\",\"~~\"),\"*\",\"~*\"),\"?\",\"~?\"))", Sheet(document, "LajurMaster").Worksheet!.Descendants<Cell>().Single(c => c.CellReference == "B29").CellFormula!.Text);
        }
        var renamed = await store.ChangeMasterAsync(key, new(Guid.NewGuid(), added.Version, "category", "update", "Biaya sintetis baru", "Biaya sintetis diperbarui", "Pengeluaran", null, null), Audit);
        Assert.DoesNotContain(renamed.Categories, e => e.Name == "Biaya sintetis baru");
        var removed = await store.ChangeMasterAsync(key, new(Guid.NewGuid(), renamed.Version, "category", "delete", "Biaya sintetis diperbarui", null, null, null, null), Audit);
        Assert.Equal(initial.Categories.Count, removed.Categories.Count);
        var after = Parts(path);
        foreach (var (part, bytes) in original)
            if (part is not "xl/workbook.xml" and not "xl/_rels/workbook.xml.rels" and not "[Content_Types].xml" and not "xl/worksheets/sheet14.xml") Assert.True(bytes.SequenceEqual(after[part]), $"Unrelated part changed: {part}");
        using var final = SpreadsheetDocument.Open(path, false);
        Assert.Equal("OFFSET(Master!$A$5,0,0,COUNTA(Master!$A$5:$A$81),1)", final.WorkbookPart!.Workbook!.DefinedNames!.Elements<DefinedName>().Single(n => n.Name == "ListKategori").Text);
    }

    [Fact]
    public async Task Payment_icons_are_editable_on_used_values_without_renaming_transactions()
    {
        var initial = await store.ReadMasterManagementAsync(key);
        var used = initial.Payments.First(e => e.TransactionCount > 0);
        Assert.False(used.CanRename); Assert.False(used.CanDelete);
        var changed = await store.ChangeMasterAsync(key, new(Guid.NewGuid(), initial.Version, "payment", "update", used.Name, used.Name, null, null, "qr"), Audit);
        Assert.Equal("qr", changed.Payments.Single(e => e.Name == used.Name).Icon);
        Assert.Equal("qr", (await store.ReadYearAsync(key)).Master.PaymentIcons[used.Name]);
        using var document = SpreadsheetDocument.Open(path, false);
        Assert.Equal(SheetStateValues.VeryHidden, document.WorkbookPart!.Workbook!.Sheets!.Elements<Sheet>().Single(s => s.Name == "__MasterIcons").State!.Value);
        var beforeText = Sheet(document, "__AuditLog").Worksheet!.Descendants<Cell>().Single(c => c.CellReference == "H2").InnerText;
        Assert.True(beforeText.Length < 2048);
        using var before = System.Text.Json.JsonDocument.Parse(beforeText);
        Assert.Equal(used.Name, before.RootElement.GetProperty("Entry").GetProperty("Name").GetString());
        Assert.Equal(initial.Version, before.RootElement.GetProperty("BeforeVersion").GetString());
    }

    [Fact]
    public async Task Transactions_history_budget_and_formula_references_block_unsafe_changes()
    {
        var initial = await store.ReadMasterManagementAsync(key);
        var added = await Add(initial, "Baru tanpa pemakaian");
        var id = Guid.NewGuid();
        await store.CreateTransactionAsync(key, new(id, new(new DateOnly(2026, 10, 10), "Pengeluaran", "", "Baru tanpa pemakaian", 15000, "Synthetic"), Audit));
        var current = await store.ReadMasterManagementAsync(key);
        Assert.False(current.Categories.Last().CanDelete);
        await Assert.ThrowsAsync<ArgumentException>(() => store.ChangeMasterAsync(key, new(Guid.NewGuid(), added.Version, "category", "delete", "Baru tanpa pemakaian", null, null, null, null), Audit));
        await store.DeleteTransactionAsync(key, new(id, 1, Audit));
        current = await store.ReadMasterManagementAsync(key);
        Assert.Equal(0, current.Categories.Last().TransactionCount);
        Assert.True(current.Categories.Last().HistoryCount > 0); Assert.False(current.Categories.Last().CanRename);
        Assert.False(current.Categories.First().CanDelete);
        var unused = await Add(current, "Rujukan formula");
        using (var document = SpreadsheetDocument.Open(path, true))
        {
            Sheet(document, "Jan").Worksheet!.GetFirstChild<SheetData>()!.Append(new Row(new Cell { CellReference = "AT100", CellFormula = new CellFormula("Master!$A$37") }) { RowIndex = 100 });
        }
        var guarded = await store.ReadMasterManagementAsync(key);
        Assert.False(guarded.Categories.Last().CanRename); Assert.False(guarded.Categories.Last().CanDelete);
        var before = SHA256.HashData(File.ReadAllBytes(path));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ChangeMasterAsync(key, new(Guid.NewGuid(), unused.Version, "category", "delete", "Rujukan formula", null, null, null, null), Audit));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Fact]
    public async Task Retry_is_idempotent_and_stale_or_reused_operations_are_rejected()
    {
        var initial = await store.ReadMasterManagementAsync(key);
        var command = new ChangeWorkbookMaster(Guid.NewGuid(), initial.Version, "payment", "create", null, "Pembayaran sintetis", null, null, "wallet");
        var saved = await store.ChangeMasterAsync(key, command, Audit);
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        await store.ChangeMasterAsync(key, command, Audit);
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path))); Assert.Single(Directory.GetFiles(Path.Combine(root, "backups")));
        Assert.Equal("MASTER", (await store.ReadSettingsOperationAsync(key, command.OperationId))!.Scope);
        await Assert.ThrowsAsync<WorkbookSettingsConflictException>(() => store.ChangeMasterAsync(key, command with { OperationId = Guid.NewGuid() }, Audit));
        await Assert.ThrowsAsync<WorkbookSettingsConflictException>(() => store.ChangeMasterAsync(key, command with { Name = "Different" }, Audit));
        await Assert.ThrowsAsync<ArgumentException>(() => Add(saved, "pembayaran SINTETIS", "payment"));
    }

    [Fact]
    public async Task Removing_unused_middle_payment_keeps_dropdown_contiguous_and_other_configuration_intact()
    {
        var master = await store.ReadMasterManagementAsync(key);
        master = await Add(master, "Metode baru A", "payment", "cash"); master = await Add(master, "Metode baru B", "payment", "qr");
        var deleted = await store.ChangeMasterAsync(key, new(Guid.NewGuid(), master.Version, "payment", "delete", "Metode baru A", null, null, null, null), Audit);
        Assert.Equal("Metode baru B", deleted.Payments.Last().Name); Assert.Equal("qr", deleted.Payments.Last().Icon);
        using var document = SpreadsheetDocument.Open(path, false);
        Assert.Equal("Metode baru B", Sheet(document, "Master").Worksheet!.Descendants<Cell>().Single(c => c.CellReference == "G13").InnerText);
        Assert.Equal("Pengeluaran", new OpenXmlWorkbookReader().ReadMasterData(path, 2026).TransactionTypes[0]);
    }

    [Fact]
    public async Task Custom_report_rejects_creation_without_mutation()
    {
        var master = await store.ReadMasterManagementAsync(key);
        master = await Add(master, "Report guarded");
        using (var document = SpreadsheetDocument.Open(path, true))
            Sheet(document, "LajurMaster").Worksheet!.Descendants<Cell>().Single(c => c.CellReference == "B5").CellFormula = new CellFormula("123");
        var blocked = await store.ReadMasterManagementAsync(key); Assert.False(blocked.CanAddCategory);
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        await Assert.ThrowsAsync<ArgumentException>(() => Add(blocked, "Rejected report")); Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unsupported_list_or_capacity_limit_rejects_add_without_writing(bool full)
    {
        using (var document = SpreadsheetDocument.Open(path, true))
            document.WorkbookPart!.Workbook!.DefinedNames!.Elements<DefinedName>().Single(n => n.Name == "ListPembayaran").Text = full
                ? "OFFSET(Master!$G$5,0,0,COUNTA(Master!$G$5:$G$12),1)" : "Master!$G$5:$G$24";
        var master = await store.ReadMasterManagementAsync(key);
        Assert.False(master.CanAddPayment);
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        await Assert.ThrowsAsync<ArgumentException>(() => Add(master, "Cannot append", "payment"));
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Fact]
    public async Task Shared_formula_dependencies_and_managed_report_shared_serialization_are_recognized()
    {
        var master = await store.ReadMasterManagementAsync(key);
        master = await Add(master, "Shared A"); master = await Add(master, "Shared B");
        using (var document = SpreadsheetDocument.Open(path, true))
        {
            var data = Sheet(document, "Jan").Worksheet!.GetFirstChild<SheetData>()!;
            data.Append(new Row(new Cell { CellReference = "AT100", CellFormula = new CellFormula("Master!A36") { FormulaType = CellFormulaValues.Shared, SharedIndex = 99, Reference = "AT100:AT101" } }) { RowIndex = 100 });
            data.Append(new Row(new Cell { CellReference = "AT101", CellFormula = new CellFormula { FormulaType = CellFormulaValues.Shared, SharedIndex = 99 } }) { RowIndex = 101 });
            var report = Sheet(document, "LajurMaster");
            var b5 = report.Worksheet!.Descendants<Cell>().Single(c => c.CellReference == "B5");
            b5.CellFormula!.FormulaType = CellFormulaValues.Shared; b5.CellFormula.SharedIndex = 10; b5.CellFormula.Reference = "B5:B6";
            report.Worksheet.Descendants<Cell>().Single(c => c.CellReference == "B6").CellFormula = new CellFormula { FormulaType = CellFormulaValues.Shared, SharedIndex = 10 };
        }
        var guarded = await store.ReadMasterManagementAsync(key);
        Assert.True(guarded.CanAddCategory); // shared serialization does not make a managed report custom
        Assert.False(guarded.Categories.Single(c => c.Name == "Shared B").CanRename);
        Assert.False(guarded.Categories.Single(c => c.Name == "Shared B").CanDelete);
    }

    [Fact]
    public async Task Custom_indexed_range_and_dynamic_references_are_conservative_blockers()
    {
        var master = await Add(await store.ReadMasterManagementAsync(key), "Custom range target");
        using (var document = SpreadsheetDocument.Open(path, true))
        {
            Sheet(document, "Jan").Worksheet!.GetFirstChild<SheetData>()!.Append(new Row(new Cell { CellReference = "AT100", CellFormula = new CellFormula("INDEX(Master!$A$5:$A$81,32)") }) { RowIndex = 100 });
        }
        var guarded = await store.ReadMasterManagementAsync(key);
        Assert.False(guarded.Categories.Last().CanRename);
        Assert.False(guarded.Categories.Last().CanDelete);
    }

    [Fact]
    public async Task Generated_year_retains_icons_and_regenerates_the_managed_report_without_transactions()
    {
        await Add(await store.ReadMasterManagementAsync(key), "Template payment", "payment", "qr");
        var nextKey = key with { Year = 2027 };
        var nextStore = new OpenXmlWorkbookStore(k => Path.Combine(root, $"{k.Year}.xlsm"));
        await nextStore.GenerateYearAsync(nextKey, path, Audit);
        var master = await nextStore.ReadMasterManagementAsync(nextKey);
        Assert.True(master.CanAddCategory); Assert.True(master.CanAddPayment);
        Assert.Equal("qr", master.Payments.Last().Icon);
        Assert.All((await nextStore.ReadYearAsync(nextKey)).Months, Assert.Empty);
        using var document = SpreadsheetDocument.Open(Path.Combine(root, "2027.xlsm"), false);
        Assert.Equal("LAJUR — REKAP MASTER 2027", Sheet(document, "LajurMaster").Worksheet!.Descendants<Cell>().Single(c => c.CellReference == "A1").InnerText);
    }

    [Fact]
    public async Task Tombstone_snapshot_blocks_master_deletion_even_without_per_transaction_audit_rows()
    {
        await Add(await store.ReadMasterManagementAsync(key), "Historical tombstone");
        var id = Guid.NewGuid();
        await store.CreateTransactionAsync(key, new(id, new(new DateOnly(2026, 10, 10), "Pengeluaran", "", "Historical tombstone", 100, "Synthetic"), Audit));
        await store.DeleteTransactionAsync(key, new(id, 1, Audit));
        using (var document = SpreadsheetDocument.Open(path, true))
        {
            var rows = Sheet(document, "__AuditLog").Worksheet!.GetFirstChild<SheetData>()!.Elements<Row>().Skip(1).Where(row => row.Elements<Cell>().Single(c => c.CellReference!.Value == "B" + row.RowIndex!.Value).InnerText == id.ToString("D")).ToArray();
            foreach (var row in rows) row.Remove();
        }
        var master = await store.ReadMasterManagementAsync(key);
        var entry = master.Categories.Single(e => e.Name == "Historical tombstone");
        Assert.Equal(0, entry.TransactionCount); Assert.True(entry.HistoryCount > 0); Assert.False(entry.CanDelete);
    }
}
