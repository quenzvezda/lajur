using System.Reflection;
using Lajur.Infrastructure.Workbooks;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Lajur.Api.Telemetry;

/// <summary>
/// Opt-in OTLP export of traces, metrics and logs (ADR 0013). Nothing is registered
/// unless <c>Lajur:Telemetry:OtlpEndpoint</c> is set.
/// </summary>
public static class LajurTelemetry
{
    public const string SectionName = "Lajur:Telemetry";
    public const string DefaultServiceName = "lajur-api";

    public static WebApplicationBuilder AddLajurTelemetry(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(SectionName);
        var endpoint = ParseEndpoint(section["OtlpEndpoint"]);
        if (endpoint is null)
        {
            return builder;
        }

        var serviceName = section["ServiceName"] is { Length: > 0 } name ? name : DefaultServiceName;
        var version = typeof(LajurTelemetry).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: version)
                .AddAttributes(
                [
                    new("deployment.environment.name", builder.Environment.EnvironmentName.ToLowerInvariant())
                ]))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.Filter = context => !IsExcludedPath(context.Request.Path);
                    options.RecordException = false;
                })
                .AddSource(WorkbookTelemetry.SourceName)
                // Registered before the exporter below, so it runs first on span end.
                .AddProcessor(new TelemetryRedactionProcessor()))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddMeter("System.Runtime")
                .AddMeter(WorkbookTelemetry.SourceName))
            .WithLogging(configureBuilder: null, configureOptions: options =>
            {
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = false;
            })
            .UseOtlpExporter(OtlpExportProtocol.HttpProtobuf, endpoint);

        return builder;
    }

    /// <summary>
    /// Returns null when telemetry is disabled; throws for a malformed endpoint so a bad
    /// deployment fails its health gate instead of silently exporting nowhere.
    /// </summary>
    public static Uri? ParseEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query))
        {
            return uri;
        }

        // The value is deliberately not echoed: it could contain credentials.
        throw new InvalidOperationException(
            $"{SectionName}:OtlpEndpoint must be an absolute http(s) base URL without credentials or query.");
    }

    public static bool IsExcludedPath(PathString path) =>
        path.StartsWithSegments("/health")
        || path.StartsWithSegments("/css")
        || path.StartsWithSegments("/js")
        || path.StartsWithSegments("/vendor")
        || path.StartsWithSegments("/brand")
        || path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase);
}
