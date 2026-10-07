using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Lajur.Infrastructure.Workbooks;

internal sealed partial class OpenXmlWorkbookEditor
{
    /// <summary>
    /// Read-only import preflight: checks every non-blank row of the twelve monthly tables with the
    /// same rules the migration enforces, but reports all problems with their sheet/row/column instead
    /// of stopping at the first one. Messages never contain amounts or descriptions.
    /// </summary>
    public IReadOnlyList<WorkbookImportIssue> CollectImportIssues(int? expectedYear)
    {
        var issues = new List<WorkbookImportIssue>();
        using var document = SpreadsheetDocument.Open(workbookPath, false);
        var workbookPart = RequiredWorkbookPart(document);

        foreach (var (monthNumber, month) in Months)
        {
            var worksheetPart = FindWorksheetPart(workbookPart, month.SheetName);
            var tablePart = worksheetPart.TableDefinitionParts
                .Single(candidate => candidate.Table?.Name?.Value == month.TableName);
            var range = ParseRange(RequiredTable(tablePart).Reference?.Value, month.TableName);
            var sheetData = RequiredWorksheet(worksheetPart).GetFirstChild<SheetData>()
                ?? throw InvalidWorkbook($"Sheet '{month.SheetName}' does not contain sheet data.");

            foreach (var row in sheetData.Elements<Row>().Where(row =>
                         row.RowIndex?.Value > range.StartRow
                         && row.RowIndex?.Value <= range.EndRow
                         && !IsTransactionRowBlank(sheetData, row.RowIndex.Value)))
            {
                CheckRow(workbookPart, row, expectedYear, monthNumber, month.SheetName, issues);
            }
        }

        return issues;
    }

    private void CheckRow(
        WorkbookPart workbookPart,
        Row row,
        int? year,
        int monthNumber,
        string sheetName,
        List<WorkbookImportIssue> issues)
    {
        var rowNumber = row.RowIndex!.Value;
        void Add(string column, string message) =>
            issues.Add(new WorkbookImportIssue(sheetName, rowNumber, column, message));

        var dateText = ReadCellText(workbookPart, FindCell(row, 1));
        // Type and category are compared exactly (untrimmed), like the migration itself.
        var type = ReadCellText(workbookPart, FindCell(row, 2));
        var payment = ReadCellText(workbookPart, FindCell(row, 3))?.Trim() ?? string.Empty;
        var category = ReadCellText(workbookPart, FindCell(row, 4));
        var amountText = ReadCellText(workbookPart, FindCell(row, 5));

        if (string.IsNullOrWhiteSpace(dateText))
        {
            Add("A", "Tanggal kosong.");
        }
        else if (!double.TryParse(dateText, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
                 || serial < 1
                 || serial > 2958465)
        {
            Add("A", "Tanggal bukan tanggal Excel yang valid (mungkin tersimpan sebagai teks).");
        }
        else
        {
            var date = DateOnly.FromDateTime(DateTime.FromOADate(serial));
            if (date.Month != monthNumber || (year is not null && date.Year != year))
            {
                Add("A", year is null
                    ? $"Tanggal {date:dd-MM-yyyy} tidak berada di bulan sheet ini (bulan {monthNumber:00})."
                    : $"Tanggal {date:dd-MM-yyyy} tidak berada di bulan sheet ini ({monthNumber:00}/{year}).");
            }
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            Add("B", "Tipe kosong.");
        }
        else if (!masterData.TransactionTypes.Contains(type, StringComparer.Ordinal))
        {
            Add("B", masterData.TransactionTypes.Contains(type.Trim(), StringComparer.Ordinal)
                ? $"Tipe '{type.Trim()}' mengandung spasi berlebih di awal/akhir."
                : $"Tipe '{type}' tidak ada di Master.");
        }

        if (payment.Length > 0 && !masterData.PaymentMethods.Contains(payment, StringComparer.Ordinal))
        {
            Add("C", $"Metode pembayaran '{payment}' tidak ada di Master.");
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            Add("D", "Kategori kosong.");
        }
        else
        {
            var masterCategory = masterData.Categories.FirstOrDefault(candidate => candidate.Name == category);
            if (masterCategory is null)
            {
                Add("D", masterData.Categories.Any(candidate => candidate.Name == category.Trim())
                    ? $"Kategori '{category.Trim()}' mengandung spasi berlebih di awal/akhir."
                    : $"Kategori '{category}' tidak ada di Master.");
            }
            else if (!string.IsNullOrWhiteSpace(type)
                     && masterData.TransactionTypes.Contains(type, StringComparer.Ordinal)
                     && !string.Equals(masterCategory.Kind, type, StringComparison.Ordinal))
            {
                Add("D", $"Kategori '{category}' terdaftar sebagai {masterCategory.Kind} di Master, tetapi baris ini bertipe {type}.");
            }
        }

        if (string.IsNullOrWhiteSpace(amountText))
        {
            Add("E", "Jumlah kosong.");
        }
        else if (!long.TryParse(amountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
        {
            Add("E", "Jumlah harus bilangan bulat rupiah tanpa desimal.");
        }
        else if (amount <= 0)
        {
            Add("E", "Jumlah harus lebih dari nol.");
        }
    }
}

public sealed record WorkbookImportIssue(string Sheet, uint Row, string Column, string Message)
{
    public string Cell => $"{Column}{Row}";
}
