using Lajur.Application.Auth;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Lajur.Api.Health;

/// <summary>
/// Readiness fails when any directory the API writes to is not writable (for example a missing or
/// read-only Docker mount), so misconfiguration shows up before a user's import or save fails.
/// </summary>
public sealed class StorageHealthCheck(IOptions<StorageOptions> storage) : IHealthCheck
{
    public static readonly string[] WritableDirectories = ["data", "backups", "staging", "keys", "auth"];

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(storage.Value.RootPath);
        var failed = new List<string>();
        foreach (var name in WritableDirectories)
        {
            if (!CanWrite(Path.Combine(root, name)))
            {
                failed.Add(name);
            }
        }

        return Task.FromResult(failed.Count == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"Storage directories are not writable: {string.Join(", ", failed)}."));
    }

    private static bool CanWrite(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".lajur-health-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
