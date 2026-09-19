using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Lajur.Infrastructure.Workbooks;

public sealed partial class OpenXmlWorkbookReader
{
    private static readonly string[] ExpectedHeaders =
    [
        "Tanggal",
        "Tipe",
        "Pembayaran",
        "Kategori",
        "Jumlah (Rp)",
        "Keterangan"
    ];

    private static readonly IReadOnlyDictionary<int, MonthDefinition> Months =
        new Dictionary<int, MonthDefinition>
        {
            [1] = new("Jan", "JanTable"),
            [2] = new("Feb", "FebTable"),
            [3] = new("Mar", "MarTable"),
            [4] = new("April", "AprilTable"),
            [5] = new("May", "MayTable"),
            [6] = new("June", "JuneTable"),
            [7] = new("July", "JulyTable"),
            [8] = new("Aug", "AugTable"),
            [9] = new("Sep", "SepTable"),
            [10] = new("Okt", "OktTable"),
            [11] = new("Nov", "NovTable"),
            [12] = new("Des", "DesTable")
        };

    public IReadOnlyList<WorkbookTransactionRow> ReadMonthlyTransactions(
        string workbookPath,
        int month)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);

        if (!Months.TryGetValue(month, out var monthDefinition))
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be between 1 and 12.");
        }

        using var document = SpreadsheetDocument.Open(workbookPath, false);
        var workbookPart = document.WorkbookPart
            ?? throw InvalidWorkbook("The package does not contain a workbook part.");
        var workbook = workbookPart.Workbook
            ?? throw InvalidWorkbook("The workbook part does not contain a workbook definition.");

        if (workbookPart.VbaProjectPart is null)
        {
            throw InvalidWorkbook("The workbook does not contain the required VBA project.");
        }

        var sheet = workbook.Sheets?
            .Elements<Sheet>()
            .SingleOrDefault(candidate => candidate.Name?.Value == monthDefinition.SheetName)
            ?? throw InvalidWorkbook($"Required sheet '{monthDefinition.SheetName}' was not found.");

        if (sheet.Id?.Value is not { } relationshipId
            || workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart)
        {
            throw InvalidWorkbook($"Sheet '{monthDefinition.SheetName}' has no worksheet part.");
        }

        var tablePart = worksheetPart.TableDefinitionParts
            .SingleOrDefault(candidate => candidate.Table?.Name?.Value == monthDefinition.TableName)
            ?? throw InvalidWorkbook(
                $"Required table '{monthDefinition.TableName}' was not found on sheet '{monthDefinition.SheetName}'.");

        ValidateHeaders(tablePart.Table, monthDefinition.TableName);
        var range = ParseTableRange(tablePart.Table?.Reference?.Value, monthDefinition.TableName);

        if (range.StartColumn != "A" || range.EndColumn != "F")
        {
            throw InvalidWorkbook(
                $"Table '{monthDefinition.TableName}' must occupy columns A:F, but its reference is '{tablePart.Table?.Reference?.Value}'.");
        }

        var worksheet = worksheetPart.Worksheet
            ?? throw InvalidWorkbook($"Sheet '{monthDefinition.SheetName}' does not contain a worksheet definition.");
        var sheetData = worksheet.GetFirstChild<SheetData>()
            ?? throw InvalidWorkbook($"Sheet '{monthDefinition.SheetName}' does not contain sheet data.");

        var transactions = new List<WorkbookTransactionRow>();

        foreach (var row in sheetData.Elements<Row>().Where(candidate =>
                     candidate.RowIndex?.Value > range.StartRow
                     && candidate.RowIndex?.Value <= range.EndRow))
        {
            var values = Enumerable.Range(1, ExpectedHeaders.Length)
                .Select(column => ReadCellText(workbookPart, FindCell(row, column)))
                .ToArray();

            if (values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            transactions.Add(ParseTransaction(values, monthDefinition, row.RowIndex!.Value));
        }

        return transactions;
    }

    private static WorkbookTransactionRow ParseTransaction(
        IReadOnlyList<string?> values,
        MonthDefinition month,
        uint rowNumber)
    {
        var date = ParseDate(Required(values[0], month, rowNumber, ExpectedHeaders[0]), month, rowNumber);
        var type = Required(values[1], month, rowNumber, ExpectedHeaders[1]);
        var paymentMethod = Required(values[2], month, rowNumber, ExpectedHeaders[2]);
        var category = Required(values[3], month, rowNumber, ExpectedHeaders[3]);
        var amount = ParseAmount(Required(values[4], month, rowNumber, ExpectedHeaders[4]), month, rowNumber);
        var description = string.IsNullOrWhiteSpace(values[5]) ? null : values[5];

        return new WorkbookTransactionRow(date, type, paymentMethod, category, amount, description);
    }

    private static string Required(string? value, MonthDefinition month, uint rowNumber, string column)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw InvalidWorkbook(
                $"Table '{month.TableName}' row {rowNumber} has no value for required column '{column}'.");
        }

        return value;
    }

    private static DateOnly ParseDate(string value, MonthDefinition month, uint rowNumber)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serialDate))
        {
            return DateOnly.FromDateTime(DateTime.FromOADate(serialDate));
        }

        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw InvalidWorkbook($"Table '{month.TableName}' row {rowNumber} contains an invalid date '{value}'.");
    }

    private static long ParseAmount(string value, MonthDefinition month, uint rowNumber)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            || amount != decimal.Truncate(amount)
            || amount < long.MinValue
            || amount > long.MaxValue)
        {
            throw InvalidWorkbook($"Table '{month.TableName}' row {rowNumber} contains an invalid IDR amount '{value}'.");
        }

        return decimal.ToInt64(amount);
    }

    private static Cell? FindCell(Row row, int columnIndex)
    {
        var expectedColumn = ColumnName(columnIndex);

        return row.Elements<Cell>().FirstOrDefault(cell =>
            CellReferenceRegex().Match(cell.CellReference?.Value ?? string.Empty).Groups[1].Value == expectedColumn);
    }

    private static string? ReadCellText(WorkbookPart workbookPart, Cell? cell)
    {
        if (cell is null)
        {
            return null;
        }

        var rawValue = cell.CellValue?.InnerText ?? cell.InnerText;

        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (!int.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                throw InvalidWorkbook($"Cell '{cell.CellReference}' contains an invalid shared-string index.");
            }

            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable
                ?? throw InvalidWorkbook("A cell references the shared-string table, but the package has no shared-string part.");

            return sharedStrings.Elements<SharedStringItem>().ElementAtOrDefault(index)?.InnerText
                ?? throw InvalidWorkbook($"Cell '{cell.CellReference}' references missing shared string {index}.");
        }

        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return cell.InlineString?.InnerText;
        }

        return rawValue;
    }

    private static void ValidateHeaders(Table? table, string tableName)
    {
        var actualHeaders = table?.TableColumns?
            .Elements<TableColumn>()
            .Select(column => column.Name?.Value ?? string.Empty)
            .ToArray()
            ?? [];

        if (!actualHeaders.SequenceEqual(ExpectedHeaders, StringComparer.Ordinal))
        {
            throw InvalidWorkbook(
                $"Table '{tableName}' headers do not match the required A:F transaction contract.");
        }
    }

    private static TableRange ParseTableRange(string? reference, string tableName)
    {
        var match = TableRangeRegex().Match(reference ?? string.Empty);

        if (!match.Success
            || !uint.TryParse(match.Groups[2].Value, CultureInfo.InvariantCulture, out var startRow)
            || !uint.TryParse(match.Groups[4].Value, CultureInfo.InvariantCulture, out var endRow)
            || endRow < startRow)
        {
            throw InvalidWorkbook($"Table '{tableName}' has an invalid reference '{reference}'.");
        }

        return new TableRange(match.Groups[1].Value, startRow, match.Groups[3].Value, endRow);
    }

    private static string ColumnName(int columnIndex)
    {
        var name = string.Empty;

        while (columnIndex > 0)
        {
            columnIndex--;
            name = (char)('A' + (columnIndex % 26)) + name;
            columnIndex /= 26;
        }

        return name;
    }

    private static InvalidDataException InvalidWorkbook(string message) => new(message);

    [GeneratedRegex("^([A-Z]+)([0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex CellReferenceRegex();

    [GeneratedRegex("^\\$?([A-Z]+)\\$?([0-9]+):\\$?([A-Z]+)\\$?([0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex TableRangeRegex();

    private sealed record MonthDefinition(string SheetName, string TableName);

    private sealed record TableRange(string StartColumn, uint StartRow, string EndColumn, uint EndRow);
}
