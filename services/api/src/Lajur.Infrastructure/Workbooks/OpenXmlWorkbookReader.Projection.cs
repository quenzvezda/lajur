using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Lajur.Infrastructure.Workbooks;

/// <summary>One row of the "A. PROYEKSI KAS BULAN INI" block (columns K:P).</summary>
public sealed record WorkbookIncomeSourceInput(
    uint Row,
    string Label,
    int? PayoutDay,
    long? EstimateIdr,
    bool EstimateRecognized,
    string? IncomeCategory,
    long? PaidThresholdIdr);

public sealed partial class OpenXmlWorkbookReader
{
    private const int MaximumIncomeSources = 30;

    /// <summary>
    /// Reads the income sources of a monthly cash-projection block. Only known formula shapes are mapped:
    /// Aktual = SUMIFS(&lt;table&gt;, "Pemasukan", Kategori "X") and Belum cair = IF(OR(N&gt;threshold, TODAY()&gt;$AD$2), 0, N(M)).
    /// Unknown shapes leave the category/threshold null so the report shows "belum dipetakan" instead of
    /// a guess. Returns null when the block is not where the template puts it.
    /// </summary>
    public IReadOnlyList<WorkbookIncomeSourceInput>? ReadIncomeSourceInputs(string workbookPath, int month)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        if (!Months.TryGetValue(month, out var monthDefinition))
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be between 1 and 12.");
        }

        using var document = SpreadsheetDocument.Open(workbookPath, false);
        var workbookPart = document.WorkbookPart
            ?? throw InvalidWorkbook("The package does not contain a workbook part.");
        var sheet = workbookPart.Workbook?.Sheets?
            .Elements<Sheet>()
            .SingleOrDefault(candidate => candidate.Name?.Value == monthDefinition.SheetName);
        if (sheet?.Id?.Value is not { } relationshipId
            || workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart
            || worksheetPart.Worksheet?.GetFirstChild<SheetData>() is not { } sheetData)
        {
            return null;
        }

        var rows = sheetData.Elements<Row>()
            .Where(row => row.RowIndex?.Value is >= 2 and <= 2 + MaximumIncomeSources + 1)
            .ToDictionary(row => row.RowIndex!.Value);
        string? Text(uint rowNumber, int column) =>
            rows.TryGetValue(rowNumber, out var row) ? ReadCellText(workbookPart, FindCell(row, column))?.Trim() : null;

        if (Text(2, 11) != "Sumber" || Text(2, 12) != "Cair tgl" || Text(2, 13) != "Estimasi" || Text(2, 14) != "Aktual")
        {
            return null;
        }

        var sources = new List<WorkbookIncomeSourceInput>();
        for (uint rowNumber = 3; rowNumber < 3 + MaximumIncomeSources; rowNumber++)
        {
            var label = Text(rowNumber, 11);
            if (string.Equals(label, "TOTAL", StringComparison.Ordinal))
            {
                return sources;
            }

            if (!rows.TryGetValue(rowNumber, out var row)
                || (string.IsNullOrWhiteSpace(label) && FindCell(row, 14)?.CellFormula is null))
            {
                continue;
            }

            // A formula label (e.g. "Uang Makan (hari kerja …)") is display text only; fall back when uncached.
            label = string.IsNullOrWhiteSpace(label) ? $"Sumber baris {rowNumber}" : label;

            var (estimate, estimateRecognized) = ReadEstimate(workbookPart, sheetData, FindCell(row, 13));
            // Formula-derived cached labels can mention an old month; use stable presentation text.
            if (FindCell(row, 11)?.CellFormula is not null && FindCell(row, 13)?.CellFormula?.Text == "$M$10*$M$11") label = "Uang makan";
            var category = MatchIncomeCategory(FindCell(row, 14)?.CellFormula?.Text, monthDefinition.TableName);
            var threshold = MatchPaidThreshold(FindCell(row, 15)?.CellFormula?.Text, rowNumber);
            sources.Add(new WorkbookIncomeSourceInput(
                rowNumber,
                label!,
                ReadLiteralWholeNumber(FindCell(row, 12)) is { } day and >= 1 and <= 31 ? (int)day : null,
                estimate,
                estimateRecognized,
                category,
                threshold));
        }

        // No TOTAL row within the expected range: the layout is not the one we know.
        return null;
    }

    private static (long? Value, bool Recognized) ReadEstimate(WorkbookPart workbookPart, SheetData sheetData, Cell? cell)
    {
        if (cell is null || (cell.CellFormula is null && string.IsNullOrWhiteSpace(cell.CellValue?.InnerText)))
        {
            return (null, true); // empty input: Excel treats it as 0 ("isi estimasi dulu")
        }

        if (cell.CellFormula is null)
        {
            return ReadLiteralWholeNumber(cell) is { } literal ? (literal, true) : (null, false);
        }

        // Known template shape: =$M$10*$M$11 (working days × meal allowance), both literal inputs.
        var match = ProductOfInputsRegex().Match(cell.CellFormula.Text ?? string.Empty);
        if (!match.Success)
        {
            return (null, false);
        }

        var left = ReadLiteralWholeNumber(FindCellByReference(sheetData, $"M{match.Groups[1].Value}"));
        var right = ReadLiteralWholeNumber(FindCellByReference(sheetData, $"M{match.Groups[2].Value}"));
        return left is not null && right is not null
            ? (checked(left.Value * right.Value), true)
            : (null, true); // an empty assumption cell counts as 0 in Excel
    }

    private static string? MatchIncomeCategory(string? formula, string tableName)
    {
        var match = IncomeSumRegex().Match(formula ?? string.Empty);
        return match.Success
               && match.Groups[1].Value == tableName
               && match.Groups[2].Value == tableName
               && match.Groups[3].Value == tableName
            ? match.Groups[4].Value
            : null;
    }

    private static long? MatchPaidThreshold(string? formula, uint rowNumber)
    {
        var match = PaidThresholdRegex().Match(formula ?? string.Empty);
        return match.Success
               && match.Groups[1].Value == rowNumber.ToString(CultureInfo.InvariantCulture)
               && match.Groups[3].Value == rowNumber.ToString(CultureInfo.InvariantCulture)
               && long.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var threshold)
            ? threshold
            : null;
    }

    private static long? ReadLiteralWholeNumber(Cell? cell)
    {
        if (cell is null || cell.CellFormula is not null
            || (cell.DataType?.Value is { } type && type != CellValues.Number))
        {
            return null;
        }

        return decimal.TryParse(cell.CellValue?.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
               && value == decimal.Truncate(value)
               && value is >= long.MinValue and <= long.MaxValue
            ? decimal.ToInt64(value)
            : null;
    }

    private static Cell? FindCellByReference(SheetData sheetData, string reference) =>
        sheetData.Descendants<Cell>().FirstOrDefault(cell => cell.CellReference?.Value == reference);

    [GeneratedRegex("^\\$M\\$([0-9]+)\\*\\$M\\$([0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ProductOfInputsRegex();

    [GeneratedRegex(
        "^SUMIFS\\(([A-Za-z]+)\\[Jumlah \\(Rp\\)\\],([A-Za-z]+)\\[Tipe\\],\"Pemasukan\",([A-Za-z]+)\\[Kategori\\],\"([^\"]+)\"\\)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex IncomeSumRegex();

    [GeneratedRegex(
        "^IF\\(OR\\(N([0-9]+)>([0-9]+),TODAY\\(\\)>\\$AD\\$2\\),0,N\\(M([0-9]+)\\)\\)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PaidThresholdRegex();
}
