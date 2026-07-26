using System.Diagnostics.Metrics;
using TUnit.Mocks.Logging;

namespace DotNetQuery.Core.Tests;

public class QueryInstrumentationTests
{
    private QueryInstrumentation _sut = default!;
    private MockLogger _logger = default!;
    private Meter _meter = default!;

    [Before(Test)]
    public void Setup()
    {
        _logger = Mock.Logger();
        _meter = new Meter($"DotNetQuery-Test-{Guid.NewGuid()}");
        _sut = new(_logger, _meter);
    }

    [After(Test)]
    public void Teardown() => _meter.Dispose();

    [Test]
    public async Task RecordFetchStart_LogsDebugWithKey()
    {
        _sut.RecordFetchStart(QueryKey.From("users", 1), "users");

        var entry = _logger.Entries.Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Debug);
        await Assert.That(entry.Message).Contains("users:1");
    }

    [Test]
    public async Task RecordFetchSuccess_LogsDebugWithKeyAndDuration()
    {
        _sut.RecordFetchSuccess(QueryKey.From("users", 1), "users", 42.5, 1, FetchTrigger.Manual);

        var entry = _logger.Entries.Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Debug);
        await Assert.That(entry.Message).Contains("users:1");
        await Assert.That(entry.Message).Contains("42.5");
    }

    [Test]
    public async Task RecordFetchFailure_LogsWarningWithKeyAndException()
    {
        var ex = new InvalidOperationException("boom");
        _sut.RecordFetchFailure(QueryKey.From("users", 1), "users", 10.0, ex, 1, FetchTrigger.Manual);

        var entry = _logger.Entries.Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Warning);
        await Assert.That(entry.Message).Contains("users:1");
        await Assert.That(entry.Exception).IsEqualTo(ex);
    }

    [Test]
    public async Task RecordFetchCancelled_LogsDebugWithKey()
    {
        _sut.RecordFetchCancelled(QueryKey.From("users", 1), "users", 5.0, FetchTrigger.Manual);

        var entry = _logger.Entries.Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Debug);
        await Assert.That(entry.Message).Contains("users:1");
    }

    [Test]
    public async Task RecordCacheHit_LogsDebugWithKey()
    {
        _sut.RecordCacheHit(QueryKey.From("users", 1), "users");

        var entry = _logger.Entries.Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Debug);
        await Assert.That(entry.Message).Contains("users:1");
    }

    [Test]
    public async Task RecordCacheMiss_LogsDebugWithKey()
    {
        _sut.RecordCacheMiss(QueryKey.From("users", 1), "users");

        var entry = _logger.Entries.Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Debug);
        await Assert.That(entry.Message).Contains("users:1");
    }

    [Test]
    public async Task RecordCacheEviction_LogsDebugWithKey()
    {
        _sut.RecordCacheEviction(QueryKey.From("users", 1), "users");

        var entry = _logger.Entries.Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Debug);
        await Assert.That(entry.Message).Contains("users:1");
    }

    [Test]
    public async Task RecordMutationStart_LogsDebugWithName()
    {
        _sut.RecordMutationStart("createTodo");

        var entry = _logger.Entries.Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Debug);
        await Assert.That(entry.Message).Contains("createTodo");
    }

    [Test]
    public async Task RecordMutationSuccess_LogsDebugWithDuration()
    {
        _sut.RecordMutationSuccess("createTodo", 99.0, 1);

        var entry = _logger.Entries.Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Debug);
        await Assert.That(entry.Message).Contains("99");
    }

    [Test]
    public async Task RecordMutationFailure_LogsWarningWithException()
    {
        var ex = new InvalidOperationException("oops");
        _sut.RecordMutationFailure("createTodo", 5.0, ex, 1);

        var entry = _logger.Entries.Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Warning);
        await Assert.That(entry.Exception).IsEqualTo(ex);
    }

    [Test]
    public async Task RecordMutationCancelled_LogsDebug()
    {
        _sut.RecordMutationCancelled("createTodo", 3.0);

        var entry = _logger.Entries.Single();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Debug);
    }

    [Test]
    public async Task RecordFetchSuccess_RecordsFetchDurationWithTags()
    {
        var key = QueryKey.From("inst-fetch-success");
        double? recorded = null;
        List<KeyValuePair<string, object?>> recordedTags = [];

        using var listener = CreateNamedMeterListener<double>(
            "dotnetquery.query.duration",
            "inst-fetch-success",
            (m, tags) =>
            {
                recorded = m;
                recordedTags = tags;
            }
        );

        _sut.RecordFetchStart(key, "inst-fetch-success");
        _sut.RecordFetchSuccess(key, "inst-fetch-success", 123.0, 1, FetchTrigger.Manual);

        using var _ = Assert.Multiple();
        await Assert.That(recorded).IsEqualTo(123.0);
        await Assert
            .That(recordedTags)
            .Contains(
                new KeyValuePair<string, object?>(QueryTelemetryTags.TagStatus, QueryTelemetryTags.StatusSuccess)
            );
        await Assert
            .That(recordedTags)
            .Contains(new KeyValuePair<string, object?>(QueryTelemetryTags.TagTrigger, "manual"));
        await Assert.That(recordedTags.Any(t => t.Key == QueryTelemetryTags.TagQueryKey)).IsFalse();
    }

    [Test]
    public async Task RecordFetchFailure_RecordsFetchDurationWithTags()
    {
        var key = QueryKey.From("inst-fetch-failure");
        double? recorded = null;
        List<KeyValuePair<string, object?>> recordedTags = [];

        using var listener = CreateNamedMeterListener<double>(
            "dotnetquery.query.duration",
            "inst-fetch-failure",
            (m, tags) =>
            {
                recorded = m;
                recordedTags = tags;
            }
        );

        _sut.RecordFetchStart(key, "inst-fetch-failure");
        _sut.RecordFetchFailure(
            key,
            "inst-fetch-failure",
            55.0,
            new InvalidOperationException("err"),
            1,
            FetchTrigger.Invalidate
        );

        using var _ = Assert.Multiple();
        await Assert.That(recorded).IsEqualTo(55.0);
        await Assert
            .That(recordedTags)
            .Contains(
                new KeyValuePair<string, object?>(QueryTelemetryTags.TagStatus, QueryTelemetryTags.StatusFailure)
            );
        await Assert
            .That(recordedTags)
            .Contains(
                new KeyValuePair<string, object?>(QueryTelemetryTags.TagErrorType, nameof(InvalidOperationException))
            );
    }

    [Test]
    public async Task RecordFetchCancelled_RecordsFetchDurationWithCancelledStatus()
    {
        var key = QueryKey.From("inst-fetch-cancelled");
        double? recorded = null;
        List<KeyValuePair<string, object?>> recordedTags = [];

        using var listener = CreateNamedMeterListener<double>(
            "dotnetquery.query.duration",
            "inst-fetch-cancelled",
            (m, tags) =>
            {
                recorded = m;
                recordedTags = tags;
            }
        );

        _sut.RecordFetchStart(key, "inst-fetch-cancelled");
        _sut.RecordFetchCancelled(key, "inst-fetch-cancelled", 20.0, FetchTrigger.Interval);

        using var _ = Assert.Multiple();
        await Assert.That(recorded).IsEqualTo(20.0);
        await Assert
            .That(recordedTags)
            .Contains(
                new KeyValuePair<string, object?>(QueryTelemetryTags.TagStatus, QueryTelemetryTags.StatusCancelled)
            );
        await Assert
            .That(recordedTags)
            .Contains(new KeyValuePair<string, object?>(QueryTelemetryTags.TagTrigger, "interval"));
    }

    [Test]
    public async Task RecordFetchSuccess_WithRetries_RecordsQueryRetriesCounter()
    {
        var key = QueryKey.From("inst-retries");
        long? recorded = null;

        using var listener = CreateNamedMeterListener<long>(
            "dotnetquery.query.retries",
            "inst-retries",
            (m, _) => recorded = m
        );

        _sut.RecordFetchStart(key, "inst-retries");
        _sut.RecordFetchSuccess(key, "inst-retries", 12.0, attempts: 3, FetchTrigger.Manual);

        await Assert.That(recorded).IsEqualTo(2);
    }

    [Test]
    public async Task RecordFetchSuccess_SingleAttempt_DoesNotRecordRetries()
    {
        var key = QueryKey.From("inst-no-retries");
        long? recorded = null;

        using var listener = CreateNamedMeterListener<long>(
            "dotnetquery.query.retries",
            "inst-no-retries",
            (m, _) => recorded = m
        );

        _sut.RecordFetchStart(key, "inst-no-retries");
        _sut.RecordFetchSuccess(key, "inst-no-retries", 12.0, attempts: 1, FetchTrigger.Manual);

        await Assert.That(recorded).IsNull();
    }

    [Test]
    public async Task RecordFetchStart_IncrementsActiveFetches()
    {
        var key = QueryKey.From("inst-active-inc");
        int? delta = null;

        using var listener = CreateNamedMeterListener<int>(
            "dotnetquery.query.active",
            "inst-active-inc",
            (m, _) => delta = m
        );

        _sut.RecordFetchStart(key, "inst-active-inc");

        await Assert.That(delta).IsEqualTo(1);
    }

    [Test]
    public async Task RecordFetchSuccess_DecrementsActiveFetches()
    {
        var key = QueryKey.From("inst-active-dec");
        var deltas = new List<int>();

        using var listener = CreateNamedMeterListener<int>(
            "dotnetquery.query.active",
            "inst-active-dec",
            (m, _) => deltas.Add(m)
        );

        _sut.RecordFetchStart(key, "inst-active-dec");
        _sut.RecordFetchSuccess(key, "inst-active-dec", 10.0, 1, FetchTrigger.Manual);

        await Assert.That(deltas).IsEquivalentTo([1, -1]);
    }

    [Test]
    public async Task RecordCacheHit_IncrementsCacheHitsCounter()
    {
        var key = QueryKey.From("inst-cache-hit");
        long? recorded = null;

        using var listener = CreateNamedMeterListener<long>(
            "dotnetquery.cache.hits",
            "inst-cache-hit",
            (m, _) => recorded = m
        );

        _sut.RecordCacheHit(key, "inst-cache-hit");

        await Assert.That(recorded).IsEqualTo(1);
    }

    [Test]
    public async Task RecordCacheMiss_IncrementsCacheMissesCounterAndEntries()
    {
        var key = QueryKey.From("inst-cache-miss");
        long? misses = null;
        int? entries = null;

        using var missListener = CreateNamedMeterListener<long>(
            "dotnetquery.cache.misses",
            "inst-cache-miss",
            (m, _) => misses = m
        );
        using var entriesListener = CreateNamedMeterListener<int>(
            "dotnetquery.cache.entries",
            "inst-cache-miss",
            (m, _) => entries = m
        );

        _sut.RecordCacheMiss(key, "inst-cache-miss");

        using var _ = Assert.Multiple();
        await Assert.That(misses).IsEqualTo(1);
        await Assert.That(entries).IsEqualTo(1);
    }

    [Test]
    public async Task RecordCacheEviction_IncrementsEvictionsAndDecrementsEntries()
    {
        var key = QueryKey.From("inst-cache-evict");
        long? evictions = null;
        int? entries = null;

        using var evictionListener = CreateNamedMeterListener<long>(
            "dotnetquery.cache.evictions",
            "inst-cache-evict",
            (m, _) => evictions = m
        );
        using var entriesListener = CreateNamedMeterListener<int>(
            "dotnetquery.cache.entries",
            "inst-cache-evict",
            (m, _) => entries = m
        );

        _sut.RecordCacheMiss(key, "inst-cache-evict");
        _sut.RecordCacheEviction(key, "inst-cache-evict");

        using var _ = Assert.Multiple();
        await Assert.That(evictions).IsEqualTo(1);
        await Assert.That(entries).IsEqualTo(-1);
    }

    [Test]
    public async Task RecordMutationSuccess_RecordsMutationDurationWithNameAndStatusTag()
    {
        double? recorded = null;
        List<KeyValuePair<string, object?>> recordedTags = [];

        using var listener = CreateStatusMeterListener<double>(
            "dotnetquery.mutation.duration",
            QueryTelemetryTags.StatusSuccess,
            (m, tags) =>
            {
                recorded = m;
                recordedTags = tags;
            }
        );

        _sut.RecordMutationSuccess("createTodo", 77.0, 1);

        using var _ = Assert.Multiple();
        await Assert.That(recorded).IsEqualTo(77.0);
        await Assert
            .That(recordedTags)
            .Contains(new KeyValuePair<string, object?>(QueryTelemetryTags.TagMutationName, "createTodo"));
    }

    [Test]
    public async Task RecordMutationSuccess_WithRetries_RecordsMutationRetriesCounter()
    {
        long? recorded = null;

        using var listener = CreateMeterListener<long>(
            "dotnetquery.mutation.retries",
            (m, tags) =>
            {
                if (tags.Any(t => t.Key == QueryTelemetryTags.TagMutationName && Equals(t.Value, "createTodo")))
                {
                    recorded = m;
                }
            }
        );

        _sut.RecordMutationSuccess("createTodo", 77.0, attempts: 4);

        await Assert.That(recorded).IsEqualTo(3);
    }

    [Test]
    public async Task RecordMutationFailure_RecordsMutationDurationWithErrorType()
    {
        double? recorded = null;
        List<KeyValuePair<string, object?>> recordedTags = [];

        using var listener = CreateStatusMeterListener<double>(
            "dotnetquery.mutation.duration",
            QueryTelemetryTags.StatusFailure,
            (m, tags) =>
            {
                recorded = m;
                recordedTags = tags;
            }
        );

        _sut.RecordMutationFailure("createTodo", 33.0, new InvalidOperationException("fail"), 1);

        using var _ = Assert.Multiple();
        await Assert.That(recorded).IsEqualTo(33.0);
        await Assert
            .That(recordedTags)
            .Contains(
                new KeyValuePair<string, object?>(QueryTelemetryTags.TagErrorType, nameof(InvalidOperationException))
            );
    }

    [Test]
    public async Task RecordMutationCancelled_RecordsMutationDurationWithCancelledStatus()
    {
        double? recorded = null;
        List<KeyValuePair<string, object?>> recordedTags = [];

        using var listener = CreateStatusMeterListener<double>(
            "dotnetquery.mutation.duration",
            QueryTelemetryTags.StatusCancelled,
            (m, tags) =>
            {
                recorded = m;
                recordedTags = tags;
            }
        );

        _sut.RecordMutationCancelled("createTodo", 8.0);

        using var _ = Assert.Multiple();
        await Assert.That(recorded).IsEqualTo(8.0);
        await Assert
            .That(recordedTags)
            .Contains(new KeyValuePair<string, object?>(QueryTelemetryTags.TagMutationName, "createTodo"));
    }

    [Test]
    public async Task WithIncludeQueryKeyInMetrics_AddsQueryKeyTag()
    {
        var sutWithKey = new QueryInstrumentation(_logger, _meter, includeQueryKeyInMetrics: true);
        var key = QueryKey.From("inst-with-key", 7);
        List<KeyValuePair<string, object?>> recordedTags = [];

        using var listener = CreateNamedMeterListener<int>(
            "dotnetquery.query.active",
            "inst-with-key",
            (_, tags) => recordedTags = tags
        );

        sutWithKey.RecordFetchStart(key, "inst-with-key");

        await Assert
            .That(recordedTags)
            .Contains(new KeyValuePair<string, object?>(QueryTelemetryTags.TagQueryKey, key.ToString()));
    }

    /// <summary>Listens to a specific instrument and only fires the callback for the given query.name.</summary>
    private MeterListener CreateNamedMeterListener<T>(
        string instrumentName,
        string name,
        Action<T, List<KeyValuePair<string, object?>>> onMeasurement
    )
        where T : struct =>
        CreateMeterListener<T>(
            instrumentName,
            (m, tags) =>
            {
                if (tags.Any(t => t.Key == QueryTelemetryTags.TagQueryName && Equals(t.Value, name)))
                {
                    onMeasurement(m, tags);
                }
            }
        );

    private MeterListener CreateStatusMeterListener<T>(
        string instrumentName,
        string statusValue,
        Action<T, List<KeyValuePair<string, object?>>> onMeasurement
    )
        where T : struct =>
        CreateMeterListener<T>(
            instrumentName,
            (m, tags) =>
            {
                if (tags.Any(t => t.Key == QueryTelemetryTags.TagStatus && Equals(t.Value, statusValue)))
                {
                    onMeasurement(m, tags);
                }
            }
        );

    private MeterListener CreateMeterListener<T>(
        string instrumentName,
        Action<T, List<KeyValuePair<string, object?>>> onMeasurement
    )
        where T : struct
    {
        var meter = _meter;
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (ReferenceEquals(instrument.Meter, meter) && instrument.Name == instrumentName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<T>(
            (_, measurement, tags, _) =>
            {
                var tagList = new List<KeyValuePair<string, object?>>();

                foreach (var tag in tags)
                {
                    tagList.Add(tag);
                }

                onMeasurement(measurement, tagList);
            }
        );

        listener.Start();

        return listener;
    }
}
