using System.Diagnostics.Metrics;

namespace DotNetQuery.Core.Tests;

public class QueryCacheTests
{
    private readonly TestScheduler _scheduler = new();
    private QueryCache _sut = default!;

    private static readonly QueryInstrumentation _instrumentation = new(NullLogger.Instance);

    [Before(Test)]
    public void Setup()
    {
        _sut = new(_scheduler, _instrumentation);
    }

    [After(Test)]
    public void Teardown()
    {
        _sut.Dispose();
    }

    private Query<int, string> CreateQuery(QueryKey key, TimeSpan? cacheTime = null)
    {
        var options = new EffectiveQueryOptions<int, string>
        {
            Fetcher = (_, _) => Task.FromResult("data"),
            StaleTime = TimeSpan.Zero,
            CacheTime = cacheTime ?? TimeSpan.FromMinutes(5),
            RefetchInterval = null,
            RetryHandler = new DefaultRetryHandler(),
            IsEnabled = true,
            DataComparer = EqualityComparer<string>.Default,
            InitialData = null,
            Name = null,
        };

        return new Query<int, string>(key, 0, options, _scheduler, _instrumentation);
    }

    [Test]
    public async Task GetOrCreate_NewKey_ReturnsProvidedQuery()
    {
        var key = QueryKey.From("a");
        using var query = CreateQuery(key);

        var result = _sut.GetOrCreate(key, query);

        await Assert.That(result).IsEqualTo(query);
    }

    [Test]
    public async Task GetOrCreate_ExistingKey_ReturnsFirstQuery()
    {
        var key = QueryKey.From("a");
        using var first = CreateQuery(key);
        using var second = CreateQuery(key);

        _sut.GetOrCreate(key, first);
        var result = _sut.GetOrCreate(key, second);

        await Assert.That(result).IsEqualTo(first);
    }

    [Test]
    public async Task GetOrCreate_AfterRemoveBeforeTimerFires_CancelsPendingRemovalAndReturnsExistingQuery()
    {
        var key = QueryKey.From("a");
        using var query = CreateQuery(key, TimeSpan.FromMinutes(5));
        _sut.GetOrCreate(key, query);
        _sut.Remove(key);

        // Re-add before timer fires — pending removal should be cancelled
        using var replacement = CreateQuery(key);
        var result = _sut.GetOrCreate(key, replacement);

        // Advance past _sut time — query should NOT be removed
        _scheduler.AdvanceBy(TimeSpan.FromMinutes(10).Ticks);

        // Key is still alive: adding again returns the same original query
        using var late = CreateQuery(key);
        var afterAdvance = _sut.GetOrCreate(key, late);

        using var _ = Assert.Multiple();
        await Assert.That(result).IsEqualTo(query);
        await Assert.That(afterAdvance).IsEqualTo(query);
    }

