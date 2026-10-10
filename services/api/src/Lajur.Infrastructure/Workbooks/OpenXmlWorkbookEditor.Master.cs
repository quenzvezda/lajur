using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Lajur.Application.Finance;

namespace Lajur.Infrastructure.Workbooks;

internal sealed partial class OpenXmlWorkbookEditor
{
    public MasterManagement ChangeMaster(WorkbookKey key, ChangeWorkbookMaster command, WorkbookAuditContext audit, DateTimeOffset timestamp)
    {
        var map = WorkbookMasterMap.Read(workbookPath, key.Year);
        var payload = JsonSerializer.Serialize(command);
        if (SettingsReplay(command.OperationId, "MASTER", payload)) return map.Snapshot;
        CheckSettingsVersion(map.Snapshot.Version, command.ExpectedVersion);
        var category = command.Entity == "category";
        if (((category ? map.CategoryStructure : map.PaymentStructure) ?? map.ReportReason) is { } unsupported)
            throw new ArgumentException(unsupported);
        var list = category ? map.Snapshot.Categories : map.Snapshot.Payments;
        var rows = category ? map.Categories : map.Payments;
        var existing = command.Action == "create" ? null : list.SingleOrDefault(e => e.Name == command.OriginalName)
            ?? (command.Action == "create" ? null : throw new ArgumentException("Master yang dipilih sudah tidak ditemukan."));
        var name = command.Action == "delete" ? existing!.Name : MasterInput.Name(command.Name);
        if (command.Action != "delete" && list.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && e.Name != existing?.Name)) throw new ArgumentException("Nama sudah ada dalam daftar Master.");
        if (command.Action == "create" && !(category ? map.Snapshot.CanAddCategory : map.Snapshot.CanAddPayment))
            throw new ArgumentException((category ? map.Snapshot.CategoryReason : map.Snapshot.PaymentReason) ?? "Master tidak dapat ditambah.");
        var kind = category ? command.Kind : null;
        var budget = category ? SettingsInput.Amount(command.BudgetIdr) : null;
        if (category && command.Action != "delete" && (kind is not "Pengeluaran" and not "Pemasukan" || !masterData.TransactionTypes.Contains(kind))) throw new ArgumentException("Pilih tipe Pengeluaran atau Pemasukan dari Master.");
        if (category && kind == "Pemasukan" && budget is not null) throw new ArgumentException("Budget hanya berlaku untuk kategori pengeluaran.");
        if (category && command.Icon is not null || !category && (command.Kind is not null || command.BudgetIdr is not null)) throw new ArgumentException("Isian Master tidak sesuai jenis data.");
        if (!category && command.Icon is not null && !MasterInput.Icons.Contains(command.Icon)) throw new ArgumentException("Pilih ikon pembayaran yang tersedia.");
        if (command.Action == "update" && (name != existing!.Name || kind != existing.Kind) && !existing.CanRename) throw new ArgumentException(existing.Reason ?? "Nama/tipe Master masih memiliki dependensi.");
        if (command.Action == "delete" && !existing!.CanDelete) throw new ArgumentException(existing.Reason ?? "Master masih memiliki dependensi.");
        if (command.Action == "create" && command.OriginalName is not null) throw new ArgumentException("Master baru tidak memiliki nama asal.");
        var total = map.Snapshot.Categories.Where(e => e.Name != existing?.Name || !category).Sum(e => (decimal)(SettingsInput.Amount(e.BudgetIdr) ?? 0)) + (budget ?? 0);
        if (total > long.MaxValue) throw new ArgumentException("Total budget melebihi batas yang didukung.");
        using (var document = SpreadsheetDocument.Open(workbookPath, true, new OpenSettings { AutoSave = false }))
        {
            var workbook = RequiredWorkbookPart(document);
            MasterSupplement.Validate(workbook, masterData);
            var sheet = WorkbookSettingsMap.Sheet(workbook, "Master");
            var data = sheet.Worksheet!.GetFirstChild<SheetData>()!;
            var rowNumber = command.Action == "create" ? (uint)(rows.Count + 5) : rows[existing!.Name];
            if (command.Action == "delete")
            {
                var columns = category ? new[] { 1, 2, 3 } : [7];
                for (var row = rowNumber; row < rows.Count + 4; row++)
                    foreach (var column in columns) CopyCell(FindCell(GetOrCreateRow(data, row + 1), column), GetOrCreateRow(data, row), column, row);
                foreach (var column in columns) ClearCellValue(FindCell(GetOrCreateRow(data, (uint)(rows.Count + 4)), column));
            }
            else
            {
                var row = GetOrCreateRow(data, rowNumber);
                WriteTextCell(row, category ? 1 : 7, rowNumber, name, data, rowNumber > 5 ? rowNumber - 1 : 5);
                if (category)
                {
                    WriteTextCell(row, 2, rowNumber, kind, data, rowNumber > 5 ? rowNumber - 1 : 5);
                    WriteSettingsNumber(sheet, 3, rowNumber, budget);
                }
            }
            sheet.Worksheet.Save();
            if (!category)
            {
                var icons = WorkbookMasterMap.ReadIcons(workbook);
                if (existing is not null) icons.Remove(existing.Name);
                if (command.Action != "delete" && command.Icon is not null) icons[name] = command.Icon;
                var iconPart = EnsureSystemSheet(workbook, WorkbookMasterMap.IconSheet, ["PaymentMethod", "Icon"]);
                var iconData = iconPart.Worksheet!.GetFirstChild<SheetData>()!;
                foreach (var row in iconData.Elements<Row>().Skip(1).ToArray()) row.Remove();
                foreach (var (method, icon) in icons) AppendRow(iconPart, method, icon);
                iconPart.Worksheet.Save();
            }
        }
        // Read the just-written Master before generating its complete additional report.
        var updatedMaster = new OpenXmlWorkbookReader().ReadMasterData(workbookPath, key.Year);
        using (var document = SpreadsheetDocument.Open(workbookPath, true, new OpenSettings { AutoSave = false }))
        {
            MasterSupplement.Write(document.WorkbookPart!, updatedMaster);
            var before = JsonSerializer.Serialize(new { BeforeVersion = map.Snapshot.Version,
                Entry = existing is null ? null : new { existing.Name, existing.Kind, existing.BudgetIdr, existing.Icon } });
            AppendSettingsAudit(document, key, command.OperationId, "MASTER", payload, before, audit, timestamp);
        }
        return WorkbookMasterMap.Read(workbookPath, key.Year).Snapshot;
    }
}
