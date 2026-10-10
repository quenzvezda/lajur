using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Lajur.Infrastructure.Workbooks;

internal sealed partial class OpenXmlWorkbookEditor(string workbookPath, WorkbookMasterData masterData)
{
    private static readonly string[] AppIndexHeaders =
    [
        "TransactionId", "Year", "Month", "LastKnownExcelRow", "Revision", "ContentHash",
        "SnapshotJson", "IsDeleted", "CreatedAtUtc", "UpdatedAtUtc", "DeletedAtUtc"
    ];

    private static readonly string[] AuditLogHeaders =
    [
        "AuditId", "TransactionId", "Action", "Source", "OccurredAtUtc", "RevisionBefore",
        "RevisionAfter", "BeforeJson", "AfterJson", "UserId", "DeviceId", "CorrelationId"
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

    public WorkbookTransaction CreateTransaction(
        WorkbookKey key,
        CreateWorkbookTransactionCommand command,
        DateTimeOffset occurredAtUtc)
    {
        ValidateDraft(key, command.Transaction);

        using var document = SpreadsheetDocument.Open(workbookPath, true);
        var workbookPart = RequiredWorkbookPart(document);
        var indexPart = EnsureSystemSheet(workbookPart, "__AppIndex", AppIndexHeaders);
        var auditPart = EnsureSystemSheet(workbookPart, "__AuditLog", AuditLogHeaders);

        if (FindRowByFirstColumn(indexPart, command.TransactionId.ToString("D")) is not null)
        {
            var existing = ReadIndexEntry(indexPart, command.TransactionId);
            var requestedSnapshot = SerializeSnapshot(command.Transaction);

            if (!existing.IsDeleted
                && existing.Year == key.Year
                && string.Equals(existing.ContentHash, HashSnapshot(requestedSnapshot), StringComparison.Ordinal))
            {
                return ToTransaction(command.TransactionId, existing.Revision, command.Transaction);
            }

            throw new WorkbookTransactionIdConflictException(
                $"Transaction '{command.TransactionId:D}' already exists with different canonical content.");
        }

        var excelRow = AppendTransactionRow(workbookPart, command.Transaction);
        var snapshot = SerializeSnapshot(command.Transaction);
        var contentHash = HashSnapshot(snapshot);

        AppendRow(
            indexPart,
            command.TransactionId.ToString("D"),
            key.Year,
            command.Transaction.Date.Month,
            excelRow,
            1,
            contentHash,
            snapshot,
            false,
            FormatTimestamp(occurredAtUtc),
            FormatTimestamp(occurredAtUtc),
            null);

        AppendRow(
            auditPart,
            Guid.NewGuid().ToString("D"),
            command.TransactionId.ToString("D"),
            "CREATE",
            FormatSource(command.Audit.Source),
            FormatTimestamp(occurredAtUtc),
            null,
            1,
            null,
            snapshot,
            key.UserId.ToString("D"),
            command.Audit.DeviceId,
            command.Audit.CorrelationId);

        Save(document, indexPart, auditPart);
        return ToTransaction(command.TransactionId, 1, command.Transaction);
    }

    public WorkbookTransaction UpdateTransaction(
        WorkbookKey key,
        UpdateWorkbookTransactionCommand command,
        DateTimeOffset occurredAtUtc)
    {
        ValidateDraft(key, command.Transaction);

        using var document = SpreadsheetDocument.Open(workbookPath, true);
        var workbookPart = RequiredWorkbookPart(document);
        var indexPart = EnsureSystemSheet(workbookPart, "__AppIndex", AppIndexHeaders);
        var auditPart = EnsureSystemSheet(workbookPart, "__AuditLog", AuditLogHeaders);
        var indexEntry = ReadIndexEntry(indexPart, command.TransactionId);

        if (indexEntry.IsDeleted)
        {
            throw new WorkbookTransactionNotFoundException(command.TransactionId);
        }

        if (indexEntry.Revision != command.ExpectedRevision)
        {
            throw new WorkbookRevisionConflictException(
                $"Transaction '{command.TransactionId:D}' is revision {indexEntry.Revision}, not expected revision {command.ExpectedRevision}.");
        }

        var located = LocateTransactionRow(workbookPart, indexEntry);
        var before = ReadDraft(workbookPart, located.Row);
        uint newExcelRow;

        if (before.Date.Month == command.Transaction.Date.Month)
        {
            WriteTransactionRow(located.WorksheetPart, located.Row.RowIndex!.Value, command.Transaction);
            newExcelRow = located.Row.RowIndex.Value;
        }
        else
        {
            RemoveTransactionRow(located.WorksheetPart, located.TablePart, located.Range, located.Row.RowIndex!.Value);
            ShiftIndexRowsAfterRemoval(indexPart, before.Date.Month, located.Row.RowIndex.Value);
            newExcelRow = AppendTransactionRow(workbookPart, command.Transaction);
        }

        var nextRevision = indexEntry.Revision + 1;
        var afterSnapshot = SerializeSnapshot(command.Transaction);
        UpdateIndexRow(
            indexEntry,
            key.Year,
            command.Transaction.Date.Month,
            newExcelRow,
            nextRevision,
            HashSnapshot(afterSnapshot),
            afterSnapshot,
            false,
            occurredAtUtc,
            null);

        AppendRow(
            auditPart,
            Guid.NewGuid().ToString("D"),
            command.TransactionId.ToString("D"),
            "UPDATE",
            FormatSource(command.Audit.Source),
            FormatTimestamp(occurredAtUtc),
            indexEntry.Revision,
            nextRevision,
            SerializeSnapshot(before),
            afterSnapshot,
            key.UserId.ToString("D"),
            command.Audit.DeviceId,
            command.Audit.CorrelationId);

        Save(document, indexPart, auditPart);
        return ToTransaction(command.TransactionId, nextRevision, command.Transaction);
    }

    public void DeleteTransaction(
        WorkbookKey key,
        DeleteWorkbookTransactionCommand command,
        DateTimeOffset occurredAtUtc)
    {
        using var document = SpreadsheetDocument.Open(workbookPath, true);
        var workbookPart = RequiredWorkbookPart(document);
        var indexPart = EnsureSystemSheet(workbookPart, "__AppIndex", AppIndexHeaders);
        var auditPart = EnsureSystemSheet(workbookPart, "__AuditLog", AuditLogHeaders);
        var indexEntry = ReadIndexEntry(indexPart, command.TransactionId);

        if (indexEntry.IsDeleted)
        {
            throw new WorkbookTransactionNotFoundException(command.TransactionId);
        }

        if (indexEntry.Revision != command.ExpectedRevision)
        {
            throw new WorkbookRevisionConflictException(
                $"Transaction '{command.TransactionId:D}' is revision {indexEntry.Revision}, not expected revision {command.ExpectedRevision}.");
        }

        var located = LocateTransactionRow(workbookPart, indexEntry);
        var before = ReadDraft(workbookPart, located.Row);
        var beforeSnapshot = SerializeSnapshot(before);
        var nextRevision = indexEntry.Revision + 1;

        RemoveTransactionRow(located.WorksheetPart, located.TablePart, located.Range, located.Row.RowIndex!.Value);
        ShiftIndexRowsAfterRemoval(indexPart, before.Date.Month, located.Row.RowIndex.Value);
        UpdateIndexRow(
            indexEntry,
            key.Year,
            before.Date.Month,
            located.Row.RowIndex.Value,
            nextRevision,
            HashSnapshot(beforeSnapshot),
            beforeSnapshot,
            true,
            occurredAtUtc,
            occurredAtUtc);

        AppendRow(
            auditPart,
            Guid.NewGuid().ToString("D"),
            command.TransactionId.ToString("D"),
            "DELETE",
            FormatSource(command.Audit.Source),
            FormatTimestamp(occurredAtUtc),
            indexEntry.Revision,
            nextRevision,
            beforeSnapshot,
            null,
            key.UserId.ToString("D"),
            command.Audit.DeviceId,
            command.Audit.CorrelationId);

        Save(document, indexPart, auditPart);
    }

    public WorkbookMigrationResult PrepareGeneratedWorkbook(
        WorkbookKey key,
        WorkbookAuditContext audit,
        DateTimeOffset occurredAtUtc)
    {
        // Save only explicitly changed parts; reading shared strings must not reserialize them.
        using var document = SpreadsheetDocument.Open(workbookPath, true, new OpenSettings { AutoSave = false });
        var workbookPart = RequiredWorkbookPart(document);

        if (RequiredWorkbook(workbookPart).Sheets!.Elements<Sheet>().Any(sheet => sheet.Name == MasterSupplement.SheetName))
        {
            var title = WorkbookSettingsMap.Text(workbookPart, WorkbookSettingsMap.Cell(FindWorksheetPart(workbookPart, MasterSupplement.SheetName), "A1"));
            var match = Regex.Match(title, "^LAJUR — REKAP MASTER ([0-9]{4})$");
            if (!match.Success) throw InvalidWorkbook("Managed Master report has an unknown title.");
            MasterSupplement.Validate(workbookPart, masterData with { WorkbookYear = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) });
            MasterSupplement.Write(workbookPart, masterData with { WorkbookYear = key.Year });
        }
        PatchKnownYearContent(workbookPart, key.Year);
        ClearMonthlyTransactions(workbookPart);

        var indexPart = ResetSystemSheet(workbookPart, "__AppIndex", AppIndexHeaders);
        var auditPart = ResetSystemSheet(workbookPart, "__AuditLog", AuditLogHeaders);
        AppendMigrationAudit(auditPart, key, audit, occurredAtUtc, "GENERATE", 0, 0, 0);
        Save(document, indexPart, auditPart);
        return new WorkbookMigrationResult(0, 0, 0);
    }

