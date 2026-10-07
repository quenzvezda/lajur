namespace Lajur.Infrastructure.Workbooks;

/// <summary>
/// Separates server storage/configuration faults (read-only mount, permission denied, disk full)
/// from transient workbook contention. Retrying never fixes the former, so clients must be told it is
/// a server problem rather than "retry later".
/// </summary>
public static class StorageFault
{
    // Unix errno values surface as IOException.HResult on Linux.
    private const int UnixReadOnlyFileSystem = 30; // EROFS
    private const int UnixNoSpace = 28;            // ENOSPC
    private const int UnixQuotaExceeded = 122;     // EDQUOT

    // Windows HRESULTs.
    private const int WindowsDiskFull = unchecked((int)0x80070070);
    private const int WindowsHandleDiskFull = unchecked((int)0x80070027);
    private const int WindowsWriteProtect = unchecked((int)0x80070013);

    public static bool IsServerStorageFault(Exception exception) => exception switch
    {
        ServerStorageUnavailableException => true,
        UnauthorizedAccessException => true,
        IOException io => io.HResult is UnixReadOnlyFileSystem or UnixNoSpace or UnixQuotaExceeded
            or WindowsDiskFull or WindowsHandleDiskFull or WindowsWriteProtect,
        _ => false
    };
}

/// <summary>A server-owned storage area (for example import staging) cannot be created or written.</summary>
public sealed class ServerStorageUnavailableException(string area, Exception innerException)
    : IOException($"Server storage area '{area}' is not writable.", innerException)
{
    public string Area { get; } = area;
}
