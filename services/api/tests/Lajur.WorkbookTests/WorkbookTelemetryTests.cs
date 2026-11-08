using System.Diagnostics;
using Lajur.Infrastructure.Workbooks;

namespace Lajur.WorkbookTests;

public sealed class WorkbookTelemetryTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "LajurWorkbookFixture.xlsm");

    [Fact]
    public async Task Workbook_read_emits_span_with_only_operation_year_and_lock_wait()
    {
        var root = Directory.CreateTempSubdirectory("lajur-telemetry-");
        try
        {
            var key = new WorkbookKey(Guid.NewGuid(), 2026);
            var path = Path.Combine(root.FullName, "users", key.UserId.ToString("D"), "workbooks", "Keuangan_Pribadi_2026.xlsm");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(FixturePath, path);
            using var capture = new ActivityCapture(key);

            _ = await new OpenXmlWorkbookStore(root.FullName).ListTransactionsAsync(key, 1);

            var activity = Assert.Single(capture.Activities);
            Assert.Equal("workbook read ListTransactions", activity.DisplayName);
            Assert.Equal(ActivityStatusCode.Unset, activity.Status);
            Assert.Equal(
                [WorkbookTelemetry.LockWaitTag, WorkbookTelemetry.OperationTag, WorkbookTelemetry.YearTag],
                activity.TagObjects.Select(tag => tag.Key).Order().ToArray());
            Assert.Equal("ListTransactions", activity.GetTagItem(WorkbookTelemetry.OperationTag));
            Assert.Equal(2026, activity.GetTagItem(WorkbookTelemetry.YearTag));
            AssertNoIdentifyingValues(activity, key, path);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Failed_workbook_read_marks_span_with_exception_type_only()
    {
        var root = Directory.CreateTempSubdirectory("lajur-telemetry-");
        try
        {
            var key = new WorkbookKey(Guid.NewGuid(), 2026);
            using var capture = new ActivityCapture(key);

            await Assert.ThrowsAsync<FileNotFoundException>(
                () => new OpenXmlWorkbookStore(root.FullName).ReadYearAsync(key));

            var activity = Assert.Single(capture.Activities);
            Assert.Equal("workbook read ReadYear", activity.DisplayName);
            Assert.Equal(ActivityStatusCode.Error, activity.Status);
            Assert.Equal(nameof(FileNotFoundException), activity.StatusDescription);
            Assert.Empty(activity.Events);
            AssertNoIdentifyingValues(activity, key, root.FullName);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static void AssertNoIdentifyingValues(Activity activity, WorkbookKey key, string path)
    {
        var values = activity.TagObjects.Select(tag => tag.Value?.ToString() ?? string.Empty)
            .Append(activity.DisplayName)
            .Append(activity.StatusDescription ?? string.Empty);
        Assert.DoesNotContain(values, value => value.Contains(key.UserId.ToString("D"), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(values, value => value.Contains(path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Captures workbook spans for one key only, so parallel tests in this assembly do not leak in.
    /// </summary>
    private sealed class ActivityCapture : IDisposable
    {
        private readonly ActivityListener listener;
        private readonly List<Activity> activities = [];
        private readonly Activity? root;

        public ActivityCapture(WorkbookKey key)
        {
            var testRoot = new ActivitySource("Lajur.WorkbookTests.Telemetry");
            listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name is WorkbookTelemetry.SourceName or "Lajur.WorkbookTests.Telemetry",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.Source.Name == WorkbookTelemetry.SourceName && activity.ParentId == root?.Id)
                    {
                        lock (activities)
                        {
                            activities.Add(activity);
                        }
                    }
                }
            };
            ActivitySource.AddActivityListener(listener);
            // Becomes Activity.Current for the calling test, so workbook spans parent to it.
            root = testRoot.StartActivity($"test {key.UserId:N}");
        }

        public IReadOnlyList<Activity> Activities
        {
            get
            {
                lock (activities)
                {
                    return activities.ToArray();
                }
            }
        }

        public void Dispose()
        {
            root?.Dispose();
            listener.Dispose();
        }
    }
}
