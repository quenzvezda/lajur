using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Lajur.Application.Finance;

namespace Lajur.Infrastructure.Workbooks;

internal static partial class WorkbookMasterMap
{
    internal const string IconSheet = "__MasterIcons";
    internal sealed record Map(MasterManagement Snapshot, IReadOnlyDictionary<string, uint> Categories, IReadOnlyDictionary<string, uint> Payments,
        string? CategoryStructure, string? PaymentStructure, string? ReportReason);
    internal static Dictionary<string, string> ReadIcons(WorkbookPart workbook)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!workbook.Workbook!.Sheets!.Elements<Sheet>().Any(s => s.Name == IconSheet)) return result;
        var sheet = WorkbookSettingsMap.Sheet(workbook, IconSheet);
        if (WorkbookSettingsMap.Text(workbook, WorkbookSettingsMap.Cell(sheet, "A1")) != "PaymentMethod"
            || WorkbookSettingsMap.Text(workbook, WorkbookSettingsMap.Cell(sheet, "B1")) != "Icon") throw new InvalidDataException("Master icon metadata is not recognized.");
        foreach (var row in sheet.Worksheet!.GetFirstChild<SheetData>()!.Elements<Row>().Skip(1))
        {
            var name = WorkbookSettingsMap.Text(workbook, WorkbookSettingsMap.Cell(sheet, $"A{row.RowIndex!.Value}"));
            var icon = WorkbookSettingsMap.Text(workbook, WorkbookSettingsMap.Cell(sheet, $"B{row.RowIndex.Value}"));
            if (name.Length == 0 && icon.Length == 0) continue;
            if (name.Length == 0 || !MasterInput.Icons.Contains(icon) || !result.TryAdd(name, icon)) throw new InvalidDataException("Master icon metadata contains an invalid entry.");
        }
        return result;
    }

    internal static Map Read(string path, int year)
    {
        var reader = new OpenXmlWorkbookReader();
        var master = reader.ReadMasterData(path, year);
        var ledger = Enumerable.Range(1, 12).SelectMany(month => reader.ReadMonthlyTransactions(path, month)).ToArray();
        using var document = SpreadsheetDocument.Open(path, false);
        var workbook = document.WorkbookPart!;
        var sheet = WorkbookSettingsMap.Sheet(workbook, "Master");
        var icons = ReadIcons(workbook);
        var categories = new Dictionary<string, uint>(StringComparer.Ordinal);
        var payments = new Dictionary<string, uint>(StringComparer.Ordinal);
        for (uint row = 5; row <= 81; row++) Add(categories, row, 1);
        for (uint row = 5; row <= 24; row++) Add(payments, row, 7);
        void Add(Dictionary<string, uint> rows, uint row, int column)
        {
            var name = WorkbookSettingsMap.Text(workbook, WorkbookSettingsMap.Cell(sheet, $"{(column == 1 ? 'A' : 'G')}{row}"));
            if (name.Length > 0 && !rows.TryAdd(name, row)) throw new InvalidDataException("Master contains duplicate names.");
        }
        var categoryCapacity = Capacity(master, "ListKategori", 'A', 81);
        var paymentCapacity = Capacity(master, "ListPembayaran", 'G', 24);
        var categoryStructure = Structure(workbook, sheet, categories, categoryCapacity, true);
        var paymentStructure = Structure(workbook, sheet, payments, paymentCapacity, false);
        string? reportReason = null;
        try { MasterSupplement.Validate(workbook, master); }
        catch (ArgumentException error) { reportReason = error.Message; }
        var history = History(workbook);
        var dependencies = ReadDependencies(workbook);
        var entries = categories.Select(pair => Entry(pair.Key, pair.Value, true)).ToArray();
        var methods = payments.Select(pair => Entry(pair.Key, pair.Value, false)).ToArray();
        MasterEntry Entry(string name, uint row, bool category)
        {
            var data = category ? master.Categories.Single(c => c.Name == name) : null;
            var count = ledger.Count(t => string.Equals(category ? t.Category : t.PaymentMethod, name, StringComparison.OrdinalIgnoreCase));
            var historical = history.Count(item => string.Equals(category ? item.Category : item.Payment, name, StringComparison.OrdinalIgnoreCase));
            var deps = Dependencies(dependencies, name, row, category, false);
            var tailDeps = Dependencies(dependencies, name, row, category, true);
            var structure = category ? categoryStructure : paymentStructure;
            var reason = count > 0 ? $"Dipakai oleh {count} transaksi."
                : historical > 0 ? $"Masih tercatat pada {historical} riwayat transaksi."
                : deps > 0 ? $"Memiliki {deps} referensi formula, label atau workbook."
                : structure ?? reportReason;
            var canRename = reason is null;
            var deleteReason = reason ?? (data?.MonthlyBudgetIdr is not null ? "Masih memiliki target budget. Kosongkan budget sebelum menghapus." : tailDeps > 0 ? "Penghapusan akan menggeser Master yang direferensikan workbook." : null);
            return new(name, data?.Kind, data?.MonthlyBudgetIdr?.ToString(CultureInfo.InvariantCulture), category ? null : icons.GetValueOrDefault(name) ?? MasterInput.DefaultIcon(name),
                count, historical, deps, canRename, deleteReason is null, deleteReason);
        }
        var categoryReason = categoryStructure ?? reportReason ?? (categories.Count >= categoryCapacity ? "Daftar kategori mencapai kapasitas template." : null);
        var paymentReason = paymentStructure ?? reportReason ?? (payments.Count >= paymentCapacity ? "Daftar pembayaran mencapai kapasitas template." : null);
        var last = WorkbookSettingsMap.AuditRows(workbook).LastOrDefault(row => WorkbookSettingsMap.AuditText(row, "C") == "SETTINGS_MASTER");
        var content = sheet.Worksheet!.OuterXml + workbook.Workbook!.DefinedNames?.OuterXml + JsonSerializer.Serialize(icons) + WorkbookSettingsMap.AuditText(last, "A");
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return new(new(version, entries, methods, categoryCapacity, paymentCapacity, categoryReason is null, paymentReason is null, categoryReason, paymentReason), categories, payments, categoryStructure, paymentStructure, reportReason);
    }

    private static int Capacity(WorkbookMasterData master, string name, char column, int readEnd)
    {
        var definition = master.DefinedNames.GetValueOrDefault(name)?.Replace(" ", "").Replace("'Master'!", "Master!");
        var match = Regex.Match(definition ?? "", $@"^OFFSET\(Master!\${column}\$5,0,0,COUNTA\(Master!\${column}\$5:\${column}\$([0-9]+)\),1\)$", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var end) && end >= 5 ? Math.Min(readEnd, end) - 4 : 0;
    }
    private static string? Structure(WorkbookPart workbook, WorksheetPart sheet, Dictionary<string, uint> rows, int capacity, bool category)
    {
        if (capacity == 0) return "Named range daftar tidak dikenali. Kelola struktur melalui Excel.";
        if (rows.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count) return "Nama Master berbeda hanya dalam huruf besar/kecil. Perbaiki melalui Excel.";
        if (rows.Count > capacity || !rows.Values.SequenceEqual(Enumerable.Range(5, rows.Count).Select(r => (uint)r))) return "Daftar Master tidak berurutan atau melebihi batas template. Perbaiki melalui Excel.";
        var columns = category ? new[] { "A", "B", "C" } : ["G"];
        for (var row = 5; row < 5 + capacity; row++)
            foreach (var column in columns)
            {
                var cell = WorkbookSettingsMap.Cell(sheet, $"{column}{row}");
                if (cell?.CellFormula is not null) return "Master memiliki sel formula yang tidak dapat digeser atau ditimpa.";
                if (row >= 5 + rows.Count && !string.IsNullOrEmpty(WorkbookSettingsMap.Text(workbook, cell))) return "Slot kosong Master berisi konfigurasi lain. Periksa melalui Excel.";
            }
        return null;
    }
    private sealed record Historical(string Category, string Payment);
    private static IReadOnlyList<Historical> History(WorkbookPart workbook)
    {
        var values = new Dictionary<(string Id, string Category, string Payment), Historical>();
        void AddSnapshot(string id, string text)
        {
            if (text.Length == 0) return;
            try
            {
                using var json = JsonDocument.Parse(text);
                if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Transaction snapshots are malformed.");
                string Field(string key) => json.RootElement.EnumerateObject().FirstOrDefault(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase)).Value is { ValueKind: JsonValueKind.String } value ? value.GetString()! : "";
                var category = Field("Category"); var payment = Field("PaymentMethod");
                if (category.Length == 0) throw new InvalidDataException("Transaction snapshots have no category.");
                values.TryAdd((id, category, payment), new(category, payment));
            }
            catch (JsonException) { throw new InvalidDataException("Transaction snapshots are malformed."); }
        }
        if (workbook.Workbook!.Sheets!.Elements<Sheet>().Any(s => s.Name == "__AppIndex"))
            foreach (var row in WorkbookSettingsMap.Sheet(workbook, "__AppIndex").Worksheet!.GetFirstChild<SheetData>()!.Elements<Row>().Skip(1))
                AddSnapshot(WorkbookSettingsMap.AuditText(row, "A"), WorkbookSettingsMap.AuditText(row, "G"));
        foreach (var row in WorkbookSettingsMap.AuditRows(workbook))
        {
            if (WorkbookSettingsMap.AuditText(row, "C") is not "CREATE" and not "UPDATE" and not "DELETE") continue;
            foreach (var column in new[] { "H", "I" })
            {
                AddSnapshot(WorkbookSettingsMap.AuditText(row, "B"), WorkbookSettingsMap.AuditText(row, column));
            }
        }
        return values.Values.ToArray();
    }
    private sealed record DependencySources(IReadOnlyList<(string Text, bool Local)> Formulas, IReadOnlyDictionary<string, int> Labels, IReadOnlyList<string> Validations);
    private static DependencySources ReadDependencies(WorkbookPart workbook)
    {
        var formulas = new List<(string, bool)>();
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var validations = new List<string>();
        foreach (var definition in workbook.Workbook!.DefinedNames?.Elements<DefinedName>() ?? [])
            if (definition.Name?.Value is not "ListKategori" and not "ListPembayaran" and not "ListTipe") formulas.Add((definition.Text ?? "", false));
        foreach (var sheet in workbook.Workbook!.Sheets!.Elements<Sheet>())
        {
            var name = sheet.Name!.Value!;
            if (name.StartsWith("__", StringComparison.Ordinal) || name == MasterSupplement.SheetName) continue;
            var part = (WorksheetPart)workbook.GetPartById(sheet.Id!);
            foreach (var cell in part.Worksheet!.Descendants<Cell>())
            {
                if (cell.CellFormula is not null) formulas.Add((MasterSupplement.ExpandedFormula(part, cell), name == "Master"));
                else if (name != "Master")
                {
                    var text = WorkbookSettingsMap.Text(workbook, cell);
                    if (text.Length > 0) labels[text] = labels.GetValueOrDefault(text) + 1;
                }
            }
            foreach (var validation in part.Worksheet.Descendants<DataValidation>())
            {
                var formula = validation.Formula1?.Text ?? "";
                if (formula is not "ListKategori" and not "ListPembayaran" and not "ListTipe") { formulas.Add((formula, name == "Master")); validations.Add(formula); }
            }
        }
        foreach (var chart in workbook.WorksheetParts.SelectMany(p => p.DrawingsPart?.ChartParts ?? []))
            foreach (var formula in chart.ChartSpace!.Descendants<DocumentFormat.OpenXml.Drawing.Charts.Formula>()) formulas.Add((formula.Text ?? "", false));
        return new(formulas, labels, validations);
    }
    private static int Dependencies(DependencySources sources, string name, uint row, bool category, bool shifting)
    {
        var count = 0;
        var columns = category ? new[] { "A", "B", "C" } : ["G"];
        var escaped = name.Replace("\"", "\"\"");
        bool References(string formula, bool local)
        {
            if (formula.Contains("\"" + escaped + "\"", StringComparison.OrdinalIgnoreCase)) return true;
            if (Regex.IsMatch(formula, category ? @"\bListKategori\b" : @"\bListPembayaran\b", RegexOptions.IgnoreCase)
                || Regex.IsMatch(formula, @"\bINDIRECT\s*\(", RegexOptions.IgnoreCase)) return true;
            var normalized = formula.Replace(" ", "").Replace("'Master'!", "Master!");
            var genericBudget = Regex.IsMatch(normalized, @"^SUM\(Master!\$C\$5:\$C\$(?:81|86)\)$", RegexOptions.IgnoreCase)
                || Regex.IsMatch(normalized, @"^IFERROR\(INDEX\(Master!\$C\$5:\$C\$(81|86),MATCH\(\$A[0-9]+,Master!\$A\$5:\$A\$\1,0\)\),0\)$", RegexOptions.IgnoreCase);
            var pattern = local ? LocalReferenceRegex() : MasterReferenceRegex();
            foreach (Match match in pattern.Matches(formula))
            {
                var column = match.Groups[1].Value;
                if (!columns.Contains(column, StringComparer.OrdinalIgnoreCase)) continue;
                var start = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                var end = match.Groups[4].Success ? int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) : start;
                var endColumn = match.Groups[3].Success ? match.Groups[3].Value : column;
                // Whole known list/budget ranges update safely with a compacted list.
                if (genericBudget && start == 5 && end is 81 or 86 && category && endColumn.Equals(column, StringComparison.OrdinalIgnoreCase) && column is "A" or "C") continue;
                if (shifting ? end >= row : start <= row && end >= row) return true;
            }
            return false;
        }
        count += sources.Labels.GetValueOrDefault(name);
        count += sources.Formulas.Count(formula => References(formula.Text, formula.Local));
        count += sources.Validations.Count(formula => formula.Contains(name, StringComparison.OrdinalIgnoreCase));
        return count;
    }
    [GeneratedRegex(@"(?:'Master'|Master)!\$?([A-Z]+)\$?([0-9]+)(?::\$?([A-Z]+)\$?([0-9]+))?", RegexOptions.IgnoreCase)]
    private static partial Regex MasterReferenceRegex();
    [GeneratedRegex(@"(?<![A-Za-z0-9_])\$?([A-Z]+)\$?([0-9]+)(?::\$?([A-Z]+)\$?([0-9]+))?", RegexOptions.IgnoreCase)]
    private static partial Regex LocalReferenceRegex();
}
