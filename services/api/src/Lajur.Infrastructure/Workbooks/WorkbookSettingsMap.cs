using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.CompilerServices;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Lajur.Application.Finance;

namespace Lajur.Infrastructure.Workbooks;

// Server-owned bindings. Neither cell addresses nor formulas are accepted from the browser.
internal static class WorkbookSettingsMap
{
    private static readonly ConditionalWeakTable<WorkbookPart, string[]> SharedText = new();
    internal sealed record BudgetMap(BudgetSettings Snapshot, IReadOnlyDictionary<string, uint> Rows);
    internal sealed record ProjectionMap(ProjectionSettings Snapshot, string Sheet, IReadOnlyDictionary<string, uint> Rows);
    internal static readonly string[] MonthSheets = ["Jan", "Feb", "Mar", "April", "May", "June", "July", "Aug", "Sep", "Okt", "Nov", "Des"];
    internal const string BudgetScope = "BUDGET";
    internal static string ProjectionScope(int month) => $"PROJECTION_{month}";

    internal static WorksheetPart Sheet(WorkbookPart workbook, string name)
    {
        var sheet = workbook.Workbook!.Sheets!.Elements<Sheet>().SingleOrDefault(s => s.Name?.Value == name)
            ?? throw new InvalidDataException($"Required settings sheet '{name}' was not found.");
        return (WorksheetPart)workbook.GetPartById(sheet.Id!);
    }
    internal static Cell? Cell(WorksheetPart sheet, string reference) => sheet.Worksheet!.GetFirstChild<SheetData>()!
        .Elements<Row>().SelectMany(row => row.Elements<Cell>()).SingleOrDefault(cell => cell.CellReference?.Value == reference);
    internal static string Text(WorkbookPart workbook, Cell? cell)
    {
        if (cell?.DataType?.Value == CellValues.SharedString && int.TryParse(cell.CellValue?.Text, out var index))
            return SharedText.GetValue(workbook, part => part.SharedStringTablePart!.SharedStringTable!.Elements<SharedStringItem>().Select(item => item.InnerText).ToArray())[index].Trim();
        return (cell?.InlineString?.InnerText ?? cell?.CellValue?.Text ?? "").Trim();
    }
    internal static bool Literal(WorkbookPart workbook, Cell? cell) => cell?.CellFormula is null
        && (string.IsNullOrEmpty(Text(workbook, cell)) || cell?.DataType?.Value is null || cell.DataType.Value == CellValues.Number)
        && (string.IsNullOrEmpty(Text(workbook, cell)) || WholeNumber(cell) is not null);
    private static long? WholeNumber(Cell? cell) => decimal.TryParse(cell?.CellValue?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && value == decimal.Truncate(value) && value is >= 0 and <= long.MaxValue ? (long)value : null;
    private static string? Amount(WorkbookPart workbook, Cell? cell) => Literal(workbook, cell) && !string.IsNullOrEmpty(Text(workbook, cell))
        ? WholeNumber(cell)?.ToString(CultureInfo.InvariantCulture) : null;

    internal static BudgetMap ReadBudget(string path)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        var workbook = document.WorkbookPart!;
        var sheet = Sheet(workbook, "Master");
        var rows = new Dictionary<string, uint>(StringComparer.Ordinal);
        var settings = new List<BudgetSetting>();
        for (uint row = 5; row <= 81; row++)
        {
            var name = Text(workbook, Cell(sheet, $"A{row}"));
            if (name.Length == 0 || Text(workbook, Cell(sheet, $"B{row}")) != "Pengeluaran") continue;
            if (!rows.TryAdd(name, row)) throw new InvalidDataException("Kategori budget duplikat; perbaiki Master di Excel.");
            var editable = Literal(workbook, Cell(sheet, $"C{row}"))
                && Cell(sheet, $"A{row}")?.CellFormula is null && Cell(sheet, $"B{row}")?.CellFormula is null;
            settings.Add(new(name, Amount(workbook, Cell(sheet, $"C{row}")), editable,
                editable ? null : "Budget atau kategori menggunakan formula/format yang belum didukung. Edit melalui Excel."));
        }
        var cells = Enumerable.Range(4, 78).SelectMany(row => new[] { $"A{row}", $"B{row}", $"C{row}" });
        return new(new(Version(workbook, sheet, cells, BudgetScope), settings), rows);
    }