    [Test]
    public async Task GetOrCreate_SameKeyDifferentDataType_ThrowsInvalidOperationException()
    {
        var key = QueryKey.From("a");
        using var stringQuery = CreateQuery(key);
        _sut.GetOrCreate(key, stringQuery);

        var objectOptions = new EffectiveQueryOptions<int, object>
        {
            Fetcher = (_, _) => Task.FromResult<object>("data"),
            StaleTime = TimeSpan.Zero,
            CacheTime = TimeSpan.FromMinutes(5),
            RefetchInterval = null,
            RetryHandler = new DefaultRetryHandler(),
            IsEnabled = true,
            DataComparer = EqualityComparer<object>.Default,
            InitialData = null,
            Name = null,
        };
        using var objectQuery = new Query<int, object>(key, 0, objectOptions, _scheduler, _instrumentation);

        await Assert.That(() => _sut.GetOrCreate(key, objectQuery)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Remove_CalledTwiceBeforeTimerFires_StillEvictsAfterCacheTime()
    {
        // Regression: a double-Detach (or Detach racing an auto-eviction) used to overwrite the first
        // pending-removal subscription without disposing it, leaking a timer.
        var key = QueryKey.From("a");
        using var query = CreateQuery(key, TimeSpan.FromMinutes(5));
        _sut.GetOrCreate(key, query);

        _sut.Remove(key);
        _sut.Remove(key);

        _scheduler.AdvanceBy(TimeSpan.FromMinutes(5).Ticks + 1);

        using var fresh = CreateQuery(key);
        var result = _sut.GetOrCreate(key, fresh);

        await Assert.That(result).IsEqualTo(fresh);
    }

    [Test]
    public async Task Remove_NonExistentKey_DoesNothing()
    {
        var key = QueryKey.From("missing");
        _sut.Remove(key);

        // If no exception was thrown, adding a query afterwards should still work normally
        using var query = CreateQuery(key);
        var result = _sut.GetOrCreate(key, query);
        await Assert.That(result).IsEqualTo(query);
    }

    [Test]
    public async Task Remove_Before_sutTimeElapses_QueryStillReturned()
    {
        var key = QueryKey.From("a");
        using var query = CreateQuery(key, TimeSpan.FromMinutes(5));
        _sut.GetOrCreate(key, query);
        _sut.Remove(key);

        // Advance to just before expiry
        _scheduler.AdvanceBy(TimeSpan.FromMinutes(4).Ticks);

        using var other = CreateQuery(key);
        var result = _sut.GetOrCreate(key, other);

        await Assert.That(result).IsEqualTo(query);
    }

    [Test]
    public async Task Remove_After_sutTimeElapses_QueryIsEvicted()
    {
        var key = QueryKey.From("a");
        using var original = CreateQuery(key, TimeSpan.FromMinutes(5));
        _sut.GetOrCreate(key, original);
        _sut.Remove(key);

        _scheduler.AdvanceBy(TimeSpan.FromMinutes(5).Ticks + 1);

        // Original was evicted; a new query should now be stored
        using var fresh = CreateQuery(key);
        var result = _sut.GetOrCreate(key, fresh);

        await Assert.That(result).IsEqualTo(fresh);
    }

    [Test]
    public async Task LastSubscriberUnsubscribing_SchedulesEviction()
    {
        // Regression: previously, nothing started the eviction timer when the last State subscriber
        // left — only an explicit Detach() did — so cache entries accumulated forever.
        var key = QueryKey.From("a");
        using var query = CreateQuery(key, TimeSpan.FromMinutes(5));
        _sut.GetOrCreate(key, query);

        var subscription = query.State.Subscribe();
        subscription.Dispose(); // last (only) subscriber leaves

        _scheduler.AdvanceBy(TimeSpan.FromMinutes(5).Ticks + 1);

        // Query should have been evicted automatically — GetOrCreate for the same key returns a fresh instance
        using var fresh = CreateQuery(key);
        var result = _sut.GetOrCreate(key, fresh);

        await Assert.That(result).IsEqualTo(fresh);
    }

    [Test]
    public async Task SubscriberRejoiningBeforeEvictionTimerFires_CancelsEviction()
    {
        var key = QueryKey.From("a");
        using var query = CreateQuery(key, TimeSpan.FromMinutes(5));
        _sut.GetOrCreate(key, query);

        var subscription = query.State.Subscribe();
        subscription.Dispose();

        // Rejoin directly against the Query (not via GetOrCreate) before the CacheTime timer fires
        using var rejoin = query.State.Subscribe();

        _scheduler.AdvanceBy(TimeSpan.FromMinutes(10).Ticks);

        using var late = CreateQuery(key);
        var result = _sut.GetOrCreate(key, late);

        await Assert.That(result).IsEqualTo(query);
    }

    [Test]
    public async Task Invalidate_ExistingKey_TriggersRefetch()
    {
        var key = QueryKey.From("a");
        using var query = CreateQuery(key);
        _sut.GetOrCreate(key, query);

        // Subscribe before invalidating so the count is > 0 and we don't miss the Fetching state
        var tcs = new TaskCompletionSource<QueryState<string>>();
        using var sub = query.State.Where(s => s.IsFetching).Subscribe(s => tcs.TrySetResult(s));

        _sut.Invalidate(key);

        var state = await tcs.Task;
        await Assert.That(state.IsFetching).IsTrue();
    }

    [Test]
    public async Task Invalidate_NonExistentKey_DoesNothing()
    {
        var key = QueryKey.From("missing");
        _sut.Invalidate(key);

        // Subsequent GetOrCreate should still work normally
        using var query = CreateQuery(key);
        var result = _sut.GetOrCreate(key, query);
        await Assert.That(result).IsEqualTo(query);
    }

    [Test]
    public async Task Invalidate_Predicate_MatchingKeys_TriggerRefetch()
    {
        var keyA = QueryKey.From("todos", "A");
        var keyB = QueryKey.From("todos", "B");
        using var queryA = CreateQuery(keyA);
        using var queryB = CreateQuery(keyB);
        _sut.GetOrCreate(keyA, queryA);
        _sut.GetOrCreate(keyB, queryB);

        var tcsA = new TaskCompletionSource<QueryState<string>>();
        var tcsB = new TaskCompletionSource<QueryState<string>>();
        using var subA = queryA.State.Where(s => s.IsFetching).Subscribe(s => tcsA.TrySetResult(s));
        using var subB = queryB.State.Where(s => s.IsFetching).Subscribe(s => tcsB.TrySetResult(s));

        _sut.Invalidate(k => k.Parts.Contains("todos"));

        using var _ = Assert.Multiple();
        await Assert.That((await tcsA.Task).IsFetching).IsTrue();
        await Assert.That((await tcsB.Task).IsFetching).IsTrue();
    }

    [Test]
    public async Task Invalidate_Predicate_NonMatchingKeys_DoNotRefetch()
    {
        var key = QueryKey.From("users");
        using var query = CreateQuery(key);
        _sut.GetOrCreate(key, query);

        var fetchCount = 0;
        using var sub = query.State.Where(s => s.IsFetching).Subscribe(_ => fetchCount++);

        _sut.Invalidate(k => k.Parts.Contains("todos"));

        await Task.Delay(50);

        await Assert.That(fetchCount).IsEqualTo(0);
    }

    [Test]
    public async Task Dispose_CompletesQueryStateObservables()
    {
        var key = QueryKey.From("a");
        using var query = CreateQuery(key);
        _sut.GetOrCreate(key, query);

        var completed = false;
        using var _ = query.State.Subscribe(_ => { }, () => completed = true);

        _sut.Dispose();

        await Assert.That(completed).IsTrue();
    }

    [Test]
    public async Task Eviction_ReturnsCacheEntriesMetricToZero()
    {
        using var meter = new Meter($"DotNetQuery-CacheEntries-{Guid.NewGuid()}");
        var instrumentation = new QueryInstrumentation(NullLogger.Instance, meter);
        using var cache = new QueryCache(_scheduler, instrumentation);

        var entryDeltas = new List<int>();
        using var entriesListener = CreateMeterListener<int>(meter, "dotnetquery.cache.entries", entryDeltas.Add);

        var key = QueryKey.From("evict-metric");
        using var query = CreateQuery(key, TimeSpan.FromMinutes(5));
        cache.GetOrCreate(key, query);
        cache.Remove(key);

        _scheduler.AdvanceBy(TimeSpan.FromMinutes(5).Ticks + 1);

        await Assert.That(entryDeltas.Sum()).IsEqualTo(0);
    }

    [Test]
    public async Task CancelledPendingRemoval_DoesNotDecrementCacheEntriesOrRecordEviction()
    {
        using var meter = new Meter($"DotNetQuery-CacheEntries-{Guid.NewGuid()}");
        var instrumentation = new QueryInstrumentation(NullLogger.Instance, meter);
        using var cache = new QueryCache(_scheduler, instrumentation);

        var entryDeltas = new List<int>();
        var evictions = new List<long>();
        using var entriesListener = CreateMeterListener<int>(meter, "dotnetquery.cache.entries", entryDeltas.Add);
        using var evictionsListener = CreateMeterListener<long>(meter, "dotnetquery.cache.evictions", evictions.Add);

        var key = QueryKey.From("evict-cancel-metric");
        using var query = CreateQuery(key, TimeSpan.FromMinutes(5));
        cache.GetOrCreate(key, query);
        cache.Remove(key);

        using var rejoin = CreateQuery(key);
        cache.GetOrCreate(key, rejoin);

        _scheduler.AdvanceBy(TimeSpan.FromMinutes(10).Ticks);

        using var _ = Assert.Multiple();
        await Assert.That(entryDeltas.Sum()).IsEqualTo(1);
        await Assert.That(evictions).IsEmpty();
    }

    private static MeterListener CreateMeterListener<T>(Meter meter, string instrumentName, Action<T> onMeasurement)
        where T : struct
    {
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

        listener.SetMeasurementEventCallback<T>((_, measurement, _, _) => onMeasurement(measurement));
        listener.Start();

        return listener;
    }

    [Test]
    public async Task Dispose_WithPendingRemoval_DoesNotEvictQueryAfterDispose()
    {
        var key = QueryKey.From("a");
        using var query = CreateQuery(key, TimeSpan.FromMinutes(5));
        _sut.GetOrCreate(key, query);
        _sut.Remove(key);

        var completed = false;
        using var _ = query.State.Subscribe(_ => { }, () => completed = true);

        _sut.Dispose();

        // Timer fires after dispose — should be a no-op, not a second dispose
        _scheduler.AdvanceBy(TimeSpan.FromMinutes(10).Ticks);

        await Assert.That(completed).IsTrue();
    }
}
