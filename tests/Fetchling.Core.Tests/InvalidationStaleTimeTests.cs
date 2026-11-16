using System.Reactive.Threading.Tasks;

namespace Fetchling.Core.Tests;

/// <summary>
/// <see cref="IQueryClient.Invalidate(QueryKey)"/> marks data stale regardless of <c>StaleTime</c>;
/// <see cref="IQueryClient.InvalidateStale"/> (what <c>&lt;QueryRefreshMonitor&gt;</c> calls) respects it.
/// </summary>
public class InvalidationStaleTimeTests
{
    private static readonly TimeSpan StaleTime = TimeSpan.FromMinutes(5);
    private static readonly QueryInstrumentation _instrumentation = new(NullLogger.Instance);

    private readonly TestScheduler _scheduler = new();
    private QueryClient _client = default!;
    private int _fetches;

    [Before(Test)]
    public void Setup() => _client = new(new(), _scheduler, _instrumentation);

    [After(Test)]
    public void Teardown() => _client.Dispose();

    private static QueryKey Todo(int id) => QueryKey.From("todos", id);

    private IQuery<int, string> CreateQuery(TimeSpan? refetchInterval = null) =>
        _client.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = Todo,
                Fetcher = (_, _) => Task.FromResult($"v{Interlocked.Increment(ref _fetches)}"),
                StaleTime = StaleTime,
                RefetchInterval = refetchInterval,
            }
        );

    private int Fetches => Volatile.Read(ref _fetches);

    private static Task<QueryState<string>> WaitForData(IQuery<int, string> query, string data) =>
        query.State.Where(s => s.IsSuccess && s.CurrentData == data).FirstAsync().ToTask();

    /// <summary>An observed query whose first fetch has completed, so its data is fresh.</summary>
    private async Task<(IQuery<int, string> Query, IDisposable Subscription)> FreshQuery(
        TimeSpan? refetchInterval = null
    )
    {
        var query = CreateQuery(refetchInterval);
        var subscription = query.State.Subscribe(_ => { });
        query.SetArgs(1);
        await WaitForData(query, "v1");

        return (query, subscription);
    }

    [Test]
    public async Task Invalidate_RefetchesAnObservedQuery_ThatIsStillFresh()
    {
        var (query, subscription) = await FreshQuery();
        using var _q = query;
        using var _s = subscription;

        _client.Invalidate(Todo(1));
        await WaitForData(query, "v2");

        await Assert.That(Fetches).IsEqualTo(2);
    }

    [Test]
    public async Task InvalidatePredicate_RefetchesAnObservedQuery_ThatIsStillFresh()
    {
        var (query, subscription) = await FreshQuery();
        using var _q = query;
        using var _s = subscription;

        _client.Invalidate(key => key.Parts is ["todos", ..]);
        await WaitForData(query, "v2");

        await Assert.That(Fetches).IsEqualTo(2);
    }

    [Test]
    public async Task InvalidateStale_LeavesAFreshQueryAlone()
    {
        var (query, subscription) = await FreshQuery();
        using var _q = query;
        using var _s = subscription;

        _client.InvalidateStale(_ => true);
        await Task.Delay(50);

        await Assert.That(Fetches).IsEqualTo(1);
    }

    [Test]
    public async Task InvalidateStale_RefetchesAQueryOlderThanItsStaleTime()
    {
        var (query, subscription) = await FreshQuery();
        using var _q = query;
        using var _s = subscription;
        _scheduler.AdvanceBy(StaleTime.Ticks);

        _client.InvalidateStale(_ => true);
        await WaitForData(query, "v2");

        await Assert.That(Fetches).IsEqualTo(2);
    }

    [Test]
    public async Task InvalidateStale_ThrowsForANullPredicate()
    {
        await Assert.That(() => _client.InvalidateStale(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Invalidate_OfAFreshUnobservedQuery_IsFetchedByTheNextObserver()
    {
        var (query, subscription) = await FreshQuery();
        subscription.Dispose();
        query.Dispose();

        _client.Invalidate(Todo(1));
        var entry = (await Entries()).Single();
        var staleWhileUnobserved = entry.IsStale;
        var fetchesWhileUnobserved = Fetches;

        using var next = CreateQuery();
        using var nextSubscription = next.State.Subscribe(_ => { });
        next.SetArgs(1);
        await WaitForData(next, "v2");

        using var _ = Assert.Multiple();
        await Assert.That(staleWhileUnobserved).IsTrue();
        await Assert.That(fetchesWhileUnobserved).IsEqualTo(1);
        await Assert.That(Fetches).IsEqualTo(2);
    }

    [Test]
    public async Task Invalidate_OfAFreshUnobservedQuery_IsNotSkippedByAPrefetch()
    {
        var (query, subscription) = await FreshQuery();
        subscription.Dispose();

        _client.Invalidate(Todo(1));
        await query.PrefetchAsync(1);

        await Assert.That(Fetches).IsEqualTo(2);
        query.Dispose();
    }

    [Test]
    public async Task CacheEntryInvalidate_RefetchesAFreshQuery()
    {
        var (query, subscription) = await FreshQuery();
        using var _q = query;
        using var _s = subscription;

        (await Entries()).Single().Invalidate();
        await WaitForData(query, "v2");

        await Assert.That(Fetches).IsEqualTo(2);
    }

    [Test]
    public async Task MutationInvalidateKeys_RefetchAFreshQuery()
    {
        var (query, subscription) = await FreshQuery();
        using var _q = query;
        using var _s = subscription;
        using var mutation = _client.CreateMutation(
            new MutationOptions<string, string>
            {
                Mutator = (title, _) => Task.FromResult(title),
                InvalidateKeys = [Todo(1)],
            }
        );

        mutation.Execute("renamed");
        await WaitForData(query, "v2");

        await Assert.That(Fetches).IsEqualTo(2);
    }

    [Test]
    public async Task RefetchInterval_StillRespectsStaleTime()
    {
        var (query, subscription) = await FreshQuery(refetchInterval: TimeSpan.FromMinutes(1));
        using var _q = query;
        using var _s = subscription;

        _scheduler.AdvanceBy(TimeSpan.FromMinutes(4).Ticks); // four ticks, all within StaleTime
        await Task.Delay(50);
        var fetchesWhileFresh = Fetches;
        _scheduler.AdvanceBy(TimeSpan.FromMinutes(1).Ticks); // the tick at StaleTime
        await WaitForData(query, "v2");

        await Assert.That(fetchesWhileFresh).IsEqualTo(1);
    }

    [Test]
    public async Task Invalidate_RefetchesAFreshInfiniteQuery()
    {
        var fetches = 0;
        using var query = _client.CreateInfiniteQuery(
            new InfiniteQueryOptions<int, string, int>
            {
                KeyFactory = Todo,
                Fetcher = (_, page, _) => Task.FromResult($"page{page}.{Interlocked.Increment(ref fetches)}"),
                InitialPageParam = 0,
                GetNextPageParam = _ => PageParam<int>.None,
                StaleTime = StaleTime,
            }
        );
        using var subscription = query.State.Subscribe(_ => { });
        query.SetArgs(1);
        await query.State.Where(s => s.IsSuccess && s.Pages is [_]).FirstAsync();

        _client.Invalidate(Todo(1));
        await query.State.Where(s => s.IsSuccess && s.Pages is ["page0.2"]).FirstAsync();

        await Assert.That(Volatile.Read(ref fetches)).IsEqualTo(2);
    }

    private Task<IReadOnlyList<IQueryCacheEntry>> Entries()
    {
        _client.TryGetInspector(out var inspector);

        return inspector!.Entries.FirstAsync().ToTask();
    }
}
