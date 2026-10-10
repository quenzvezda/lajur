using System.Globalization;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Lajur.Application.Finance;

namespace Lajur.Infrastructure.Workbooks;

internal sealed partial class OpenXmlWorkbookEditor
{
    internal bool SettingsReplayed { get; private set; }
    public BudgetSettings SaveBudget(WorkbookKey key, SaveBudgetSettings command, WorkbookAuditContext audit, DateTimeOffset timestamp)
    {
        var map = WorkbookSettingsMap.ReadBudget(workbookPath);
        var scope = WorkbookSettingsMap.BudgetScope;
        var payload = JsonSerializer.Serialize(command);
        if (SettingsReplay(command.OperationId, scope, payload)) return map.Snapshot;
        CheckSettingsVersion(map.Snapshot.Version, command.ExpectedVersion);
        if (command.Changes.Select(c => c.Category).Distinct(StringComparer.Ordinal).Count() != command.Changes.Count)
            throw new ArgumentException("Kategori budget tidak boleh diulang dalam satu operasi.");
        var changed = command.Changes.ToDictionary(c => c.Category, c => SettingsInput.Amount(c.AmountIdr), StringComparer.Ordinal);
        foreach (var category in changed.Keys)
            if (!map.Snapshot.Categories.Any(c => c.Category == category && c.Editable)) throw new ArgumentException("Kategori budget tidak ditemukan atau tidak dapat diedit.");
        decimal total = map.Snapshot.Categories.Sum(c => (decimal)(changed.TryGetValue(c.Category, out var amount) ? amount ?? 0 : SettingsInput.Amount(c.AmountIdr) ?? 0));
        if (total > long.MaxValue) throw new ArgumentException("Total budget melebihi batas nominal yang didukung.");
        using (var document = SpreadsheetDocument.Open(workbookPath, true))
        {
            var workbook = RequiredWorkbookPart(document);
            var sheet = WorkbookSettingsMap.Sheet(workbook, "Master");
            foreach (var (category, amount) in changed) WriteSettingsNumber(sheet, 3, map.Rows[category], amount);
            sheet.Worksheet!.Save();
            AppendSettingsAudit(document, key, command.OperationId, scope, payload, JsonSerializer.Serialize(map.Snapshot), audit, timestamp);
        }
        return WorkbookSettingsMap.ReadBudget(workbookPath).Snapshot;
    }

    public ProjectionSettings SaveProjection(WorkbookKey key, int month, SaveProjectionSettings command, WorkbookAuditContext audit, DateTimeOffset timestamp)
    {
        var map = WorkbookSettingsMap.ReadProjection(workbookPath, month);
        var scope = WorkbookSettingsMap.ProjectionScope(month);
        var payload = JsonSerializer.Serialize(command);
        if (SettingsReplay(command.OperationId, scope, payload)) return map.Snapshot;
        CheckSettingsVersion(map.Snapshot.Version, command.ExpectedVersion);
        if (command.Changes.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != command.Changes.Count)
            throw new ArgumentException("Sumber proyeksi tidak boleh diulang dalam satu operasi.");
        var values = new Dictionary<string, (int? Day, long? Estimate, int? Days, long? Rate)>();
        foreach (var change in command.Changes)
        {
            var source = map.Snapshot.Sources.SingleOrDefault(s => s.Id == change.Id && s.Editable)
                ?? throw new ArgumentException("Sumber proyeksi tidak ditemukan atau tidak dapat diedit.");
            if (change.PayoutDay is < 1 or > 31 || change.WorkDays is < 0 or > 31) throw new ArgumentException("Tanggal cair harus 1–31 dan hari kerja 0–31, atau kosong.");
            var estimate = SettingsInput.Amount(change.EstimateIdr);
            var rate = SettingsInput.Amount(change.DailyRateIdr);
            if (source.IsMealAllowance)
            {
                if (change.EstimateIdr is not null) throw new ArgumentException("Estimasi uang makan dihitung dari jumlah hari dan tarif.");
                var product = (decimal)(change.WorkDays ?? 0) * (rate ?? 0);
                if (product > long.MaxValue) throw new ArgumentException("Estimasi uang makan melebihi batas nominal yang didukung.");
                estimate = change.WorkDays is not null && rate is not null ? (long)product : null;
            }
            else if (change.WorkDays is not null || change.DailyRateIdr is not null) throw new ArgumentException("Hari dan tarif hanya berlaku untuk uang makan.");
            values.Add(change.Id, (change.PayoutDay, estimate, change.WorkDays, rate));
        }
        if (map.Snapshot.Sources.Sum(s => (decimal)(values.TryGetValue(s.Id, out var v) ? v.Estimate ?? 0 : SettingsInput.Amount(s.EstimateIdr) ?? 0)) > long.MaxValue)
            throw new ArgumentException("Total estimasi melebihi batas nominal yang didukung.");
        using (var document = SpreadsheetDocument.Open(workbookPath, true))
        {
            var workbook = RequiredWorkbookPart(document);
            var sheet = WorkbookSettingsMap.Sheet(workbook, map.Sheet);
            foreach (var (id, value) in values)
            {
                WriteSettingsNumber(sheet, 12, map.Rows[id], value.Day);
                if (map.Snapshot.Sources.Single(s => s.Id == id).IsMealAllowance)
                {
                    WriteSettingsNumber(sheet, 13, 10, value.Days);
                    WriteSettingsNumber(sheet, 13, 11, value.Rate);
                }
                else WriteSettingsNumber(sheet, 13, map.Rows[id], value.Estimate);
            }
            sheet.Worksheet!.Save();
            AppendSettingsAudit(document, key, command.OperationId, scope, payload, JsonSerializer.Serialize(map.Snapshot), audit, timestamp);
        }
        return WorkbookSettingsMap.ReadProjection(workbookPath, month).Snapshot;
    }