    internal static ProjectionMap ReadProjection(string path, int month)
    {
        if (month is < 1 or > 12) throw new ArgumentException("Bulan harus antara 1 dan 12.");
        var sources = new OpenXmlWorkbookReader().ReadIncomeSourceInputs(path, month);
        using var document = SpreadsheetDocument.Open(path, false);
        var workbook = document.WorkbookPart!;
        var sheetName = MonthSheets[month - 1];
        var sheet = Sheet(workbook, sheetName);
        var rows = new Dictionary<string, uint>(StringComparer.Ordinal);
        var settings = new List<IncomeProjectionSetting>();
        foreach (var source in sources ?? [])
        {
            var row = source.Row;
            var estimateCell = Cell(sheet, $"M{row}");
            var meal = row == 3 && source.IncomeCategory == "Uang Makan"
                && sources!.All(source => source.Row < 10) && estimateCell?.CellFormula?.Text == "$M$10*$M$11";
            var day = Cell(sheet, $"L{row}");
            var editable = source.IncomeCategory is not null && source.PaidThresholdIdr is not null
                && Literal(workbook, day) && (string.IsNullOrEmpty(Text(workbook, day)) || WholeNumber(day) is >= 1 and <= 31)
                && (meal ? Literal(workbook, Cell(sheet, "M10")) && Literal(workbook, Cell(sheet, "M11"))
                    && (WholeNumber(Cell(sheet, "M10")) is null or <= 31) : Literal(workbook, estimateCell));
            var id = $"source-{row}";
            rows.Add(id, row);
            settings.Add(new(id, meal ? "Uang makan" : source.Label, source.PayoutDay,
                meal ? source.EstimateIdr?.ToString(CultureInfo.InvariantCulture) : Amount(workbook, estimateCell), meal,
                meal && WholeNumber(Cell(sheet, "M10")) is { } days and <= 31 ? (int)days : null,
                meal ? Amount(workbook, Cell(sheet, "M11")) : null, editable,
                editable ? null : "Input atau formula proyeksi belum dikenali. Edit melalui Excel."));
        }
        var cells = Enumerable.Range(2, 32).SelectMany(row => new[] { $"K{row}", $"L{row}", $"M{row}", $"N{row}", $"O{row}" });
        return new(new(month, Version(workbook, sheet, cells, ProjectionScope(month)), settings), sheetName, rows);
    }

    // Include the latest scope operation to reject ABA edits, while unrelated transaction writes do not invalidate settings.
    private static string Version(WorkbookPart workbook, WorksheetPart sheet, IEnumerable<string> cells, string scope)
    {
        var content = string.Join("\n", cells.Select(reference => reference + ":" + Cell(sheet, reference)?.OuterXml));
        var lastOperation = AuditRows(workbook).LastOrDefault(row => AuditText(row, "C") == "SETTINGS_" + scope);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content + "\n" + AuditText(lastOperation, "A"))));
    }
    internal static IEnumerable<Row> AuditRows(WorkbookPart workbook)
    {
        var sheet = workbook.Workbook!.Sheets!.Elements<Sheet>().SingleOrDefault(s => s.Name?.Value == "__AuditLog");
        return sheet is null ? [] : ((WorksheetPart)workbook.GetPartById(sheet.Id!)).Worksheet!.GetFirstChild<SheetData>()!.Elements<Row>().Skip(1);
    }
    internal static string AuditText(Row? row, string column) => row?.Elements<Cell>().SingleOrDefault(c => c.CellReference?.Value == column + row.RowIndex!.Value)?.InnerText ?? "";
    internal static SettingsOperation? ReadOperation(string path, Guid id)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        var row = AuditRows(document.WorkbookPart!).SingleOrDefault(row => AuditText(row, "A") == id.ToString("D") && AuditText(row, "C").StartsWith("SETTINGS_", StringComparison.Ordinal));
        return row is null ? null : new(id, AuditText(row, "C")[9..]);
    }
}

public sealed class WorkbookSettingsConflictException(string message) : WorkbookConflictException(message);
