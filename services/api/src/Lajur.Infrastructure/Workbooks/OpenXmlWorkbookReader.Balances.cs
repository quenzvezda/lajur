using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Lajur.Infrastructure.Workbooks;

public sealed record WorkbookMonthBalanceInputs(long? OpeningBalanceIdr, long? ActualBalanceIdr);

public sealed partial class OpenXmlWorkbookReader
{
    private const string ActualBalanceLabel = "Saldo Aktual";
    private const string OpeningBalanceLabel = "Opening Balance";

    /// <summary>
    /// Reads the manual balance inputs at the top of a monthly sheet (A1/B1 "Saldo Aktual",
    /// C1/D1 "Opening Balance"). A value is returned only when its label matches, the cell holds a
    /// literal whole-rupiah number and no formula. Anything else is reported as missing, never as zero.
    /// </summary>
    public WorkbookMonthBalanceInputs ReadMonthBalanceInputs(string workbookPath, int month)
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
            || workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart)
        {
            return new WorkbookMonthBalanceInputs(null, null);
        }

        var firstRow = worksheetPart.Worksheet?
            .GetFirstChild<SheetData>()?
            .Elements<Row>()
            .FirstOrDefault(row => row.RowIndex?.Value == 1);
        if (firstRow is null)
        {
            return new WorkbookMonthBalanceInputs(null, null);
        }

        return new WorkbookMonthBalanceInputs(
            ReadLabeledInput(workbookPart, firstRow, labelColumn: 3, OpeningBalanceLabel),
            ReadLabeledInput(workbookPart, firstRow, labelColumn: 1, ActualBalanceLabel));
    }

    private static long? ReadLabeledInput(WorkbookPart workbookPart, Row row, int labelColumn, string expectedLabel)
    {
        var label = ReadCellText(workbookPart, FindCell(row, labelColumn))?.Trim();
        if (!string.Equals(label, expectedLabel, StringComparison.Ordinal))
        {
            return null;
        }

        var valueCell = FindCell(row, labelColumn + 1);
        if (valueCell is null || valueCell.CellFormula is not null)
        {
            return null;
        }

        if (valueCell.DataType?.Value is { } dataType && dataType != CellValues.Number)
        {
            return null;
        }

        var raw = valueCell.CellValue?.InnerText;
        if (string.IsNullOrWhiteSpace(raw)
            || !decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)
            || amount != decimal.Truncate(amount)
            || amount < long.MinValue
            || amount > long.MaxValue)
        {
            return null;
        }

        return decimal.ToInt64(amount);
    }
}
