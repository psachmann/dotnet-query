namespace DotNetQuery.Core.Observability;

internal sealed class QueryInstrumentation(ILogger logger, Meter? meter = null)
{
    private readonly Histogram<double> _fetchDuration = (meter ?? QueryTelemetry.Meter).CreateHistogram<double>(
        "dotnetquery.query.duration",
        "ms",
        "Duration of query fetch operations."
    );

    private readonly UpDownCounter<int> _activeFetches = (meter ?? QueryTelemetry.Meter).CreateUpDownCounter<int>(
        "dotnetquery.query.active",
        description: "Number of query fetch operations currently in flight."
    );

    private readonly Counter<long> _cacheHits = (meter ?? QueryTelemetry.Meter).CreateCounter<long>(
        "dotnetquery.cache.hits",
        description: "Number of query cache hits."
    );

    private readonly Counter<long> _cacheMisses = (meter ?? QueryTelemetry.Meter).CreateCounter<long>(
        "dotnetquery.cache.misses",
        description: "Number of query cache misses."
    );

    private readonly Histogram<double> _mutationDuration = (meter ?? QueryTelemetry.Meter).CreateHistogram<double>(
        "dotnetquery.mutation.duration",
        "ms",
        "Duration of mutation operations."
    );

    // ── Query fetch ──────────────────────────────────────────────────────────

    internal void RecordFetchStart(QueryKey key)
    {
        _activeFetches.Add(1, new TagList { { QueryTelemetryTags.TagQueryKey, key.ToString() } });
        logger.LogDebug("Fetch started for key '{QueryKey}'", key);
    }

    internal void RecordFetchSuccess(QueryKey key, double durationMs)
    {
        var keyStr = key.ToString();
        _activeFetches.Add(-1, new TagList { { QueryTelemetryTags.TagQueryKey, keyStr } });
        _fetchDuration.Record(
            durationMs,
            new TagList
            {
                { QueryTelemetryTags.TagQueryKey, keyStr },
                { QueryTelemetryTags.TagStatus, QueryTelemetryTags.StatusSuccess },
            }
        );
        logger.LogDebug("Fetch succeeded for key '{QueryKey}' in {Duration}ms", keyStr, durationMs);
    }

    internal void RecordFetchFailure(QueryKey key, double durationMs, Exception ex)
    {
        var keyStr = key.ToString();
        _activeFetches.Add(-1, new TagList { { QueryTelemetryTags.TagQueryKey, keyStr } });
        _fetchDuration.Record(
            durationMs,
            new TagList
            {
                { QueryTelemetryTags.TagQueryKey, keyStr },
                { QueryTelemetryTags.TagStatus, QueryTelemetryTags.StatusFailure },
            }
        );
        logger.LogWarning(ex, "Fetch failed for key '{QueryKey}' after {Duration}ms", keyStr, durationMs);
    }

    internal void RecordFetchCancelled(QueryKey key)
    {
        _activeFetches.Add(-1, new TagList { { QueryTelemetryTags.TagQueryKey, key.ToString() } });
        logger.LogDebug("Fetch cancelled for key '{QueryKey}'", key);
    }

    // ── Cache ─────────────────────────────────────────────────────────────────

    internal void RecordCacheHit(QueryKey key)
    {
        _cacheHits.Add(1, new TagList { { QueryTelemetryTags.TagQueryKey, key.ToString() } });
        logger.LogDebug("Cache hit for key '{QueryKey}'", key);
    }

    internal void RecordCacheMiss(QueryKey key)
    {
        _cacheMisses.Add(1, new TagList { { QueryTelemetryTags.TagQueryKey, key.ToString() } });
        logger.LogDebug("Cache miss for key '{QueryKey}'", key);
    }

    // ── Mutation ──────────────────────────────────────────────────────────────

    internal void RecordMutationStart()
    {
        logger.LogDebug("Mutation started");
    }

    internal void RecordMutationSuccess(double durationMs)
    {
        _mutationDuration.Record(
            durationMs,
            new TagList { { QueryTelemetryTags.TagStatus, QueryTelemetryTags.StatusSuccess } }
        );
        logger.LogDebug("Mutation succeeded in {Duration}ms", durationMs);
    }

    internal void RecordMutationFailure(double durationMs, Exception ex)
    {
        _mutationDuration.Record(
            durationMs,
            new TagList { { QueryTelemetryTags.TagStatus, QueryTelemetryTags.StatusFailure } }
        );
        logger.LogWarning(ex, "Mutation failed after {Duration}ms", durationMs);
    }

    internal void RecordMutationCancelled()
    {
        logger.LogDebug("Mutation cancelled");
    }
}
