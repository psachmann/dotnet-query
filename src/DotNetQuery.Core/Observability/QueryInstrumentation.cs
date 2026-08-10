namespace DotNetQuery.Core.Observability;

internal sealed partial class QueryInstrumentation(
    ILogger logger,
    Meter? meter = null,
    bool includeQueryKeyInMetrics = false
)
{
    private readonly Histogram<double> _fetchDuration = (meter ?? QueryTelemetry.Meter).CreateHistogram<double>(
        "dotnetquery.query.duration",
        "s",
        "Duration of query fetch operations."
    );

    private readonly UpDownCounter<int> _activeFetches = (meter ?? QueryTelemetry.Meter).CreateUpDownCounter<int>(
        "dotnetquery.query.active",
        description: "Number of query fetch operations currently in flight."
    );

    private readonly Counter<long> _cacheHits = (meter ?? QueryTelemetry.Meter).CreateCounter<long>(
        "dotnetquery.cache.hits",
        description: "Number of query cache lookups that found an existing entry."
    );

    private readonly Counter<long> _cacheMisses = (meter ?? QueryTelemetry.Meter).CreateCounter<long>(
        "dotnetquery.cache.misses",
        description: "Number of query cache lookups that created a new entry."
    );

    private readonly UpDownCounter<int> _cacheEntries = (meter ?? QueryTelemetry.Meter).CreateUpDownCounter<int>(
        "dotnetquery.cache.entries",
        description: "Number of entries currently held in the query cache."
    );

    private readonly Counter<long> _cacheEvictions = (meter ?? QueryTelemetry.Meter).CreateCounter<long>(
        "dotnetquery.cache.evictions",
        description: "Number of query cache entries automatically evicted after CacheTime elapsed."
    );

    private readonly Counter<long> _queryRetries = (meter ?? QueryTelemetry.Meter).CreateCounter<long>(
        "dotnetquery.query.retries",
        description: "Number of retry attempts made by query fetches beyond the first."
    );

    private readonly Histogram<double> _mutationDuration = (meter ?? QueryTelemetry.Meter).CreateHistogram<double>(
        "dotnetquery.mutation.duration",
        "s",
        "Duration of mutation operations."
    );

    private readonly Counter<long> _mutationRetries = (meter ?? QueryTelemetry.Meter).CreateCounter<long>(
        "dotnetquery.mutation.retries",
        description: "Number of retry attempts made by mutations beyond the first."
    );

    private TagList BuildTags(
        string name,
        QueryKey? key = null,
        string? status = null,
        string? errorType = null,
        FetchTrigger? trigger = null
    )
    {
        var tags = new TagList { { QueryTelemetryTags.TagQueryName, name } };

        if (status is not null)
        {
            tags.Add(QueryTelemetryTags.TagStatus, status);
        }

        if (errorType is not null)
        {
            tags.Add(QueryTelemetryTags.TagErrorType, errorType);
        }

        if (trigger is { } t)
        {
            tags.Add(QueryTelemetryTags.TagTrigger, t.ToTagValue());
        }

        if (includeQueryKeyInMetrics && key is not null)
        {
            tags.Add(QueryTelemetryTags.TagQueryKey, key.ToString());
        }

        return tags;
    }

    // ── Query fetch ──────────────────────────────────────────────────────────

    internal void RecordFetchStart(QueryKey key, string name)
    {
        _activeFetches.Add(1, BuildTags(name, key));
        LogFetchStarted(key.ToString());
    }

    internal void RecordFetchSuccess(QueryKey key, string name, TimeSpan duration, int attempts, FetchTrigger trigger)
    {
        _activeFetches.Add(-1, BuildTags(name, key));
        _fetchDuration.Record(
            duration.TotalSeconds,
            BuildTags(name, key, QueryTelemetryTags.StatusSuccess, trigger: trigger)
        );

        if (attempts > 1)
        {
            _queryRetries.Add(attempts - 1, BuildTags(name, key));
        }

        LogFetchSucceeded(key.ToString(), duration.TotalMilliseconds);
    }

    internal void RecordFetchFailure(
        QueryKey key,
        string name,
        TimeSpan duration,
        Exception ex,
        int attempts,
        FetchTrigger trigger
    )
    {
        _activeFetches.Add(-1, BuildTags(name, key));
        _fetchDuration.Record(
            duration.TotalSeconds,
            BuildTags(name, key, QueryTelemetryTags.StatusFailure, ex.GetType().Name, trigger)
        );

        if (attempts > 1)
        {
            _queryRetries.Add(attempts - 1, BuildTags(name, key));
        }

        LogFetchFailed(ex, key.ToString(), duration.TotalMilliseconds);
    }

    internal void RecordFetchCancelled(QueryKey key, string name, TimeSpan duration, FetchTrigger trigger)
    {
        _activeFetches.Add(-1, BuildTags(name, key));
        _fetchDuration.Record(
            duration.TotalSeconds,
            BuildTags(name, key, QueryTelemetryTags.StatusCancelled, trigger: trigger)
        );
        LogFetchCancelled(key.ToString());
    }

    // ── Cache ─────────────────────────────────────────────────────────────────

    internal void RecordCacheHit(QueryKey key, string name)
    {
        _cacheHits.Add(1, BuildTags(name, key));
        LogCacheHit(key.ToString());
    }

    internal void RecordCacheMiss(QueryKey key, string name)
    {
        _cacheMisses.Add(1, BuildTags(name, key));
        _cacheEntries.Add(1, BuildTags(name, key));
        LogCacheMiss(key.ToString());
    }

    internal void RecordCacheEviction(QueryKey key, string name)
    {
        _cacheEntries.Add(-1, BuildTags(name, key));
        _cacheEvictions.Add(1, BuildTags(name, key));
        LogCacheEvicted(key.ToString());
    }

    /// <summary>
    /// Records an entry released because the whole cache was disposed — decrements the entry gauge
    /// without counting an eviction. Without this, scoped (SSR) clients would leak their live-entry
    /// count into the process-wide gauge on every scope disposal.
    /// </summary>
    internal void RecordCacheDisposed(QueryKey key, string name)
    {
        _cacheEntries.Add(-1, BuildTags(name, key));
        LogCacheDisposed(key.ToString());
    }

    // ── Mutation ──────────────────────────────────────────────────────────────

    internal void RecordMutationStart(string name)
    {
        LogMutationStarted(name);
    }

    internal void RecordMutationSuccess(string name, TimeSpan duration, int attempts)
    {
        _mutationDuration.Record(
            duration.TotalSeconds,
            new TagList
            {
                { QueryTelemetryTags.TagMutationName, name },
                { QueryTelemetryTags.TagStatus, QueryTelemetryTags.StatusSuccess },
            }
        );

        if (attempts > 1)
        {
            _mutationRetries.Add(attempts - 1, new TagList { { QueryTelemetryTags.TagMutationName, name } });
        }

        LogMutationSucceeded(name, duration.TotalMilliseconds);
    }

    internal void RecordMutationFailure(string name, TimeSpan duration, Exception ex, int attempts)
    {
        _mutationDuration.Record(
            duration.TotalSeconds,
            new TagList
            {
                { QueryTelemetryTags.TagMutationName, name },
                { QueryTelemetryTags.TagStatus, QueryTelemetryTags.StatusFailure },
                { QueryTelemetryTags.TagErrorType, ex.GetType().Name },
            }
        );

        if (attempts > 1)
        {
            _mutationRetries.Add(attempts - 1, new TagList { { QueryTelemetryTags.TagMutationName, name } });
        }

        LogMutationFailed(ex, name, duration.TotalMilliseconds);
    }

    internal void RecordMutationCancelled(string name, TimeSpan duration)
    {
        _mutationDuration.Record(
            duration.TotalSeconds,
            new TagList
            {
                { QueryTelemetryTags.TagMutationName, name },
                { QueryTelemetryTags.TagStatus, QueryTelemetryTags.StatusCancelled },
            }
        );
        LogMutationCancelled(name);
    }

    // ── Source-generated log messages ────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fetch started for key '{QueryKey}'")]
    private partial void LogFetchStarted(string queryKey);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fetch succeeded for key '{QueryKey}' in {Duration}ms")]
    private partial void LogFetchSucceeded(string queryKey, double duration);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fetch failed for key '{QueryKey}' after {Duration}ms")]
    private partial void LogFetchFailed(Exception exception, string queryKey, double duration);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Fetch cancelled for key '{QueryKey}'")]
    private partial void LogFetchCancelled(string queryKey);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache hit for key '{QueryKey}'")]
    private partial void LogCacheHit(string queryKey);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache miss for key '{QueryKey}'")]
    private partial void LogCacheMiss(string queryKey);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Cache entry for key '{QueryKey}' evicted after CacheTime elapsed"
    )]
    private partial void LogCacheEvicted(string queryKey);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache entry for key '{QueryKey}' released on cache dispose")]
    private partial void LogCacheDisposed(string queryKey);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mutation '{MutationName}' started")]
    private partial void LogMutationStarted(string mutationName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mutation '{MutationName}' succeeded in {Duration}ms")]
    private partial void LogMutationSucceeded(string mutationName, double duration);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mutation '{MutationName}' failed after {Duration}ms")]
    private partial void LogMutationFailed(Exception exception, string mutationName, double duration);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mutation '{MutationName}' cancelled")]
    private partial void LogMutationCancelled(string mutationName);
}