    private static void CheckSettingsVersion(string actual, string expected)
    {
        if (actual != expected) throw new WorkbookSettingsConflictException("Pengaturan berubah sejak dibuka. Muat versi terbaru dan tinjau draft sebelum menyimpan lagi.");
    }
    private bool SettingsReplay(Guid operationId, string scope, string payload)
    {
        using var document = SpreadsheetDocument.Open(workbookPath, false);
        var existing = WorkbookSettingsMap.AuditRows(document.WorkbookPart!).SingleOrDefault(row => WorkbookSettingsMap.AuditText(row, "A") == operationId.ToString("D"));
        if (existing is null) return false;
        if (WorkbookSettingsMap.AuditText(existing, "C") != "SETTINGS_" + scope || WorkbookSettingsMap.AuditText(existing, "I") != payload)
            throw new WorkbookSettingsConflictException("Identitas operasi sudah digunakan untuk perubahan berbeda.");
        SettingsReplayed = true;
        return true;
    }
    private static void WriteSettingsNumber(WorksheetPart sheet, int column, uint rowNumber, long? value)
    {
        var data = sheet.Worksheet!.GetFirstChild<SheetData>()!;
        var row = GetOrCreateRow(data, rowNumber);
        var cell = GetOrCreateCell(row, column, rowNumber, data, rowNumber);
        if (cell.CellFormula is not null) throw new ArgumentException("Sel formula tidak dapat ditimpa oleh pengaturan.");
        cell.InlineString = null;
        cell.DataType = value is null ? null : CellValues.Number;
        cell.CellValue = value is null ? null : new CellValue(value.Value.ToString(CultureInfo.InvariantCulture));
    }
    private static void AppendSettingsAudit(SpreadsheetDocument document, WorkbookKey key, Guid operationId, string scope,
        string payload, string before, WorkbookAuditContext audit, DateTimeOffset timestamp)
    {
        var workbook = RequiredWorkbookPart(document);
        EnsureSystemSheet(workbook, "__AppIndex", AppIndexHeaders);
        var auditPart = EnsureSystemSheet(workbook, "__AuditLog", AuditLogHeaders);
        AppendRow(auditPart, operationId.ToString("D"), null, "SETTINGS_" + scope, FormatSource(audit.Source),
            FormatTimestamp(timestamp), null, null, before, payload, key.UserId.ToString("D"), audit.DeviceId, audit.CorrelationId);
        auditPart.Worksheet!.Save();
        RequestFullCalculation(workbook);
        workbook.Workbook!.Save();
    }
}
