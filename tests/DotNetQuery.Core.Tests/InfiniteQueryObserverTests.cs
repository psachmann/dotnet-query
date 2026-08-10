namespace DotNetQuery.Core.Tests;

public class InfiniteQueryObserverTests
{
    private readonly TestScheduler _scheduler = new();
    private QueryCache _cache = default!;

    private static readonly QueryInstrumentation _instrumentation = new(NullLogger.Instance);

    [Before(Test)]
    public void Setup() => _cache = new QueryCache(_scheduler, _instrumentation);

    [After(Test)]
    public void Teardown() => _cache.Dispose();

    private InfiniteQueryObserver<int, string, int> CreateObserver(
        Func<int, int, CancellationToken, Task<string>>? fetcher = null,
        Func<int, QueryKey>? keyFactory = null,
        Func<InfinitePageInfo<string, int>, PageParam<int>>? getPreviousPageParam = null,
        bool isEnabled = true,
        string? name = null
    )
    {
        var options = new InfiniteQueryOptions<int, string, int>
        {
            KeyFactory = keyFactory ?? (args => QueryKey.From("observer-test", args)),
            Fetcher = fetcher ?? ((_, page, _) => Task.FromResult(new string($"page{page}".ToCharArray()))),
            InitialPageParam = 0,
            GetNextPageParam = info => info.PageParam + 1,
            GetPreviousPageParam = getPreviousPageParam,
            IsEnabled = isEnabled,
            Name = name,
        };

        return new InfiniteQueryObserver<int, string, int>(
            options,
            new QueryClientOptions { StaleTime = TimeSpan.Zero },
            _cache,
            _scheduler,
            _instrumentation
        );
    }

