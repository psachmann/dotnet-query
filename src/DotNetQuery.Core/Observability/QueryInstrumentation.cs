namespace DotNetQuery.Core.Observability;

internal sealed class QueryInstrumentation(ILogger logger)
{
    // Instruments are static — created once on the shared Meter, no-op when no listener is attached.
    private static readonly Histogram<double> FetchDuration = QueryTelemetry.Meter.CreateHistogram<double>(
        "dotnetquery.query.duration",
        "ms",
        "Duration of query fetch operations."
    );

    private static readonly UpDownCounter<int> ActiveFetches = QueryTelemetry.Meter.CreateUpDownCounter<int>(
        "dotnetquery.query.active",
        description: "Number of query fetch operations currently in flight."
    );

    private static readonly Counter<long> CacheHits = QueryTelemetry.Meter.CreateCounter<long>(
        "dotnetquery.cache.hits",
        description: "Number of query cache hits."
    );

    private static readonly Counter<long> CacheMisses = QueryTelemetry.Meter.CreateCounter<long>(
        "dotnetquery.cache.misses",
        description: "Number of query cache misses."
    );

    private static readonly Histogram<double> MutationDuration = QueryTelemetry.Meter.CreateHistogram<double>(
        "dotnetquery.mutation.duration",
        "ms",
        "Duration of mutation operations."
    );

    // ── Query fetch ──────────────────────────────────────────────────────────

    internal void RecordFetchStart(QueryKey key)
    {
        ActiveFetches.Add(1, new TagList { { QueryTelemetryTags.TagQueryKey, key.ToString() } });
        logger.LogDebug("Fetch started for key '{QueryKey}'", key);
    }

    internal void RecordFetchSuccess(QueryKey key, double durationMs)
    {
        var keyStr = key.ToString();
        ActiveFetches.Add(-1, new TagList { { QueryTelemetryTags.TagQueryKey, keyStr } });
        FetchDuration.Record(
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
        ActiveFetches.Add(-1, new TagList { { QueryTelemetryTags.TagQueryKey, keyStr } });
        FetchDuration.Record(
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
        ActiveFetches.Add(-1, new TagList { { QueryTelemetryTags.TagQueryKey, key.ToString() } });
        logger.LogDebug("Fetch cancelled for key '{QueryKey}'", key);
    }

    // ── Cache ─────────────────────────────────────────────────────────────────

    internal void RecordCacheHit(QueryKey key)
    {
        CacheHits.Add(1, new TagList { { QueryTelemetryTags.TagQueryKey, key.ToString() } });
        logger.LogDebug("Cache hit for key '{QueryKey}'", key);
    }

    internal void RecordCacheMiss(QueryKey key)
    {
        CacheMisses.Add(1, new TagList { { QueryTelemetryTags.TagQueryKey, key.ToString() } });
        logger.LogDebug("Cache miss for key '{QueryKey}'", key);
    }

    // ── Mutation ──────────────────────────────────────────────────────────────

    internal void RecordMutationStart()
    {
        logger.LogDebug("Mutation started");
    }

    internal void RecordMutationSuccess(double durationMs)
    {
        MutationDuration.Record(
            durationMs,
            new TagList { { QueryTelemetryTags.TagStatus, QueryTelemetryTags.StatusSuccess } }
        );
        logger.LogDebug("Mutation succeeded in {Duration}ms", durationMs);
    }

    internal void RecordMutationFailure(double durationMs, Exception ex)
    {
        MutationDuration.Record(
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
