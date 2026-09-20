using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Lajur.IntegrationTests;

internal sealed class LajurApiFactory : WebApplicationFactory<Program>
{
    public string StorageRoot { get; } = Path.Combine(
        Path.GetTempPath(),
        "lajur-integration-tests",
        Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lajur:Storage:RootPath"] = StorageRoot
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(StorageRoot))
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(StorageRoot, recursive: true);
        }
    }
}
