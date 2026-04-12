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
