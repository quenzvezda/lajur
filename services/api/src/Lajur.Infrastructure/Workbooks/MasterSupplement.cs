using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Lajur.Infrastructure.Workbooks;

// A managed additional report, leaving the owner's existing Rekap/charts/formulas intact.
internal static partial class MasterSupplement
{
    internal const string SheetName = "LajurMaster";
    private sealed record Value(string Text, bool Formula = false, uint? Style = null);
    private static Dictionary<string, Value> Plan(WorkbookPart workbook, WorkbookMasterData master)
    {
        var result = new Dictionary<string, Value>(StringComparer.Ordinal);
        var source = WorkbookSettingsMap.Sheet(workbook, "Rekap");
        uint? Style(string cell) => WorkbookSettingsMap.Cell(source, cell)?.StyleIndex?.Value;
        void Text(string cell, string text, string style = "A23") => result.Add(cell, new(text, Style: Style(style)));
        void Formula(string cell, string formula, string style = "B23") => result.Add(cell, new(formula, true, Style(style)));
        Text("A1", $"LAJUR — REKAP MASTER {master.WorkbookYear}", "A21");
        Text("A2", "LajurMaster/v1");
        Text("A3", "Laporan dikelola aplikasi. Input tetap di Master dan tabel transaksi.");
        var headers = new[] { "Kategori pengeluaran", "Jan", "Feb", "Mar", "Apr", "Mei", "Jun", "Jul", "Agu", "Sep", "Okt", "Nov", "Des", "Total", "Budget / bulan", "Rata-rata / bulan terisi", "Selisih vs budget" };
        for (var c = 1; c <= headers.Length; c++) Text(Column(c) + "4", headers[c - 1], "A22");
        Text("S4", "Metode pembayaran", "A22");
        for (var month = 0; month < 12; month++) Text(Column(20 + month) + "4", headers[month + 1], "A22");
        Text("AF4", "Total", "A22");
        var tableNames = new[] { "JanTable", "FebTable", "MarTable", "AprilTable", "MayTable", "JuneTable", "JulyTable", "AugTable", "SepTable", "OktTable", "NovTable", "DesTable" };
        var masterSheet = WorkbookSettingsMap.Sheet(workbook, "Master");
        var expenses = master.Categories.Where(c => c.Kind == "Pengeluaran").ToArray();
        for (var index = 0; index < expenses.Length; index++)
        {
            var row = index + 5;
            var masterRow = Enumerable.Range(5, 77).Single(r => WorkbookSettingsMap.Text(workbook, WorkbookSettingsMap.Cell(masterSheet, $"A{r}")) == expenses[index].Name);
            Formula($"A{row}", $"Master!$A${masterRow}", "A23");
            for (var month = 0; month < 12; month++) Formula($"{Column(month + 2)}{row}", $"SUMIFS({tableNames[month]}[Jumlah (Rp)],{tableNames[month]}[Tipe],\"Pengeluaran\",{tableNames[month]}[Kategori],{Criteria("$A" + row)})");
            Formula($"N{row}", $"SUM(B{row}:M{row})");
            Formula($"O{row}", $"N(Master!$C${masterRow})");
            var filled = string.Join("+", tableNames.Select(t => $"--(COUNTA({t}[Tanggal])>0)"));
            Formula($"P{row}", $"IFERROR(N{row}/({filled}),0)");
            Formula($"Q{row}", $"P{row}-O{row}");
        }
        var unknown = expenses.Length + 5;
        Text($"A{unknown}", "Di luar Master");
        for (var month = 0; month < 12; month++)
        {
            var column = Column(month + 2);
            var sum = expenses.Length == 0 ? "0" : $"SUM({column}5:{column}{unknown - 1})";
            Formula($"{column}{unknown}", $"SUMIFS({tableNames[month]}[Jumlah (Rp)],{tableNames[month]}[Tipe],\"Pengeluaran\")-{sum}");
        }
        Formula($"N{unknown}", $"SUM(B{unknown}:M{unknown})");
        var totalRow = unknown + 1;
        Text($"A{totalRow}", "TOTAL", "A46");
        for (var column = 2; column <= 17; column++) Formula($"{Column(column)}{totalRow}", $"SUM({Column(column)}5:{Column(column)}{unknown})", "B46");
        for (var index = 0; index <= master.PaymentMethods.Count; index++)
        {
            var row = index + 5;
            if (index == master.PaymentMethods.Count) Text($"S{row}", "Belum diisi");
            else Formula($"S{row}", $"Master!$G${index + 5}", "A23");
            for (var month = 0; month < 12; month++)
                Formula($"{Column(20 + month)}{row}", $"SUMIFS({tableNames[month]}[Jumlah (Rp)],{tableNames[month]}[Tipe],\"Pengeluaran\",{tableNames[month]}[Pembayaran],{(index == master.PaymentMethods.Count ? "\"\"" : Criteria("$S" + row))})");
            Formula($"AF{row}", $"SUM(T{row}:AE{row})");
        }
        var paymentTotal = master.PaymentMethods.Count + 6;
        Text($"S{paymentTotal}", "TOTAL", "A46");
        for (var column = 20; column <= 32; column++) Formula($"{Column(column)}{paymentTotal}", $"SUM({Column(column)}5:{Column(column)}{paymentTotal - 1})", "B46");
        return result;
    }

    internal static void Validate(WorkbookPart workbook, WorkbookMasterData master)
    {
        if (!workbook.Workbook!.Sheets!.Elements<Sheet>().Any(s => s.Name == SheetName)) return;
        var sheet = WorkbookSettingsMap.Sheet(workbook, SheetName);
        var expected = Plan(workbook, master);
        var actual = sheet.Worksheet!.Descendants<Cell>().Where(c => c.CellFormula is not null || WorkbookSettingsMap.Text(workbook, c).Length > 0).ToDictionary(c => c.CellReference!.Value!);
        if (actual.Count != expected.Count || expected.Any(p => !actual.TryGetValue(p.Key, out var cell)
            || (p.Value.Formula ? ExpandedFormula(sheet, cell) != p.Value.Text : cell.CellFormula is not null || WorkbookSettingsMap.Text(workbook, cell) != p.Value.Text)))
            throw new ArgumentException("Sheet LajurMaster berisi struktur atau formula yang berbeda. Periksa melalui Excel sebelum mengubah Master.");
    }

    internal static void Write(WorkbookPart workbook, WorkbookMasterData master)
    {
        var definition = workbook.Workbook!.Sheets!.Elements<Sheet>().SingleOrDefault(s => s.Name == SheetName);
        WorksheetPart sheet;
        if (definition is null)
        {
            sheet = workbook.AddNewPart<WorksheetPart>();
            workbook.Workbook.Sheets.Append(new Sheet { Name = SheetName, SheetId = workbook.Workbook.Sheets.Elements<Sheet>().Max(s => s.SheetId!.Value) + 1, Id = workbook.GetIdOfPart(sheet) });
        }
        else sheet = (WorksheetPart)workbook.GetPartById(definition.Id!);
        var plan = Plan(workbook, master);
        var data = new SheetData();
        foreach (var group in plan.GroupBy(p => RowNumber(p.Key)).OrderBy(g => g.Key))
        {
            var row = new Row { RowIndex = (uint)group.Key };
            foreach (var (reference, value) in group.OrderBy(p => ColumnNumber(p.Key)))
            {
                var cell = new Cell { CellReference = reference, StyleIndex = value.Style is { } style ? new UInt32Value(style) : null };
                if (value.Formula) cell.CellFormula = new CellFormula(value.Text);
                else { cell.DataType = CellValues.InlineString; cell.InlineString = new InlineString(new Text(value.Text)); }
                row.Append(cell);
            }
            data.Append(row);
        }
        sheet.Worksheet = new Worksheet(new SheetViews(new SheetView { WorkbookViewId = 0, Pane = new Pane { HorizontalSplit = 1, VerticalSplit = 4, TopLeftCell = "B5", State = PaneStateValues.Frozen, ActivePane = PaneValues.BottomRight } }),
            new Columns(new Column { Min = 1, Max = 1, Width = 28, CustomWidth = true }, new Column { Min = 2, Max = 17, Width = 16, CustomWidth = true }, new Column { Min = 18, Max = 18, Width = 3, CustomWidth = true }, new Column { Min = 19, Max = 19, Width = 24, CustomWidth = true }, new Column { Min = 20, Max = 32, Width = 16, CustomWidth = true }), data);
        sheet.Worksheet.Save();
    }

    // Excel may compress generated formulas into shared groups on export/edit/import.
    internal static string ExpandedFormula(WorksheetPart sheet, Cell cell)
    {
        var formula = cell.CellFormula;
        if (formula is null) return "";
        if (!string.IsNullOrEmpty(formula.Text)) return formula.Text;
        if (formula.FormulaType?.Value != CellFormulaValues.Shared || formula.SharedIndex is null) return "";
        var anchor = sheet.Worksheet!.Descendants<Cell>().SingleOrDefault(c => c.CellFormula?.SharedIndex?.Value == formula.SharedIndex.Value && !string.IsNullOrEmpty(c.CellFormula.Text));
        if (anchor is null) throw new ArgumentException("Grup formula workbook tidak lengkap.");
        var rowDelta = RowNumber(cell.CellReference!.Value!) - RowNumber(anchor.CellReference!.Value!);
        var colDelta = ColumnNumber(cell.CellReference.Value!) - ColumnNumber(anchor.CellReference.Value!);
        return Regex.Replace(anchor.CellFormula!.Text!, @"\""(?:[^\"" ]| |\""\"")*\""|(?<![A-Za-z0-9_])(\$?)([A-Z]{1,3})(\$?)([0-9]+)(?![A-Za-z0-9_])", match =>
        {
            if (match.Value.StartsWith('"')) return match.Value;
            var column = ColumnNumber(match.Groups[2].Value + "1") + (match.Groups[1].Value.Length == 0 ? colDelta : 0);
            var row = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) + (match.Groups[3].Value.Length == 0 ? rowDelta : 0);
            return match.Groups[1].Value + Column(column) + match.Groups[3].Value + row;
        });
    }
    private static int RowNumber(string reference) => int.Parse(Regex.Match(reference, "[0-9]+$").Value, CultureInfo.InvariantCulture);
    private static string Criteria(string reference) => $"SUBSTITUTE(SUBSTITUTE(SUBSTITUTE({reference},\"~\",\"~~\"),\"*\",\"~*\"),\"?\",\"~?\")";
    private static int ColumnNumber(string reference) { var number = 0; foreach (var c in reference.TakeWhile(char.IsLetter)) number = number * 26 + c - 'A' + 1; return number; }
    private static string Column(int number) { var name = ""; while (number > 0) { number--; name = (char)('A' + number % 26) + name; number /= 26; } return name; }
}