    public WorkbookMigrationResult MigrateImportedWorkbook(
        WorkbookKey key,
        WorkbookAuditContext audit,
        DateTimeOffset occurredAtUtc)
    {
        using var document = SpreadsheetDocument.Open(workbookPath, true, new OpenSettings { AutoSave = false });
        var workbookPart = RequiredWorkbookPart(document);
        var indexPart = EnsureSystemSheet(workbookPart, "__AppIndex", AppIndexHeaders);
        var auditPart = EnsureSystemSheet(workbookPart, "__AuditLog", AuditLogHeaders);
        ValidateSystemSheet(workbookPart, "__AppIndex", AppIndexHeaders);
        ValidateSystemSheet(workbookPart, "__AuditLog", AuditLogHeaders);

        var allEntries = ReadAllIndexEntries(indexPart);
        var activeEntries = allEntries.Where(entry => !entry.IsDeleted).ToList();
        var knownIds = allEntries.Select(entry => entry.TransactionId).ToHashSet();
        var indexed = 0;
        var updated = 0;

        foreach (var candidate in ReadMigrationCandidates(workbookPart, key))
        {
            var exact = activeEntries.FirstOrDefault(entry =>
                string.Equals(entry.ContentHash, candidate.ContentHash, StringComparison.Ordinal));
            var entry = exact ?? activeEntries.FirstOrDefault(entry =>
                entry.Month == candidate.Month && entry.LastKnownExcelRow == candidate.ExcelRow);

            if (entry is null)
            {
                var transactionId = CreateMigrationId(key, candidate, knownIds);
                knownIds.Add(transactionId);
                AppendRow(
                    indexPart,
                    transactionId.ToString("D"),
                    key.Year,
                    candidate.Month,
                    candidate.ExcelRow,
                    1,
                    candidate.ContentHash,
                    candidate.SnapshotJson,
                    false,
                    FormatTimestamp(occurredAtUtc),
                    FormatTimestamp(occurredAtUtc),
                    null);
                indexed++;
                continue;
            }

            activeEntries.Remove(entry);
            var contentChanged = !string.Equals(
                entry.ContentHash,
                candidate.ContentHash,
                StringComparison.Ordinal);
            UpdateIndexRow(
                entry,
                key.Year,
                candidate.Month,
                candidate.ExcelRow,
                contentChanged ? entry.Revision + 1 : entry.Revision,
                candidate.ContentHash,
                candidate.SnapshotJson,
                false,
                occurredAtUtc,
                null);
            if (contentChanged)
            {
                updated++;
            }
        }

        foreach (var missing in activeEntries)
        {
            UpdateIndexRow(
                missing,
                missing.Year,
                missing.Month,
                missing.LastKnownExcelRow,
                missing.Revision + 1,
                missing.ContentHash,
                missing.SnapshotJson,
                true,
                occurredAtUtc,
                occurredAtUtc);
        }

        AppendMigrationAudit(
            auditPart,
            key,
            audit,
            occurredAtUtc,
            "IMPORT",
            indexed,
            updated,
            activeEntries.Count);
        Save(document, indexPart, auditPart);
        return new WorkbookMigrationResult(indexed, updated, activeEntries.Count);
    }

