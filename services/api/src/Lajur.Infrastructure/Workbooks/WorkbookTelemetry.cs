using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Lajur.Infrastructure.Workbooks;

/// <summary>
/// Spans and metrics for workbook operations (ADR 0013). They carry only the operation
/// name, read/mutate kind, change source, replay flag, exception type and (on spans) the
/// workbook year; never user identifiers, paths, amounts or text.
/// </summary>
public static class WorkbookTelemetry
{
    /// <summary>Name of both the activity source and the meter.</summary>
    public const string SourceName = "Lajur.Workbooks";

    public const string OperationDurationMetric = "lajur.workbook.operation.duration";
    public const string LockWaitMetric = "lajur.workbook.lock.wait.duration";

    public const string OperationTag = "lajur.workbook.operation";
    public const string KindTag = "lajur.workbook.kind";
    public const string ChangeSourceTag = "lajur.workbook.change_source";
    public const string ReplayedTag = "lajur.workbook.replayed";
    public const string ErrorTypeTag = "error.type";
    public const string YearTag = "lajur.workbook.year";
    public const string LockWaitTag = "lajur.workbook.lock_wait_ms";

    // Seconds; workbook operations range from a few milliseconds to several seconds.
    private static readonly double[] DurationBuckets =
        [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30];

    private static readonly ActivitySource Source = new(SourceName);
    private static readonly Meter Meter = new(SourceName);

    private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram(
        OperationDurationMetric,
        unit: "s",
        description: "Workbook read/mutate duration, including the per-workbook lock wait.",
        tags: null,
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });

    private static readonly Histogram<double> LockWait = Meter.CreateHistogram(
        LockWaitMetric,
        unit: "s",
        description: "Time spent waiting for the per-workbook lock.",
        tags: null,
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });

    internal static Operation Start(
        string kind,
        string callerName,
        int year,
        WorkbookChangeSource? changeSource = null) =>
        new(kind, callerName, year, changeSource);

    internal sealed class Operation : IDisposable
    {
        private readonly string kind;
        private readonly string name;
        private readonly string? changeSource;
        private readonly long started = Stopwatch.GetTimestamp();
        private readonly Activity? activity;
        private string? errorType;
        private bool replayed;

        internal Operation(string kind, string callerName, int year, WorkbookChangeSource? source)
        {
            this.kind = kind;
            name = callerName.EndsWith("Async", StringComparison.Ordinal)
                ? callerName[..^"Async".Length]
                : callerName;
            changeSource = source?.ToString().ToLowerInvariant();

            if (Source.HasListeners())
            {
                activity = Source.StartActivity($"workbook {kind} {name}");
                activity?.SetTag(OperationTag, name);
                activity?.SetTag(YearTag, year);
                if (changeSource is not null)
                {
                    activity?.SetTag(ChangeSourceTag, changeSource);
                }
            }
        }

        public void LockAcquired()
        {
            var wait = Stopwatch.GetElapsedTime(started);
            activity?.SetTag(LockWaitTag, wait.TotalMilliseconds);
            if (LockWait.Enabled)
            {
                LockWait.Record(wait.TotalSeconds, BaseTags());
            }
        }

        /// <summary>An idempotent retry matched an operation that was already applied.</summary>
        public void MarkReplayed()
        {
            replayed = true;
            activity?.SetTag(ReplayedTag, true);
        }

        /// <summary>Exception filter helper: records the failure type and never catches.</summary>
        public bool MarkFailed(Exception exception)
        {
            errorType = exception.GetType().Name;
            activity?.SetStatus(ActivityStatusCode.Error, errorType);
            return false;
        }

        public void Dispose()
        {
            if (OperationDuration.Enabled)
            {
                var tags = BaseTags();
                if (kind == "mutate")
                {
                    tags.Add(ReplayedTag, replayed);
                }

                if (errorType is not null)
                {
                    tags.Add(ErrorTypeTag, errorType);
                }

                OperationDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
            }

            activity?.Dispose();
        }

        private TagList BaseTags()
        {
            var tags = new TagList { { KindTag, kind }, { OperationTag, name } };
            if (changeSource is not null)
            {
                tags.Add(ChangeSourceTag, changeSource);
            }

            return tags;
        }
    }
}