    [Test]
    public async Task Success_RefetchWithIdenticalPages_DoesNotReEmit()
    {
        using var sut = CreateObserver();

        var emissions = new List<IReadOnlyList<string>>();
        using var sub = sut.Success.Subscribe(emissions.Add);

        sut.SetArgs(1);
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        var tcs = new TaskCompletionSource<InfiniteQueryState<string, int>>();
        using var secondSub = sut.State.Where(s => s.IsSuccess).Skip(1).Subscribe(s => tcs.TrySetResult(s));
        sut.Refetch();
        await tcs.Task;

        await Assert.That(emissions.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Success_AfterFetchNextPage_EmitsAgainWithAllPages()
    {
        using var sut = CreateObserver();

        var emissions = new List<IReadOnlyList<string>>();
        using var sub = sut.Success.Subscribe(emissions.Add);

        sut.SetArgs(1);
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        sut.FetchNextPage();
        await sut.State.Where(s => s.IsSuccess && s.Pages.Count == 2).FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(emissions.Count).IsEqualTo(2);
        await Assert.That(emissions[^1].Count).IsEqualTo(2);
    }

    [Test]
    public async Task Success_RefetchWithChangedPages_ReEmits()
    {
        var version = 0;
        using var sut = CreateObserver(fetcher: (_, page, _) => Task.FromResult($"page{page}-v{version}"));

        var emissions = new List<IReadOnlyList<string>>();
        using var sub = sut.Success.Subscribe(emissions.Add);

        sut.SetArgs(1);
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        version++;
        var tcs = new TaskCompletionSource<InfiniteQueryState<string, int>>();
        using var secondSub = sut.State.Where(s => s.IsSuccess).Skip(1).Subscribe(s => tcs.TrySetResult(s));
        sut.Refetch();
        await tcs.Task;

        using var _ = Assert.Multiple();
        await Assert.That(emissions.Count).IsEqualTo(2);
        await Assert.That(emissions[^1][0]).IsEqualTo("page0-v1");
    }

    [Test]
    public async Task Key_ReflectsKeyDerivedFromLatestArgs()
    {
        using var sut = CreateObserver();

        await Assert.That(sut.Key).IsEqualTo(QueryKey.Default);

        sut.SetArgs(7);

        await Assert.That(sut.Key).IsEqualTo(QueryKey.From("observer-test", 7));
    }

    [Test]
    public async Task CacheTime_ReturnsConfiguredValue()
    {
        var options = new InfiniteQueryOptions<int, string, int>
        {
            KeyFactory = _ => QueryKey.From("observer-cachetime"),
            Fetcher = (_, page, _) => Task.FromResult($"page{page}"),
            InitialPageParam = 0,
            GetNextPageParam = info => info.PageParam + 1,
            CacheTime = TimeSpan.FromMinutes(42),
        };
        using var sut = new InfiniteQueryObserver<int, string, int>(
            options,
            new QueryClientOptions { StaleTime = TimeSpan.Zero },
            _cache,
            _scheduler,
            _instrumentation
        );

        await Assert.That(sut.CacheTime).IsEqualTo(TimeSpan.FromMinutes(42));
    }

    [Test]
    public async Task Status_And_CurrentData_And_LastUpdatedAt_ReflectActiveQuery()
    {
        using var sut = CreateObserver();

        using var _ = Assert.Multiple();
        await Assert.That(sut.Status).IsEqualTo(QueryStatus.Idle);
        await Assert.That(sut.CurrentData).IsNull();
        await Assert.That(sut.LastUpdatedAt).IsNull();

        using var sub = sut.State.Subscribe();
        sut.SetArgs(1);
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        await Assert.That(sut.Status).IsEqualTo(QueryStatus.Success);
        await Assert.That(((IReadOnlyList<string>)sut.CurrentData!).Count).IsEqualTo(1);
        await Assert.That(sut.LastUpdatedAt).IsNotNull();
    }

    [Test]
    public async Task ObserverCount_TracksActiveStateSubscribers()
    {
        using var sut = CreateObserver();
        sut.SetArgs(1);

        await Assert.That(sut.ObserverCount).IsEqualTo(0);

        var sub = sut.State.Subscribe();
        await Assert.That(sut.ObserverCount).IsEqualTo(1);

        sub.Dispose();
        await Assert.That(sut.ObserverCount).IsEqualTo(0);
    }

    [Test]
    public async Task MetricName_FallsBackToOptionsName_BeforeArgsSet()
    {
        using var sut = CreateObserver(name: "custom-name");

        await Assert.That(sut.MetricName).IsEqualTo("custom-name");
    }

    [Test]
    public async Task MetricName_FallsBackToUnknown_WhenNoNameAndNoActiveQuery()
    {
        using var sut = CreateObserver();

        await Assert.That(sut.MetricName).IsEqualTo("unknown");
    }

    [Test]
    public async Task StateChanged_EmitsOnEachStateTransition()
    {
        using var sut = CreateObserver();

        var count = 0;
        using var sub = sut.StateChanged.Subscribe(_ => count++);

        using var stateSub = sut.State.Subscribe();
        sut.SetArgs(1);
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        await Assert.That(count).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task SetEnabled_False_SuppressesFetch_ThenTrue_Fetches()
    {
        var fetchCount = 0;
        using var sut = CreateObserver(
            fetcher: (_, page, _) =>
            {
                fetchCount++;
                return Task.FromResult($"page{page}");
            },
            isEnabled: false
        );

        using var sub = sut.State.Subscribe();
        sut.SetArgs(1);
        await Task.Delay(50);

        await Assert.That(fetchCount).IsEqualTo(0);

        sut.SetEnabled(true);
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        await Assert.That(fetchCount).IsEqualTo(1);
    }

    [Test]
    public async Task FetchPreviousPage_ForwardsToActiveQuery()
    {
        using var sut = CreateObserver(
            fetcher: (_, page, _) => Task.FromResult($"page{page}"),
            getPreviousPageParam: info => PageParam<int>.Some(info.PageParam - 1)
        );

        using var sub = sut.State.Subscribe();
        sut.SetArgs(1);
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        sut.FetchNextPage();
        await sut.State.Where(s => s.IsSuccess && s.Pages.Count == 2).FirstAsync();

        sut.FetchPreviousPage();
        var state = await sut.State.Where(s => s.IsSuccess && s.Pages.Count == 3).FirstAsync();

        await Assert.That(state.Pages[0]).IsEqualTo("page-1");
    }

    [Test]
    public async Task Failure_EmitsExceptionOnFailedFetch()
    {
        using var sut = CreateObserver(
            fetcher: (_, _, _) => Task.FromException<string>(new InvalidOperationException("boom"))
        );

        var tcs = new TaskCompletionSource<Exception>();
        using var sub = sut.Failure.Subscribe(ex => tcs.TrySetResult(ex));

        using var stateSub = sut.State.Subscribe();
        sut.SetArgs(1);

        var error = await tcs.Task;
        await Assert.That(error.Message).IsEqualTo("boom");
    }

    [Test]
    public async Task Cancel_BeforeAnyArgsSet_DoesNotThrow()
    {
        using var sut = CreateObserver();

        await Assert.That(sut.Cancel).ThrowsNothing();
    }

    [Test]
    public async Task Cancel_DuringFetch_LeavesQueryFetchable()
    {
        using var sut = CreateObserver();

        using var sub = sut.State.Subscribe();
        sut.SetArgs(1);
        sut.Cancel();

        sut.Refetch();
        var state = await sut.State.Where(s => s.IsSuccess).FirstAsync();

        await Assert.That(state.Pages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Invalidate_ForwardsToActiveQuery()
    {
        var fetchCount = 0;
        using var sut = CreateObserver(
            fetcher: (_, page, _) =>
            {
                fetchCount++;
                return Task.FromResult($"page{page}");
            }
        );

        using var sub = sut.State.Subscribe();
        sut.SetArgs(1);
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        sut.Invalidate();
        await Task.Delay(50);

        // StaleTime is zero, so Invalidate triggers an immediate re-fetch.
        await Assert.That(fetchCount).IsEqualTo(2);
    }

    [Test]
    public async Task Detach_RemovesEntryFromCache_SoNextSubscriberIsFreshCacheMiss()
    {
        var key = QueryKey.From("observer-detach");
        using var sut = CreateObserver(keyFactory: _ => key);

        using var sub = sut.State.Subscribe();
        sut.SetArgs(1);
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        sut.Detach();
        _scheduler.AdvanceBy(TimeSpan.FromMinutes(5).Ticks + 1);

        using var rejoin = CreateObserver(keyFactory: _ => key);
        using var rejoinSub = rejoin.State.Subscribe();
        rejoin.SetArgs(1);
        var state = await rejoin.State.Where(s => s.IsSuccess).FirstAsync();

        await Assert.That(state.Pages).IsEquivalentTo(["page0"]);
    }

    [Test]
    public async Task Dispose_CalledTwice_IsIdempotent()
    {
        var sut = CreateObserver();
        sut.SetArgs(1);

        sut.Dispose();

        await Assert.That(sut.Dispose).ThrowsNothing();
    }

    [Test]
    public async Task SetArgs_SameKeyTwice_DisposesRejectedCandidate()
    {
        var key = QueryKey.From("observer-cache-hit");
        using var sut = CreateObserver(keyFactory: _ => key);

        using var sub = sut.State.Subscribe();
        sut.SetArgs(1);
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        // Second SetArgs derives the same key — GetOrCreate returns the existing entry, and the
        // freshly constructed candidate InfiniteQuery is discarded via candidate.Dispose().
        await Assert.That(() => sut.SetArgs(1)).ThrowsNothing();
    }
}
