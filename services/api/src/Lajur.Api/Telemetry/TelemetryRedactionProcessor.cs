using System.Diagnostics;
using OpenTelemetry;

namespace Lajur.Api.Telemetry;

/// <summary>
/// Removes span attributes that can identify a person or carry request data before
/// export (ADR 0013). The collector repeats this as defense in depth.
/// </summary>
public sealed class TelemetryRedactionProcessor : BaseProcessor<Activity>
{
    private static readonly HashSet<string> RedactedKeys = new(StringComparer.Ordinal)
    {
        "client.address",
        "client.port",
        "network.peer.address",
        "network.peer.port",
        "url.query",
        "url.full",
        "user_agent.original"
    };

    private static readonly string[] RedactedPrefixes =
    [
        "enduser.",
        "user.",
        "http.request.header.",
        "http.response.header."
    ];

    public override void OnEnd(Activity activity)
    {
        List<string>? redacted = null;
        foreach (var tag in activity.TagObjects)
        {
            if (IsRedacted(tag.Key))
            {
                (redacted ??= []).Add(tag.Key);
            }
        }

        if (redacted is null)
        {
            return;
        }

        foreach (var key in redacted)
        {
            activity.SetTag(key, null);
        }
    }

    public static bool IsRedacted(string key) =>
        RedactedKeys.Contains(key)
        || RedactedPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal));
}
