using System.Diagnostics;

namespace Lajur.Infrastructure.Workbooks;

/// <summary>
/// Activity source for workbook operations. Spans carry only the operation name,
/// workbook year and lock wait; never user identifiers, paths, amounts or text
/// (ADR 0013).
/// </summary>
public static class WorkbookTelemetry
{
    public const string SourceName = "Lajur.Workbooks";

    public const string OperationTag = "lajur.workbook.operation";
    public const string YearTag = "lajur.workbook.year";
    public const string LockWaitTag = "lajur.workbook.lock_wait_ms";

    private static readonly ActivitySource Source = new(SourceName);

    internal static Activity? Start(string kind, string callerName, int year)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var operation = callerName.EndsWith("Async", StringComparison.Ordinal)
            ? callerName[..^"Async".Length]
            : callerName;
        var activity = Source.StartActivity($"workbook {kind} {operation}");
        activity?.SetTag(OperationTag, operation);
        activity?.SetTag(YearTag, year);
        return activity;
    }

    internal static void RecordLockWait(Activity? activity, long startTimestamp) =>
        activity?.SetTag(LockWaitTag, Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);

    /// <summary>Exception filter helper: marks the span failed and never catches.</summary>
    internal static bool MarkFailed(Activity? activity, Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
        return false;
    }
}