    public static void ValidateSystemSheets(string path)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        var workbookPart = RequiredWorkbookPart(document);
        ValidateSystemSheet(workbookPart, "__AppIndex", AppIndexHeaders);
        ValidateSystemSheet(workbookPart, "__AuditLog", AuditLogHeaders);
    }

    private static void PatchKnownYearContent(WorkbookPart workbookPart, int targetYear)
    {
        var rekapPart = FindWorksheetPart(workbookPart, "Rekap");
        var rekapData = RequiredWorksheet(rekapPart).GetFirstChild<SheetData>()
            ?? throw InvalidWorkbook("Sheet 'Rekap' does not contain sheet data.");
        var titleRow = rekapData.Elements<Row>().SingleOrDefault(row => row.RowIndex?.Value == 1)
            ?? throw InvalidWorkbook("Cell 'Rekap!A1' was not found.");
        var titleCell = FindCell(titleRow, 1)
            ?? throw InvalidWorkbook("Cell 'Rekap!A1' was not found.");
        var title = ReadCellText(workbookPart, titleCell) ?? string.Empty;
        var patchedTitle = YearTextRegex().Replace(title, targetYear.ToString(CultureInfo.InvariantCulture), 1);
        if (patchedTitle == title && !title.Contains(targetYear.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw InvalidWorkbook("Cell 'Rekap!A1' does not contain a recognizable workbook year.");
        }

        SetInlineText(titleCell, patchedTitle);
        RequiredWorksheet(rekapPart).Save();

        foreach (var (monthNumber, month) in Months)
        {
            var worksheetPart = FindWorksheetPart(workbookPart, month.SheetName);
            var sheetData = RequiredWorksheet(worksheetPart).GetFirstChild<SheetData>()
                ?? throw InvalidWorkbook($"Sheet '{month.SheetName}' does not contain sheet data.");
            MonthlyDateHelpers.PatchYear(sheetData, month.SheetName, monthNumber, targetYear);
            RequiredWorksheet(worksheetPart).Save();
        }
    }

    private static void ClearMonthlyTransactions(WorkbookPart workbookPart)
    {
        foreach (var month in Months.Values)
        {
            var worksheetPart = FindWorksheetPart(workbookPart, month.SheetName);
            var tablePart = worksheetPart.TableDefinitionParts
                .Single(candidate => candidate.Table?.Name?.Value == month.TableName);
            var table = RequiredTable(tablePart);
            var range = ParseRange(table.Reference?.Value, month.TableName);
            var sheetData = RequiredWorksheet(worksheetPart).GetFirstChild<SheetData>()
                ?? throw InvalidWorkbook($"Sheet '{month.SheetName}' does not contain sheet data.");
            var firstDataRow = range.StartRow + 1;
            _ = GetOrCreateRow(sheetData, firstDataRow);

            foreach (var row in sheetData.Elements<Row>().Where(row =>
                         row.RowIndex?.Value >= firstDataRow && row.RowIndex?.Value <= range.EndRow))
            {
                for (var column = 1; column <= 6; column++)
                {
                    ClearCellValue(FindCell(row, column));
                }
            }

            SetTableEndRow(table, range, firstDataRow);
            RequiredWorksheet(worksheetPart).Save();
            table.Save();
        }
    }

    private IEnumerable<MigrationCandidate> ReadMigrationCandidates(
        WorkbookPart workbookPart,
        WorkbookKey key)
    {
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
                var draft = ReadDraft(workbookPart, row);
                ValidateDraft(key, draft);
                if (draft.Date.Month != monthNumber)
                {
                    throw InvalidWorkbook(
                        $"Transaction at '{month.SheetName}!A{row.RowIndex?.Value}' does not belong to month {monthNumber}.");
                }

                var snapshot = SerializeSnapshot(draft);
                yield return new MigrationCandidate(
                    monthNumber,
                    row.RowIndex!.Value,
                    snapshot,
                    HashSnapshot(snapshot));
            }
        }
    }

    private static IReadOnlyList<IndexEntry> ReadAllIndexEntries(WorksheetPart indexPart)
    {
        var entries = new List<IndexEntry>();
        var rows = RequiredWorksheet(indexPart).GetFirstChild<SheetData>()?.Elements<Row>().Skip(1) ?? [];
        foreach (var row in rows)
        {
            var id = ReadInlineCell(FindCell(row, 1));
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!Guid.TryParse(id, out var transactionId))
            {
                throw InvalidWorkbook($"System index row {row.RowIndex?.Value} has an invalid transaction ID.");
            }

            entries.Add(ReadIndexEntry(indexPart, transactionId));
        }

        if (entries.Select(entry => entry.TransactionId).Distinct().Count() != entries.Count)
        {
            throw InvalidWorkbook("System index contains duplicate transaction IDs.");
        }

        return entries;
    }

    private static Guid CreateMigrationId(
        WorkbookKey key,
        MigrationCandidate candidate,
        IReadOnlySet<Guid> knownIds)
    {
        for (var salt = 0; ; salt++)
        {
            var source = $"{key.UserId:D}|{key.Year}|{candidate.Month}|{candidate.ExcelRow}|{candidate.ContentHash}|{salt}";
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            var id = new Guid(bytes.AsSpan(0, 16));
            if (!knownIds.Contains(id))
            {
                return id;
            }
        }
    }

    private static WorksheetPart ResetSystemSheet(
        WorkbookPart workbookPart,
        string sheetName,
        IReadOnlyList<string> headers)
    {
        var part = EnsureSystemSheet(workbookPart, sheetName, headers);
        var sheetData = RequiredWorksheet(part).GetFirstChild<SheetData>()
            ?? throw InvalidWorkbook($"System sheet '{sheetName}' does not contain sheet data.");
        sheetData.RemoveAllChildren();
        AppendRow(part, headers.Cast<object?>().ToArray());
        return part;
    }

    private static void AppendMigrationAudit(
        WorksheetPart auditPart,
        WorkbookKey key,
        WorkbookAuditContext audit,
        DateTimeOffset occurredAtUtc,
        string mode,
        int indexed,
        int updated,
        int tombstoned)
    {
        var summary = JsonSerializer.Serialize(new
        {
            mode,
            indexedTransactionCount = indexed,
            updatedTransactionCount = updated,
            tombstonedTransactionCount = tombstoned
        });
        AppendRow(
            auditPart,
            Guid.NewGuid().ToString("D"),
            null,
            "SYSTEM_MIGRATION",
            FormatSource(audit.Source),
            FormatTimestamp(occurredAtUtc),
            null,
            null,
            null,
            summary,
            key.UserId.ToString("D"),
            audit.DeviceId,
            audit.CorrelationId);
    }

    private static void RequestFullCalculation(WorkbookPart workbookPart)
    {
        var workbook = RequiredWorkbook(workbookPart);
        var properties = workbook.CalculationProperties ?? workbook.AppendChild(new CalculationProperties());
        properties.CalculationMode = CalculateModeValues.Auto;
        // Request one full calculation when Excel opens the changed workbook,
        // not persistent forced mode (which keeps Excel reporting xlPending).
        properties.ForceFullCalculation = false;
        properties.FullCalculationOnLoad = true;
    }

    private static void SetInlineText(Cell cell, string value)
    {
        cell.CellFormula = null;
        cell.CellValue = null;
        cell.DataType = CellValues.InlineString;
        cell.InlineString = new InlineString(new Text(value));
    }

    private void ValidateDraft(WorkbookKey key, WorkbookTransactionDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (draft.Date.Year != key.Year)
        {
            throw new ArgumentException("Transaction date must belong to the target workbook year.", nameof(draft));
        }

        if (draft.AmountIdr <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(draft), "Transaction amount must be positive whole IDR.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Type);
        ArgumentNullException.ThrowIfNull(draft.PaymentMethod);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Category);

        if (!masterData.TransactionTypes.Contains(draft.Type, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Unknown transaction type '{draft.Type}'.", nameof(draft));
        }

        if (draft.PaymentMethod.Length > 0
            && !masterData.PaymentMethods.Contains(draft.PaymentMethod, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Unknown payment method '{draft.PaymentMethod}'.", nameof(draft));
        }

        var category = masterData.Categories.SingleOrDefault(candidate => candidate.Name == draft.Category)
            ?? throw new ArgumentException($"Unknown category '{draft.Category}'.", nameof(draft));

        if (!string.Equals(category.Kind, draft.Type, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Category '{draft.Category}' belongs to transaction type '{category.Kind}', not '{draft.Type}'.",
                nameof(draft));
        }
    }

    private static uint AppendTransactionRow(WorkbookPart workbookPart, WorkbookTransactionDraft draft)
    {
        var month = Months[draft.Date.Month];
        var worksheetPart = FindWorksheetPart(workbookPart, month.SheetName);
        var tablePart = worksheetPart.TableDefinitionParts
            .Single(candidate => candidate.Table?.Name?.Value == month.TableName);
        var table = RequiredTable(tablePart);
        var worksheet = RequiredWorksheet(worksheetPart);
        var range = ParseRange(table.Reference?.Value, month.TableName);
        var sheetData = worksheet.GetFirstChild<SheetData>()
            ?? throw InvalidWorkbook($"Sheet '{month.SheetName}' does not contain sheet data.");

        var lastRowIsBlank = IsTransactionRowBlank(sheetData, range.EndRow);
        var targetRow = lastRowIsBlank ? range.EndRow : range.EndRow + 1;
        WriteTransactionRow(worksheetPart, targetRow, draft);

        if (targetRow > range.EndRow)
        {
            SetTableEndRow(table, range, targetRow);
        }

        worksheet.Save();
        table.Save();
        return targetRow;
    }

    private static void WriteTransactionRow(
        WorksheetPart worksheetPart,
        uint rowNumber,
        WorkbookTransactionDraft draft)
    {
        var worksheet = RequiredWorksheet(worksheetPart);
        var sheetData = worksheet.GetFirstChild<SheetData>()
            ?? throw InvalidWorkbook("Worksheet does not contain sheet data.");
        var row = GetOrCreateRow(sheetData, rowNumber);
        var styleSourceRow = rowNumber > 6 ? rowNumber - 1 : 6;

        WriteNumberCell(row, 1, rowNumber, draft.Date.ToDateTime(TimeOnly.MinValue).ToOADate(), sheetData, styleSourceRow);
        WriteTextCell(row, 2, rowNumber, draft.Type, sheetData, styleSourceRow);
        WriteTextCell(row, 3, rowNumber, draft.PaymentMethod, sheetData, styleSourceRow);
        WriteTextCell(row, 4, rowNumber, draft.Category, sheetData, styleSourceRow);
        WriteNumberCell(row, 5, rowNumber, draft.AmountIdr, sheetData, styleSourceRow);
        WriteTextCell(row, 6, rowNumber, draft.Description, sheetData, styleSourceRow);
        worksheet.Save();
    }

    private static void RemoveTransactionRow(
        WorksheetPart worksheetPart,
        TableDefinitionPart tablePart,
        TableRange range,
        uint rowNumber)
    {
        var worksheet = RequiredWorksheet(worksheetPart);
        var table = RequiredTable(tablePart);
        var sheetData = worksheet.GetFirstChild<SheetData>()
            ?? throw InvalidWorkbook("Worksheet does not contain sheet data.");

        for (var current = rowNumber; current < range.EndRow; current++)
        {
            var targetRow = GetOrCreateRow(sheetData, current);
            var sourceRow = sheetData.Elements<Row>()
                .SingleOrDefault(candidate => candidate.RowIndex?.Value == current + 1);

            for (var column = 1; column <= 6; column++)
            {
                CopyCell(sourceRow is null ? null : FindCell(sourceRow, column), targetRow, column, current);
            }
        }

        var lastRow = GetOrCreateRow(sheetData, range.EndRow);

        for (var column = 1; column <= 6; column++)
        {
            ClearCellValue(FindCell(lastRow, column));
        }

        if (range.EndRow > range.StartRow + 1)
        {
            SetTableEndRow(table, range, range.EndRow - 1);
        }

        worksheet.Save();
        table.Save();
    }

    private static void CopyCell(Cell? source, Row targetRow, int column, uint targetRowNumber)
    {
        var existing = FindCell(targetRow, column);

        if (source is null)
        {
            ClearCellValue(existing);
            return;
        }

        var clone = (Cell)source.CloneNode(true);
        clone.CellReference = $"{ColumnName(column)}{targetRowNumber}";

        if (existing is not null)
        {
            targetRow.ReplaceChild(clone, existing);
            return;
        }

        var nextCell = targetRow.Elements<Cell>()
            .FirstOrDefault(candidate => ColumnNumber(candidate.CellReference?.Value) > column);
        targetRow.InsertBefore(clone, nextCell);
    }

    private static void ClearCellValue(Cell? cell)
    {
        if (cell is null)
        {
            return;
        }

        cell.CellFormula = null;
        cell.CellValue = null;
        cell.InlineString = null;
        cell.DataType = null;
    }

    private static LocatedTransaction LocateTransactionRow(WorkbookPart workbookPart, IndexEntry entry)
    {
        var month = Months[entry.Month];
        var worksheetPart = FindWorksheetPart(workbookPart, month.SheetName);
        var tablePart = worksheetPart.TableDefinitionParts
            .Single(candidate => candidate.Table?.Name?.Value == month.TableName);
        var table = RequiredTable(tablePart);
        var worksheet = RequiredWorksheet(worksheetPart);
        var range = ParseRange(table.Reference?.Value, month.TableName);
        var sheetData = worksheet.GetFirstChild<SheetData>()
            ?? throw InvalidWorkbook($"Sheet '{month.SheetName}' does not contain sheet data.");

        if (entry.LastKnownExcelRow > range.StartRow && entry.LastKnownExcelRow <= range.EndRow)
        {
            var hintedRow = sheetData.Elements<Row>()
                .SingleOrDefault(candidate => candidate.RowIndex?.Value == entry.LastKnownExcelRow);

            if (hintedRow is not null && HashSnapshot(SerializeSnapshot(ReadDraft(workbookPart, hintedRow))) == entry.ContentHash)
            {
                return new LocatedTransaction(worksheetPart, tablePart, range, hintedRow);
            }
        }

        var matches = sheetData.Elements<Row>()
            .Where(candidate => candidate.RowIndex?.Value > range.StartRow && candidate.RowIndex?.Value <= range.EndRow)
            .Where(candidate => !IsTransactionRowBlank(sheetData, candidate.RowIndex!.Value))
            .Where(candidate => HashSnapshot(SerializeSnapshot(ReadDraft(workbookPart, candidate))) == entry.ContentHash)
            .ToArray();

        return matches.Length switch
        {
            1 => new LocatedTransaction(worksheetPart, tablePart, range, matches[0]),
            0 => throw InvalidWorkbook($"Transaction '{entry.TransactionId:D}' no longer matches its workbook row."),
            _ => throw InvalidWorkbook($"Transaction '{entry.TransactionId:D}' matches multiple workbook rows.")
        };
    }

    private static WorkbookTransactionDraft ReadDraft(WorkbookPart workbookPart, Row row)
    {
        var dateText = ReadCellText(workbookPart, FindCell(row, 1));
        var type = ReadCellText(workbookPart, FindCell(row, 2));
        var payment = ReadCellText(workbookPart, FindCell(row, 3));
        var category = ReadCellText(workbookPart, FindCell(row, 4));
        var amountText = ReadCellText(workbookPart, FindCell(row, 5));
        var description = ReadCellText(workbookPart, FindCell(row, 6));

        if (!double.TryParse(dateText, NumberStyles.Float, CultureInfo.InvariantCulture, out var dateSerial)
            || !long.TryParse(amountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount)
            || string.IsNullOrWhiteSpace(type)
            || string.IsNullOrWhiteSpace(category))
        {
            throw InvalidWorkbook($"Transaction row {row.RowIndex?.Value} is invalid.");
        }

        return new WorkbookTransactionDraft(
            DateOnly.FromDateTime(DateTime.FromOADate(dateSerial)),
            type,
            payment?.Trim() ?? string.Empty,
            category,
            amount,
            string.IsNullOrWhiteSpace(description) ? null : description);
    }

    private static string? ReadCellText(WorkbookPart workbookPart, Cell? cell)
    {
        if (cell is null)
        {
            return null;
        }

        var raw = cell.CellValue?.InnerText ?? cell.InlineString?.InnerText ?? cell.InnerText;

        if (cell.DataType?.Value != CellValues.SharedString)
        {
            return raw;
        }

        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            throw InvalidWorkbook($"Cell '{cell.CellReference}' has invalid shared-string index.");
        }

        var sharedStringTable = workbookPart.SharedStringTablePart?.SharedStringTable
            ?? throw InvalidWorkbook("A cell references shared strings, but the package has no shared-string table.");

        return sharedStringTable.Elements<SharedStringItem>()
            .ElementAtOrDefault(index)?.InnerText
            ?? throw InvalidWorkbook($"Cell '{cell.CellReference}' references missing shared string {index}.");
    }

    private static IndexEntry ReadIndexEntry(WorksheetPart indexPart, Guid transactionId)
    {
        var row = FindRowByFirstColumn(indexPart, transactionId.ToString("D"))
            ?? throw new WorkbookTransactionNotFoundException(transactionId);

        if (!int.TryParse(ReadInlineCell(FindCell(row, 2)), CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(ReadInlineCell(FindCell(row, 3)), CultureInfo.InvariantCulture, out var month)
            || !uint.TryParse(ReadInlineCell(FindCell(row, 4)), CultureInfo.InvariantCulture, out var excelRow)
            || !int.TryParse(ReadInlineCell(FindCell(row, 5)), CultureInfo.InvariantCulture, out var revision))
        {
            throw InvalidWorkbook($"Index row for transaction '{transactionId:D}' is invalid.");
        }

        var deletedText = ReadInlineCell(FindCell(row, 8));
        var isDeleted = deletedText is "1" or "true" or "TRUE";

        return new IndexEntry(
            row,
            transactionId,
            year,
            month,
            excelRow,
            revision,
            ReadInlineCell(FindCell(row, 6)),
            ReadInlineCell(FindCell(row, 7)),
            isDeleted,
            ReadInlineCell(FindCell(row, 9)));
    }

    private static void UpdateIndexRow(
        IndexEntry entry,
        int year,
        int month,
        uint excelRow,
        int revision,
        string contentHash,
        string snapshot,
        bool isDeleted,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset? deletedAtUtc)
    {
        SetSystemCell(entry.Row, 2, year);
        SetSystemCell(entry.Row, 3, month);
        SetSystemCell(entry.Row, 4, excelRow);
        SetSystemCell(entry.Row, 5, revision);
        SetSystemCell(entry.Row, 6, contentHash);
        SetSystemCell(entry.Row, 7, snapshot);
        SetSystemCell(entry.Row, 8, isDeleted);
        SetSystemCell(entry.Row, 9, entry.CreatedAtUtc);
        SetSystemCell(entry.Row, 10, FormatTimestamp(updatedAtUtc));
        SetSystemCell(entry.Row, 11, deletedAtUtc is null ? null : FormatTimestamp(deletedAtUtc.Value));
    }

    private static void ShiftIndexRowsAfterRemoval(
        WorksheetPart indexPart,
        int month,
        uint removedExcelRow)
    {
        var rows = RequiredWorksheet(indexPart).GetFirstChild<SheetData>()?.Elements<Row>().Skip(1)
            ?? [];

        foreach (var row in rows)
        {
            var isDeleted = ReadInlineCell(FindCell(row, 8)) is "1" or "true" or "TRUE";

            if (isDeleted
                || !int.TryParse(ReadInlineCell(FindCell(row, 3)), CultureInfo.InvariantCulture, out var indexedMonth)
                || indexedMonth != month
                || !uint.TryParse(ReadInlineCell(FindCell(row, 4)), CultureInfo.InvariantCulture, out var indexedRow)
                || indexedRow <= removedExcelRow)
            {
                continue;
            }

            SetSystemCell(row, 4, indexedRow - 1);
        }
    }

    private static void SetSystemCell(Row row, int column, object? value)
    {
        var rowNumber = row.RowIndex?.Value
            ?? throw InvalidWorkbook("System row has no row index.");
        var cell = FindCell(row, column) ?? new Cell { CellReference = $"{ColumnName(column)}{rowNumber}" };

        if (cell.Parent is null)
        {
            var nextCell = row.Elements<Cell>()
                .FirstOrDefault(candidate => ColumnNumber(candidate.CellReference?.Value) > column);
            row.InsertBefore(cell, nextCell);
        }

        cell.CellValue = null;
        cell.InlineString = null;
        WriteSystemValue(cell, value);
    }

    private static WorksheetPart EnsureSystemSheet(
        WorkbookPart workbookPart,
        string sheetName,
        IReadOnlyList<string> headers)
    {
        var workbook = RequiredWorkbook(workbookPart);
        var existingSheet = workbook.Sheets?.Elements<Sheet>()
            .SingleOrDefault(candidate => candidate.Name?.Value == sheetName);

        if (existingSheet is not null)
        {
            existingSheet.State = SheetStateValues.VeryHidden;
            return (WorksheetPart)workbookPart.GetPartById(existingSheet.Id!.Value!);
        }

        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        worksheetPart.Worksheet = new Worksheet(new SheetData());
        var sheets = workbook.Sheets ?? workbook.AppendChild(new Sheets());
        var nextSheetId = sheets.Elements<Sheet>().Select(sheet => sheet.SheetId?.Value ?? 0).DefaultIfEmpty().Max() + 1;
        var sheet = new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = nextSheetId,
            Name = sheetName,
            State = SheetStateValues.VeryHidden
        };
        sheets.Append(sheet);
        AppendRow(worksheetPart, headers.Cast<object?>().ToArray());
        RequiredWorksheet(worksheetPart).Save();
        workbook.Save();
        return worksheetPart;
    }

    private static void ValidateSystemSheet(
        WorkbookPart workbookPart,
        string sheetName,
        IReadOnlyList<string> expectedHeaders)
    {
        var workbook = RequiredWorkbook(workbookPart);
        var sheet = workbook.Sheets?.Elements<Sheet>()
            .SingleOrDefault(candidate => candidate.Name?.Value == sheetName)
            ?? throw InvalidWorkbook($"Required system sheet '{sheetName}' was not found.");

        if (sheet.State?.Value != SheetStateValues.VeryHidden)
        {
            throw InvalidWorkbook($"System sheet '{sheetName}' must be very hidden.");
        }

        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
        var headerRow = RequiredWorksheet(worksheetPart).GetFirstChild<SheetData>()?.Elements<Row>().FirstOrDefault()
            ?? throw InvalidWorkbook($"System sheet '{sheetName}' has no header row.");
        var actualHeaders = headerRow.Elements<Cell>().Select(ReadInlineCell).ToArray();

        if (!actualHeaders.SequenceEqual(expectedHeaders, StringComparer.Ordinal))
        {
            throw InvalidWorkbook($"System sheet '{sheetName}' has invalid headers.");
        }
    }

    private static Row? FindRowByFirstColumn(WorksheetPart worksheetPart, string value) =>
        RequiredWorksheet(worksheetPart).GetFirstChild<SheetData>()?
            .Elements<Row>()
            .Skip(1)
            .SingleOrDefault(row => string.Equals(ReadInlineCell(FindCell(row, 1)), value, StringComparison.OrdinalIgnoreCase));

    private static void AppendRow(WorksheetPart worksheetPart, params object?[] values)
    {
        var worksheet = RequiredWorksheet(worksheetPart);
        var sheetData = worksheet.GetFirstChild<SheetData>()
            ?? worksheet.AppendChild(new SheetData());
        var nextRow = sheetData.Elements<Row>().Select(row => row.RowIndex?.Value ?? 0).DefaultIfEmpty().Max() + 1;
        var row = new Row { RowIndex = nextRow };

        for (var index = 0; index < values.Length; index++)
        {
            var cell = new Cell { CellReference = $"{ColumnName(index + 1)}{nextRow}" };
            WriteSystemValue(cell, values[index]);
            row.Append(cell);
        }

        sheetData.Append(row);
        worksheet.Save();
    }

    private static void WriteSystemValue(Cell cell, object? value)
    {
        switch (value)
        {
            case null:
                cell.DataType = CellValues.InlineString;
                cell.InlineString = new InlineString(new Text(string.Empty));
                break;
            case bool boolean:
                cell.DataType = CellValues.Boolean;
                cell.CellValue = new CellValue(boolean ? "1" : "0");
                break;
            case byte or short or int or long or uint:
                cell.DataType = CellValues.Number;
                cell.CellValue = new CellValue(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
                break;
            default:
                cell.DataType = CellValues.InlineString;
                cell.InlineString = new InlineString(new Text(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty));
                break;
        }
    }

    private static void WriteTextCell(
        Row row,
        int column,
        uint rowNumber,
        string? value,
        SheetData sheetData,
        uint styleSourceRow)
    {
        var cell = GetOrCreateCell(row, column, rowNumber, sheetData, styleSourceRow);
        cell.CellValue = null;

        if (string.IsNullOrEmpty(value))
        {
            cell.DataType = null;
            cell.InlineString = null;
            return;
        }

        cell.DataType = CellValues.InlineString;
        cell.InlineString = new InlineString(new Text(value));
    }

    private static void WriteNumberCell(
        Row row,
        int column,
        uint rowNumber,
        double value,
        SheetData sheetData,
        uint styleSourceRow)
    {
        var cell = GetOrCreateCell(row, column, rowNumber, sheetData, styleSourceRow);
        cell.DataType = CellValues.Number;
        cell.InlineString = null;
        cell.CellValue = new CellValue(value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void WriteNumberCell(
        Row row,
        int column,
        uint rowNumber,
        long value,
        SheetData sheetData,
        uint styleSourceRow)
    {
        var cell = GetOrCreateCell(row, column, rowNumber, sheetData, styleSourceRow);
        cell.DataType = CellValues.Number;
        cell.InlineString = null;
        cell.CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture));
    }

    private static Cell GetOrCreateCell(
        Row row,
        int column,
        uint rowNumber,
        SheetData sheetData,
        uint styleSourceRow)
    {
        var existing = FindCell(row, column);

        if (existing is not null)
        {
            return existing;
        }

        var cell = new Cell { CellReference = $"{ColumnName(column)}{rowNumber}" };
        var sourceRow = sheetData.Elements<Row>().SingleOrDefault(candidate => candidate.RowIndex?.Value == styleSourceRow);
        var sourceCell = sourceRow is null ? null : FindCell(sourceRow, column);

        if (sourceCell?.StyleIndex is not null)
        {
            cell.StyleIndex = sourceCell.StyleIndex.Value;
        }

        var nextCell = row.Elements<Cell>().FirstOrDefault(candidate => ColumnNumber(candidate.CellReference?.Value) > column);
        row.InsertBefore(cell, nextCell);
        return cell;
    }

    private static Row GetOrCreateRow(SheetData sheetData, uint rowNumber)
    {
        var row = sheetData.Elements<Row>().SingleOrDefault(candidate => candidate.RowIndex?.Value == rowNumber);

        if (row is not null)
        {
            return row;
        }

        row = new Row { RowIndex = rowNumber };
        var nextRow = sheetData.Elements<Row>().FirstOrDefault(candidate => candidate.RowIndex?.Value > rowNumber);
        sheetData.InsertBefore(row, nextRow);
        return row;
    }

    private static bool IsTransactionRowBlank(SheetData sheetData, uint rowNumber)
    {
        var row = sheetData.Elements<Row>().SingleOrDefault(candidate => candidate.RowIndex?.Value == rowNumber);
        return row is null || Enumerable.Range(1, 6).All(column => IsCellBlank(FindCell(row, column)));
    }

    private static bool IsCellBlank(Cell? cell) =>
        cell is null || string.IsNullOrWhiteSpace(cell.CellValue?.InnerText ?? cell.InlineString?.InnerText ?? cell.InnerText);

    private static Cell? FindCell(Row row, int column) => row.Elements<Cell>()
        .SingleOrDefault(cell => ColumnNumber(cell.CellReference?.Value) == column);

    private static string ReadInlineCell(Cell? cell) =>
        cell?.InlineString?.InnerText ?? cell?.CellValue?.InnerText ?? cell?.InnerText ?? string.Empty;

    private static int ColumnNumber(string? reference)
    {
        var letters = CellReferenceRegex().Match(reference ?? string.Empty).Groups[1].Value;
        var result = 0;

        foreach (var letter in letters)
        {
            result = (result * 26) + (letter - 'A' + 1);
        }

        return result;
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

    private static WorksheetPart FindWorksheetPart(WorkbookPart workbookPart, string sheetName)
    {
        var sheet = RequiredWorkbook(workbookPart).Sheets?.Elements<Sheet>()
            .SingleOrDefault(candidate => candidate.Name?.Value == sheetName)
            ?? throw InvalidWorkbook($"Required sheet '{sheetName}' was not found.");
        return (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
    }

    private static TableRange ParseRange(string? reference, string tableName)
    {
        var match = TableRangeRegex().Match(reference ?? string.Empty);

        if (!match.Success
            || !uint.TryParse(match.Groups[2].Value, CultureInfo.InvariantCulture, out var startRow)
            || !uint.TryParse(match.Groups[4].Value, CultureInfo.InvariantCulture, out var endRow))
        {
            throw InvalidWorkbook($"Table '{tableName}' has invalid reference '{reference}'.");
        }

        return new TableRange(match.Groups[1].Value, startRow, match.Groups[3].Value, endRow);
    }

    private static void SetTableEndRow(Table table, TableRange range, uint endRow)
    {
        var reference = $"{range.StartColumn}{range.StartRow}:{range.EndColumn}{endRow}";
        table.Reference = reference;

        if (table.AutoFilter is not null)
        {
            table.AutoFilter.Reference = reference;
        }
    }

    private static WorkbookPart RequiredWorkbookPart(SpreadsheetDocument document) =>
        document.WorkbookPart ?? throw InvalidWorkbook("The package does not contain a workbook part.");

    private static Workbook RequiredWorkbook(WorkbookPart workbookPart) =>
        workbookPart.Workbook ?? throw InvalidWorkbook("The workbook part does not contain a workbook definition.");

    private static Worksheet RequiredWorksheet(WorksheetPart worksheetPart) =>
        worksheetPart.Worksheet ?? throw InvalidWorkbook("A worksheet part does not contain a worksheet definition.");

    private static Table RequiredTable(TableDefinitionPart tablePart) =>
        tablePart.Table ?? throw InvalidWorkbook("A table definition part does not contain a table definition.");

    private static string SerializeSnapshot(WorkbookTransactionDraft draft) => JsonSerializer.Serialize(
        new TransactionSnapshot(
            draft.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            draft.Type,
            draft.PaymentMethod,
            draft.Category,
            draft.AmountIdr,
            draft.Description));

    private static string HashSnapshot(string snapshot) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot))).ToLowerInvariant();

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string FormatSource(WorkbookChangeSource source) => source switch
    {
        WorkbookChangeSource.Android => "ANDROID",
        WorkbookChangeSource.Web => "WEB",
        WorkbookChangeSource.ExcelImport => "EXCEL_IMPORT",
        WorkbookChangeSource.System => "SYSTEM",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
    };

    private static WorkbookTransaction ToTransaction(Guid id, int revision, WorkbookTransactionDraft draft) => new(
        id,
        revision,
        draft.Date,
        draft.Type,
        draft.PaymentMethod,
        draft.Category,
        draft.AmountIdr,
        draft.Description);

    private static void Save(
        SpreadsheetDocument document,
        WorksheetPart indexPart,
        WorksheetPart auditPart)
    {
        RequiredWorksheet(indexPart).Save();
        RequiredWorksheet(auditPart).Save();
        var workbookPart = RequiredWorkbookPart(document);
        RequestFullCalculation(workbookPart);
        RequiredWorkbook(workbookPart).Save();
    }

    private static InvalidDataException InvalidWorkbook(string message) => new(message);

    [GeneratedRegex("^([A-Z]+)", RegexOptions.CultureInvariant)]
    private static partial Regex CellReferenceRegex();

    [GeneratedRegex("^\\$?([A-Z]+)\\$?([0-9]+):\\$?([A-Z]+)\\$?([0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex TableRangeRegex();

    [GeneratedRegex("\\b(?:19|20)[0-9]{2}\\b", RegexOptions.CultureInvariant)]
    private static partial Regex YearTextRegex();

    private sealed record MonthDefinition(string SheetName, string TableName);

    private sealed record TableRange(string StartColumn, uint StartRow, string EndColumn, uint EndRow);

    private sealed record LocatedTransaction(
        WorksheetPart WorksheetPart,
        TableDefinitionPart TablePart,
        TableRange Range,
        Row Row);

    private sealed record IndexEntry(
        Row Row,
        Guid TransactionId,
        int Year,
        int Month,
        uint LastKnownExcelRow,
        int Revision,
        string ContentHash,
        string SnapshotJson,
        bool IsDeleted,
        string CreatedAtUtc);

    private sealed record TransactionSnapshot(
        string Date,
        string Type,
        string PaymentMethod,
        string Category,
        long AmountIdr,
        string? Description);

    private sealed record MigrationCandidate(
        int Month,
        uint ExcelRow,
        string SnapshotJson,
        string ContentHash);
}
