using System.Diagnostics;
using Lajur.Api.Telemetry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace Lajur.IntegrationTests;

public sealed class TelemetryTests
{
    [Fact]
    public void Telemetry_is_not_registered_without_an_endpoint()
    {
        using var factory = new LajurApiFactory();

        Assert.Null(factory.Services.GetService<TracerProvider>());
    }

    [Fact]
    public async Task Request_span_is_exported_without_identifying_attributes()
    {
        var exported = new List<Activity>();
        using var factory = new LajurApiFactory().WithWebHostBuilder(builder =>
        {
            // Discard port: OTLP export fails fast; spans are asserted through the in-memory exporter.
            builder.UseSetting("Lajur:Telemetry:OtlpEndpoint", "http://127.0.0.1:9");
            builder.ConfigureTestServices(services =>
                services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(exported)));
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LajurTelemetryTest/1.0");

        using var health = await client.GetAsync("/health/live");
        using var response = await client.GetAsync("/api/v1/auth/registration?invite=secret-value");

        var span = await WaitForServerSpanAsync(exported, factory.Services);
        Assert.Equal("GET api/v1/auth/registration", span.DisplayName);
        Assert.DoesNotContain(exported, activity => activity.DisplayName.Contains("health", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(span.TagObjects, tag => TelemetryRedactionProcessor.IsRedacted(tag.Key));
        Assert.DoesNotContain(
            span.TagObjects,
            tag => tag.Value?.ToString()?.Contains("secret-value", StringComparison.Ordinal) == true);
        Assert.Equal("api/v1/auth/registration", span.GetTagItem("http.route"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_endpoint_disables_telemetry(string? value) =>
        Assert.Null(LajurTelemetry.ParseEndpoint(value));

    [Theory]
    [InlineData("http://otel-gateway:4318")]
    [InlineData("https://otel.example.test")]
    public void Absolute_http_endpoint_is_accepted(string value) =>
        Assert.Equal(new Uri(value), LajurTelemetry.ParseEndpoint(value));

    [Theory]
    [InlineData("otel-gateway:4318")]
    [InlineData("ftp://otel.example.test")]
    [InlineData("https://user:pass@otel.example.test")]
    [InlineData("https://otel.example.test/?token=abc")]
    public void Malformed_endpoint_fails_without_echoing_the_value(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => LajurTelemetry.ParseEndpoint(value));
        Assert.DoesNotContain(value, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("client.address", true)]
    [InlineData("url.query", true)]
    [InlineData("user_agent.original", true)]
    [InlineData("enduser.id", true)]
    [InlineData("http.request.header.authorization", true)]
    [InlineData("http.route", false)]
    [InlineData("http.response.status_code", false)]
    [InlineData("url.path", false)]
    public void Redaction_list_covers_identifying_attributes(string key, bool redacted) =>
        Assert.Equal(redacted, TelemetryRedactionProcessor.IsRedacted(key));

    [Theory]
    [InlineData("/health/ready", true)]
    [InlineData("/css/site.css", true)]
    [InlineData("/favicon.ico", true)]
    [InlineData("/api/v1/workbooks", false)]
    [InlineData("/buku", false)]
    public void Health_and_static_paths_are_not_traced(string path, bool excluded) =>
        Assert.Equal(excluded, LajurTelemetry.IsExcludedPath(new PathString(path)));

    private static async Task<Activity> WaitForServerSpanAsync(List<Activity> exported, IServiceProvider services)
    {
        // The server span ends after the response is handed to the client; wait briefly for it.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            services.GetRequiredService<TracerProvider>().ForceFlush();
            lock (exported)
            {
                var span = exported.FirstOrDefault(activity => activity.Kind == ActivityKind.Server);
                if (span is not null)
                {
                    return span;
                }
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("No server span was exported.");
            }

            await Task.Delay(50);
        }
    }
}
