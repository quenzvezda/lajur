using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Lajur.Infrastructure.Workbooks;

/// <summary>
/// Recognizes only the workbook's bounded monthly calendar helpers, not general Excel formulas.
/// Reader and generator share this contract so an accepted series can be safely re-yeared.
/// </summary>
internal static partial class MonthlyDateHelpers
{
    public static int ReadYear(SheetData sheetData, string sheetName, int month) =>
        Validate(sheetData, sheetName, month).Year;

    public static void PatchYear(SheetData sheetData, string sheetName, int month, int targetYear)
    {
        var helpers = Validate(sheetData, sheetName, month);
        foreach (var cell in helpers.Days.Prepend(helpers.MonthStart))
        {
            var formula = cell.CellFormula!;
            var directDate = DirectDateRegex().Match(formula.Text);
            if (directDate.Success)
            {
                var year = directDate.Groups["year"];
                formula.Text = formula.Text[..year.Index]
                    + targetYear.ToString(CultureInfo.InvariantCulture)
                    + formula.Text[(year.Index + year.Length)..];
            }
            // Relative expressions/shared followers stay byte-for-byte formula-equivalent.
            // Every helper cache refers to the old year and must be recalculated by Excel.
            cell.CellValue = null;
        }
    }

    private static ValidatedHelpers Validate(SheetData sheetData, string sheetName, int month)
    {
        var monthStart = RequiredCell(sheetData, sheetName, "AD1");
        var startFormula = RequiredFormula(monthStart, sheetName);
        var start = DirectDateRegex().Match(startFormula.Text);
        if (startFormula.FormulaType?.Value is { } startType && startType != CellFormulaValues.Normal
            || !start.Success || start.Groups["day"].Value != "1"
            || ParseNumber(start.Groups["month"].Value) != month)
        {
            throw Unexpected(sheetName, "AD1");
        }
        var year = ParseNumber(start.Groups["year"].Value);
        if (year is < 1900 or > 9999) throw Unexpected(sheetName, "AD1");

        var days = Enumerable.Range(2, 31)
            .Select(row => RequiredCell(sheetData, sheetName, $"AG{row}"))
            .ToArray();
        var groups = new Dictionary<uint, SharedGroup>();
        for (var index = 0; index < days.Length; index++)
        {
            var row = index + 2;
            var formula = RequiredFormula(days[index], sheetName);
            var type = formula.FormulaType?.Value ?? CellFormulaValues.Normal;
            if (type != CellFormulaValues.Normal && type != CellFormulaValues.Shared)
            {
                throw Unexpected(sheetName, $"AG{row}");
            }
            if (type == CellFormulaValues.Shared)
            {
                if (formula.SharedIndex?.Value is not { } sharedIndex) throw Unexpected(sheetName, $"AG{row}");
                if (string.IsNullOrEmpty(formula.Text)) continue;
                var range = SharedRangeRegex().Match(formula.Reference?.Value ?? string.Empty);
                if (!range.Success) throw Unexpected(sheetName, $"AG{row}");
                var first = ParseNumber(range.Groups["first"].Value);
                var last = ParseNumber(range.Groups["last"].Value);
                if (first != row || last < first || last > 32
                    || !groups.TryAdd(sharedIndex, new SharedGroup(first, last)))
                {
                    throw Unexpected(sheetName, $"AG{row}");
                }
            }
            ValidateDayFormula(formula.Text, sheetName, row, year, month);
        }

        // A shared follower may omit its formula text only within a validated local AG group.
        // Validate the whole range; never borrow an unrelated or out-of-bounds shared anchor.
        for (var index = 0; index < days.Length; index++)
        {
            var row = index + 2;
            var formula = days[index].CellFormula!;
            if (formula.FormulaType?.Value != CellFormulaValues.Shared) continue;
            if (!groups.TryGetValue(formula.SharedIndex!.Value, out var group)
                || row < group.First || row > group.Last
                || row != group.First && (!string.IsNullOrEmpty(formula.Text) || formula.Reference is not null))
            {
                throw Unexpected(sheetName, $"AG{row}");
            }
        }
        foreach (var (sharedIndex, group) in groups)
        {
            for (var row = group.First; row <= group.Last; row++)
            {
                var formula = days[row - 2].CellFormula!;
                if (formula.FormulaType?.Value != CellFormulaValues.Shared || formula.SharedIndex?.Value != sharedIndex)
                {
                    throw Unexpected(sheetName, $"AG{row}");
                }
            }
        }
        return new ValidatedHelpers(year, monthStart, days);
    }

    private static void ValidateDayFormula(string formula, string sheetName, int row, int year, int month)
    {
        var direct = DirectDateRegex().Match(formula);
        if (direct.Success && ParseNumber(direct.Groups["year"].Value) == year
            && ParseNumber(direct.Groups["month"].Value) == month
            && ParseNumber(direct.Groups["row"].Value) == row)
        {
            return;
        }
        var relative = RelativeDateRegex().Match(formula);
        if (relative.Success && ParseNumber(relative.Groups["row"].Value) == row) return;
        throw Unexpected(sheetName, $"AG{row}");
    }

    private static Cell RequiredCell(SheetData sheetData, string sheetName, string reference) =>
        sheetData.Elements<Row>().SelectMany(row => row.Elements<Cell>())
            .SingleOrDefault(cell => cell.CellReference?.Value == reference)
        ?? throw new InvalidDataException($"Required year helper '{sheetName}!{reference}' was not found.");

    private static CellFormula RequiredFormula(Cell cell, string sheetName) => cell.CellFormula
        ?? throw new InvalidDataException($"Required year helper '{sheetName}!{cell.CellReference}' has no formula.");

    private static int ParseNumber(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : -1;

    private static InvalidDataException Unexpected(string sheetName, string reference) =>
        new($"Required year helper '{sheetName}!{reference}' has an unexpected formula.");

    [GeneratedRegex(@"^\s*DATE\(\s*(?<year>[0-9]{4})\s*,\s*(?<month>[0-9]{1,2})\s*,\s*(?<day>1|AF(?<row>[0-9]{1,2}))\s*\)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DirectDateRegex();

    [GeneratedRegex(@"^\s*\$AD\$1\s*\+\s*AF(?<row>[0-9]{1,2})\s*-\s*1\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RelativeDateRegex();

    [GeneratedRegex(@"^\$?AG\$?(?<first>[0-9]{1,2}):\$?AG\$?(?<last>[0-9]{1,2})$", RegexOptions.CultureInvariant)]
    private static partial Regex SharedRangeRegex();

    private sealed record SharedGroup(int First, int Last);
    private sealed record ValidatedHelpers(int Year, Cell MonthStart, IReadOnlyList<Cell> Days);
}
