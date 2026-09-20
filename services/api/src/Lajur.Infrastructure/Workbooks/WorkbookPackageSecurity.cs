using System.IO.Compression;

namespace Lajur.Infrastructure.Workbooks;

internal static class WorkbookPackageSecurity
{
    public const long MaximumUploadBytes = 25L * 1024 * 1024;
    private const long MaximumExpandedBytes = 256L * 1024 * 1024;
    private const long MaximumEntryBytes = 64L * 1024 * 1024;
    private const int MaximumEntryCount = 4_000;

    public static void Validate(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The staged workbook was not found.", path);
        }

        if (file.Length > MaximumUploadBytes)
        {
            throw new WorkbookUploadTooLargeException(MaximumUploadBytes);
        }

        try
        {
            using var stream = file.OpenRead();
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count is 0 or > MaximumEntryCount)
            {
                throw new WorkbookImportValidationException("The workbook package has an unsafe entry count.");
            }

            long expandedBytes = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                if (!names.Add(entry.FullName)
                    || Path.IsPathRooted(entry.FullName)
                    || entry.FullName.Contains('\\', StringComparison.Ordinal)
                    || entry.FullName.Split('/').Any(segment => segment == ".."))
                {
                    throw new WorkbookImportValidationException("The workbook package contains an unsafe entry name.");
                }

                if (entry.Length > MaximumEntryBytes)
                {
                    throw new WorkbookImportValidationException("The workbook package contains an oversized entry.");
                }

                expandedBytes = checked(expandedBytes + entry.Length);
                if (expandedBytes > MaximumExpandedBytes)
                {
                    throw new WorkbookImportValidationException("The expanded workbook package is too large.");
                }
            }

            if (archive.GetEntry("[Content_Types].xml") is null
                || archive.GetEntry("xl/workbook.xml") is null
                || archive.GetEntry("xl/vbaProject.bin") is null)
            {
                throw new WorkbookImportValidationException("The upload is not a macro-enabled Excel workbook package.");
            }
        }
        catch (WorkbookImportValidationException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            throw new WorkbookImportValidationException("The upload is not a readable ZIP/OPC package.", exception);
        }
        catch (OverflowException exception)
        {
            throw new WorkbookImportValidationException("The expanded workbook package is too large.", exception);
        }
    }
}
