using System.Diagnostics.Metrics;
using TUnit.Mocks.Logging;

namespace DotNetQuery.Core.Tests;

// Metrics use static instruments shared across the process. The tests that record against
// those instruments must not run in parallel with each other to prevent cross-test
// measurement pollution.
[NotInParallel(nameof(QueryInstrumentationTests))]
public class QueryInstrumentationTests
{
    private QueryInstrumentation _sut = default!;
    private MockLogger _logger = default!;

    [Before(Test)]
    public void Setup()
    {
        _logger = Mock.Logger();
        _sut = new(_logger);
    }

    [Test]
    public async Task RecordFetchStart_LogsDebugWithKey()
    {
        _sut.RecordFetchStart(QueryKey.From("users", 1));

        var entry = _logger.Entries.Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Debug);
        await Assert.That(entry.Message).Contains("users:1");
    }

    [Test]
    public async Task RecordFetchSuccess_LogsDebugWithKeyAndDuration()
    {
        _sut.RecordFetchSuccess(QueryKey.From("users", 1), 42.5);

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
        _sut.RecordFetchFailure(QueryKey.From("users", 1), 10.0, ex);

        var entry = _logger.Entries.Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Warning);
        await Assert.That(entry.Message).Contains("users:1");
        await Assert.That(entry.Exception).IsEqualTo(ex);
    }

    [Test]
    public async Task RecordFetchCancelled_LogsDebugWithKey()
    {
        _sut.RecordFetchCancelled(QueryKey.From("users", 1));

        var entry = _logger.Entries.Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Debug);
        await Assert.That(entry.Message).Contains("users:1");
    }

    [Test]
    public async Task RecordCacheHit_LogsDebugWithKey()
    {
        _sut.RecordCacheHit(QueryKey.From("users", 1));

        var entry = _logger.Entries.Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Debug);
        await Assert.That(entry.Message).Contains("users:1");
    }

    [Test]
    public async Task RecordCacheMiss_LogsDebugWithKey()
    {
        _sut.RecordCacheMiss(QueryKey.From("users", 1));

        var entry = _logger.Entries.Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Debug);
        await Assert.That(entry.Message).Contains("users:1");
    }

    [Test]
    public async Task RecordMutationStart_LogsDebug()
    {
        _sut.RecordMutationStart();

        var entry = _logger.Entries.Single();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Debug);
    }

    [Test]
    public async Task RecordMutationSuccess_LogsDebugWithDuration()
    {
        _sut.RecordMutationSuccess(99.0);

        var entry = _logger.Entries.Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Debug);
        await Assert.That(entry.Message).Contains("99");
    }

    [Test]
    public async Task RecordMutationFailure_LogsWarningWithException()
    {
        var ex = new InvalidOperationException("oops");
        _sut.RecordMutationFailure(5.0, ex);

        var entry = _logger.Entries.Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Warning);
        await Assert.That(entry.Exception).IsEqualTo(ex);
    }

    [Test]
    public async Task RecordMutationCancelled_LogsDebug()
    {
        _sut.RecordMutationCancelled();

        var entry = _logger.Entries.Single();
        await Assert.That(entry.LogLevel).IsEqualTo(LogLevel.Debug);
    }

    [Test]
    public async Task RecordFetchSuccess_RecordsFetchDurationWithTags()
    {
        var key = QueryKey.From("inst-fetch-success");
        double? recorded = null;
        List<KeyValuePair<string, object?>> recordedTags = [];

        using var listener = CreateKeyedMeterListener<double>(
            "dotnetquery.fetch.duration",
            key,
            (m, tags) =>
            {
                recorded = m;
                recordedTags = tags;
            }
        );

        _sut.RecordFetchStart(key);
        _sut.RecordFetchSuccess(key, 123.0);

        using var _ = Assert.Multiple();
        await Assert.That(recorded).IsEqualTo(123.0);
        await Assert
            .That(recordedTags)
            .Contains(
                new KeyValuePair<string, object?>(QueryTelemetryTags.TagStatus, QueryTelemetryTags.StatusSuccess)
            );
        await Assert
            .That(recordedTags)
            .Contains(new KeyValuePair<string, object?>(QueryTelemetryTags.TagQueryKey, key.ToString()));
    }

    [Test]
    public async Task RecordFetchFailure_RecordsFetchDurationWithTags()
    {
        var key = QueryKey.From("inst-fetch-failure");
        double? recorded = null;
        List<KeyValuePair<string, object?>> recordedTags = [];

        using var listener = CreateKeyedMeterListener<double>(
            "dotnetquery.fetch.duration",
            key,
            (m, tags) =>
            {
                recorded = m;
                recordedTags = tags;
            }
        );

        _sut.RecordFetchStart(key);
        _sut.RecordFetchFailure(key, 55.0, new Exception("err"));

        using var _ = Assert.Multiple();
        await Assert.That(recorded).IsEqualTo(55.0);
        await Assert
            .That(recordedTags)
            .Contains(
                new KeyValuePair<string, object?>(QueryTelemetryTags.TagStatus, QueryTelemetryTags.StatusFailure)
            );
    }

    [Test]
    public async Task RecordFetchStart_IncrementsActiveFetches()
    {
        var key = QueryKey.From("inst-active-inc");
        int? delta = null;

        using var listener = CreateKeyedMeterListener<int>("dotnetquery.fetch.active", key, (m, _) => delta = m);

        _sut.RecordFetchStart(key);

        await Assert.That(delta).IsEqualTo(1);
    }

    [Test]
    public async Task RecordFetchSuccess_DecrementsActiveFetches()
    {
        var key = QueryKey.From("inst-active-dec");
        var deltas = new List<int>();

        using var listener = CreateKeyedMeterListener<int>("dotnetquery.fetch.active", key, (m, _) => deltas.Add(m));

        _sut.RecordFetchStart(key);
        _sut.RecordFetchSuccess(key, 10.0);

        await Assert.That(deltas).IsEquivalentTo([1, -1]);
    }

    [Test]
    public async Task RecordCacheHit_IncrementsCacheHitsCounter()
    {
        var key = QueryKey.From("inst-cache-hit");
        long? recorded = null;

        using var listener = CreateKeyedMeterListener<long>("dotnetquery.cache.hits", key, (m, _) => recorded = m);

        _sut.RecordCacheHit(key);

        await Assert.That(recorded).IsEqualTo(1);
    }

    [Test]
    public async Task RecordCacheMiss_IncrementsCacheMissesCounter()
    {
        var key = QueryKey.From("inst-cache-miss");
        long? recorded = null;

        using var listener = CreateKeyedMeterListener<long>("dotnetquery.cache.misses", key, (m, _) => recorded = m);

        _sut.RecordCacheMiss(key);

        await Assert.That(recorded).IsEqualTo(1);
    }

    [Test]
    public async Task RecordMutationSuccess_RecordsMutationDurationWithStatusTag()
    {
        double? recorded = null;
        List<KeyValuePair<string, object?>> recordedTags = [];

        // Mutation duration has no query.key tag — filter by status value instead
        using var listener = CreateStatusMeterListener<double>(
            "dotnetquery.mutation.duration",
            QueryTelemetryTags.StatusSuccess,
            (m, tags) =>
            {
                recorded = m;
                recordedTags = tags;
            }
        );

        _sut.RecordMutationSuccess(77.0);

        using var _ = Assert.Multiple();
        await Assert.That(recorded).IsEqualTo(77.0);
        await Assert
            .That(recordedTags)
            .Contains(
                new KeyValuePair<string, object?>(QueryTelemetryTags.TagStatus, QueryTelemetryTags.StatusSuccess)
            );
    }

    [Test]
    public async Task RecordMutationFailure_RecordsMutationDurationWithStatusTag()
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

        _sut.RecordMutationFailure(33.0, new Exception("fail"));

        using var _ = Assert.Multiple();
        await Assert.That(recorded).IsEqualTo(33.0);
        await Assert
            .That(recordedTags)
            .Contains(
                new KeyValuePair<string, object?>(QueryTelemetryTags.TagStatus, QueryTelemetryTags.StatusFailure)
            );
    }

    /// <summary>Listens to a specific instrument and only fires the callback for the given query key.</summary>
    private static MeterListener CreateKeyedMeterListener<T>(
        string instrumentName,
        QueryKey key,
        Action<T, List<KeyValuePair<string, object?>>> onMeasurement
    )
        where T : struct =>
        CreateMeterListener<T>(
            instrumentName,
            (m, tags) =>
            {
                if (tags.Any(t => t.Key == QueryTelemetryTags.TagQueryKey && Equals(t.Value, key.ToString())))
                {
                    onMeasurement(m, tags);
                }
            }
        );

    /// <summary>Listens to a specific instrument and only fires the callback for the given status tag value.</summary>
    private static MeterListener CreateStatusMeterListener<T>(
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

    private static MeterListener CreateMeterListener<T>(
        string instrumentName,
        Action<T, List<KeyValuePair<string, object?>>> onMeasurement
    )
        where T : struct
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == QueryTelemetry.SourceName && instrument.Name == instrumentName)
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
