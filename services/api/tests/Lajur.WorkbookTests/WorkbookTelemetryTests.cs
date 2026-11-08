using System.Diagnostics;
using System.Diagnostics.Metrics;
using Lajur.Infrastructure.Workbooks;

namespace Lajur.WorkbookTests;

/// <summary>Meter listeners are process-wide, so these tests must not run beside other store tests.</summary>
[CollectionDefinition(nameof(WorkbookTelemetryCollection), DisableParallelization = true)]
public sealed class WorkbookTelemetryCollection;

[Collection(nameof(WorkbookTelemetryCollection))]
public sealed class WorkbookTelemetryTests
{
    private static readonly HashSet<string> AllowedMetricTags =
    [
        WorkbookTelemetry.KindTag,
        WorkbookTelemetry.OperationTag,
        WorkbookTelemetry.ChangeSourceTag,
        WorkbookTelemetry.ReplayedTag,
        WorkbookTelemetry.ErrorTypeTag
    ];

    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "LajurWorkbookFixture.xlsm");

    [Fact]
    public async Task Android_create_records_applied_replayed_and_conflicting_outcomes()
    {
        var root = Directory.CreateTempSubdirectory("lajur-telemetry-");
        try
        {
            var key = CopyFixture(root);
            var store = new OpenXmlWorkbookStore(root.FullName);
            var id = Guid.Parse("4f6b1d7e-0c55-4d43-9d0f-6a3e2b9c1a10");
            var audit = new WorkbookAuditContext(WorkbookChangeSource.Android, "device-test", "telemetry-create");
            var command = new CreateWorkbookTransactionCommand(id, Draft(456_000), audit);
            using var metrics = new MetricCapture();

            await store.CreateTransactionAsync(key, command);
            await store.CreateTransactionAsync(key, command);
            await Assert.ThrowsAsync<WorkbookTransactionIdConflictException>(
                () => store.CreateTransactionAsync(key, command with { Transaction = Draft(1) }));

            var durations = metrics.Recorded(WorkbookTelemetry.OperationDurationMetric);
            Assert.Equal(3, durations.Count);
            Assert.All(durations, measurement =>
            {
                Assert.True(measurement.Value > 0);
                Assert.Equal("mutate", measurement.Tags[WorkbookTelemetry.KindTag]);
                Assert.Equal("CreateTransaction", measurement.Tags[WorkbookTelemetry.OperationTag]);
                Assert.Equal("android", measurement.Tags[WorkbookTelemetry.ChangeSourceTag]);
            });
            Assert.Equal([false, true, false], durations.Select(m => (bool)m.Tags[WorkbookTelemetry.ReplayedTag]!));
            Assert.Equal(
                [null, null, nameof(WorkbookTransactionIdConflictException)],
                durations.Select(m => m.Tags.GetValueOrDefault(WorkbookTelemetry.ErrorTypeTag)));
            Assert.Equal(3, metrics.Recorded(WorkbookTelemetry.LockWaitMetric).Count);
            Assert.All(metrics.All, measurement => Assert.Subset(AllowedMetricTags, measurement.Tags.Keys.ToHashSet()));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Failed_read_records_exception_type_without_replay_flag()
    {
        var root = Directory.CreateTempSubdirectory("lajur-telemetry-");
        try
        {
            var key = new WorkbookKey(Guid.NewGuid(), 2026);
            using var metrics = new MetricCapture();

            await Assert.ThrowsAsync<FileNotFoundException>(
                () => new OpenXmlWorkbookStore(root.FullName).ReadYearAsync(key));

            var measurement = Assert.Single(metrics.Recorded(WorkbookTelemetry.OperationDurationMetric));
            Assert.Equal("read", measurement.Tags[WorkbookTelemetry.KindTag]);
            Assert.Equal("ReadYear", measurement.Tags[WorkbookTelemetry.OperationTag]);
            Assert.Equal(nameof(FileNotFoundException), measurement.Tags[WorkbookTelemetry.ErrorTypeTag]);
            Assert.False(measurement.Tags.ContainsKey(WorkbookTelemetry.ReplayedTag));
            Assert.False(measurement.Tags.ContainsKey(WorkbookTelemetry.ChangeSourceTag));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static WorkbookKey CopyFixture(DirectoryInfo root)
    {
        var key = new WorkbookKey(Guid.NewGuid(), 2026);
        var path = Path.Combine(root.FullName, "users", key.UserId.ToString("D"), "workbooks", "Keuangan_Pribadi_2026.xlsm");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(FixturePath, path);
        return key;
    }

    private static WorkbookTransactionDraft Draft(long amount) => new(
        new DateOnly(2026, 1, 21),
        "Pengeluaran",
        "Transfer Uji",
        "Kategori Uji 06",
        amount,
        "Telemetri uji");

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

    private sealed record Measurement(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags);

    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener listener = new();
        private readonly List<Measurement> measurements = [];

        public MetricCapture()
        {
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == WorkbookTelemetry.SourceName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                var copy = new Dictionary<string, object?>();
                foreach (var tag in tags)
                {
                    copy[tag.Key] = tag.Value;
                }

                lock (measurements)
                {
                    measurements.Add(new Measurement(instrument.Name, value, copy));
                }
            });
            listener.Start();
        }

        public IReadOnlyList<Measurement> All
        {
            get
            {
                lock (measurements)
                {
                    return measurements.ToArray();
                }
            }
        }

        public IReadOnlyList<Measurement> Recorded(string instrument) =>
            All.Where(measurement => measurement.Instrument == instrument).ToArray();

        public void Dispose() => listener.Dispose();
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
