using Lajur.Infrastructure.Workbooks;

namespace Lajur.WorkbookTests;

public sealed class StorageFaultTests
{
    [Fact]
    public void Server_storage_faults_are_separated_from_transient_contention()
    {
        Assert.True(StorageFault.IsServerStorageFault(new UnauthorizedAccessException()));
        Assert.True(StorageFault.IsServerStorageFault(new IOException("Read-only file system", 30)));
        Assert.True(StorageFault.IsServerStorageFault(new IOException("No space left on device", 28)));
        Assert.True(StorageFault.IsServerStorageFault(
            new ServerStorageUnavailableException("staging", new IOException("exists"))));

        Assert.False(StorageFault.IsServerStorageFault(new IOException("The process cannot access the file.")));
        Assert.False(StorageFault.IsServerStorageFault(new FileNotFoundException()));
        Assert.False(StorageFault.IsServerStorageFault(new InvalidDataException()));
    }
}
