namespace DotNetQuery.Core.Tests;

public class QueryObserverTests
{
    private readonly TestScheduler _scheduler = new();
    private QueryCache _cache = default!;

    [Before(Test)]
    public void Setup() => _cache = new QueryCache(_scheduler);

    [After(Test)]
    public void Teardown() => _cache.Dispose();

    private QueryObserver<int, string> CreateObserver(
        Func<int, QueryKey>? keyFactory = null,
        Func<int, CancellationToken, Task<string>>? fetcher = null,
        bool isEnabled = true,
        TimeSpan? staleTime = null,
        TimeSpan? cacheTime = null,
        QueryClientOptions? globalOptions = null
    )
    {
        var options = new QueryOptions<int, string>
        {
            KeyFactory = keyFactory ?? (_ => QueryKey.From("default")),
            Fetcher = fetcher ?? ((args, _) => Task.FromResult(args.ToString())),
            IsEnabled = isEnabled,
            StaleTime = staleTime,
            CacheTime = cacheTime,
        };
        return new QueryObserver<int, string>(
            options,
            globalOptions ?? new QueryClientOptions { StaleTime = TimeSpan.Zero },
            _cache,
            _scheduler
        );
    }

    [Test]
    public async Task InitialKey_IsDefault()
    {
        using var sut = CreateObserver();

        await Assert.That(sut.Key).IsEqualTo(QueryKey.Default);
    }

    [Test]
    public async Task InitialCurrentState_IsIdle()
    {
        using var sut = CreateObserver();

        await Assert.That(sut.CurrentState.IsIdle).IsTrue();
    }

    [Test]
    public async Task Args_PushingArgs_UpdatesKey()
    {
        using var sut = CreateObserver(keyFactory: id => QueryKey.From("item", id));

        using var _ = sut.State.Subscribe();
        sut.SetArgs(42);

        await Assert.That(sut.Key).IsEqualTo(QueryKey.From("item", 42));
    }

    [Test]
    public async Task Args_PushingArgs_FetchesData()
    {
        using var sut = CreateObserver(fetcher: (_, _) => Task.FromResult("hello"));

        using var _ = sut.State.Subscribe();
        sut.SetArgs(0);

        var state = await sut.State.Where(s => s.IsSuccess).FirstAsync();
        await Assert.That(state.CurrentData).IsEqualTo("hello");
    }

    [Test]
    public async Task Args_PushingDifferentKeys_SwitchesToNewQueryState()
    {
        using var sut = CreateObserver(
            keyFactory: id => QueryKey.From("item", id),
            fetcher: (id, _) => Task.FromResult(id.ToString())
        );

        using var _ = sut.State.Subscribe();

        sut.SetArgs(1);
        await sut.State.Where(s => s.IsSuccess && s.CurrentData == "1").FirstAsync();

        sut.SetArgs(2);
        var state = await sut.State.Where(s => s.IsSuccess && s.CurrentData == "2").FirstAsync();

        await Assert.That(state.CurrentData).IsEqualTo("2");
    }

    [Test]
    public async Task Args_SameKey_ReturnsCachedQuery()
    {
        var fetchCount = 0;
        var sutA = CreateObserver(
            fetcher: (_, _) =>
            {
                fetchCount++;
                return Task.FromResult("data");
            },
            staleTime: TimeSpan.FromMinutes(5)
        );
        var sutB = CreateObserver(
            fetcher: (_, _) =>
            {
                fetchCount++;
                return Task.FromResult("data");
            },
            staleTime: TimeSpan.FromMinutes(5)
        );

        using var subA = sutA.State.Subscribe();
        sutA.SetArgs(0);
        await sutA.State.Where(s => s.IsSuccess).FirstAsync();

        // sutB uses the same cache key; StaleTime not elapsed so no second fetch
        using var subB = sutB.State.Subscribe();
        sutB.SetArgs(0);
        await Task.Delay(50);

        await Assert.That(fetchCount).IsEqualTo(1);

        sutA.Dispose();
        sutB.Dispose();
    }

    [Test]
    public async Task Args_CurrentState_ReflectsActiveQuery()
    {
        using var sut = CreateObserver(fetcher: (_, _) => Task.FromResult("result"));

        using var _ = sut.State.Subscribe();
        sut.SetArgs(0);
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        await Assert.That(sut.CurrentState.IsSuccess).IsTrue();
    }

    [Test]
    public async Task IsEnabled_FalseOnCreation_DoesNotFetchWhenArgsPushed()
    {
        var fetched = false;
        using var sut = CreateObserver(
            fetcher: (_, _) =>
            {
                fetched = true;
                return Task.FromResult("data");
            },
            isEnabled: false
        );

        using var _ = sut.State.Subscribe();
        sut.SetArgs(0);
        await Task.Delay(50);

        await Assert.That(fetched).IsFalse();
    }

    [Test]
    public async Task IsEnabled_FalseOnCreation_StateRemainsIdle()
    {
        using var sut = CreateObserver(isEnabled: false);

        using var _ = sut.State.Subscribe();
        sut.SetArgs(0);
        await Task.Delay(50);

        await Assert.That(sut.CurrentState.IsIdle).IsTrue();
    }

    [Test]
    public async Task IsEnabled_ReenablingAfterArgs_TriggersRefetch()
    {
        using var sut = CreateObserver(isEnabled: false);

        using var sub = sut.State.Subscribe();
        sut.SetArgs(0);
        await Task.Delay(50);

        var tcs = new TaskCompletionSource<QueryState<string>>();
        using var fetchSub = sut.State.Where(s => s.IsFetching).Subscribe(s => tcs.TrySetResult(s));

        sut.SetEnabled(true);

        var state = await tcs.Task;
        await Assert.That(state.IsFetching).IsTrue();
    }

    [Test]
    public async Task IsEnabled_TogglingFalseAgain_DoesNotFireOnDistinctUntilChanged()
    {
        var fetchCount = 0;
        using var sut = CreateObserver(
            fetcher: (_, _) =>
            {
                fetchCount++;
                return Task.FromResult("data");
            }
        );

        using var _ = sut.State.Subscribe();
        sut.SetArgs(0);
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        var countAfterFirstFetch = fetchCount;

        // Setting false then false again — DistinctUntilChanged filters the duplicate
        sut.SetEnabled(false);
        sut.SetEnabled(false);
        await Task.Delay(50);

        await Assert.That(fetchCount).IsEqualTo(countAfterFirstFetch);
    }

    [Test]
    public async Task Refetch_WithActiveQuery_TriggersAnotherFetch()
    {
        using var sut = CreateObserver();

        using var sub = sut.State.Subscribe();
        sut.SetArgs(0);
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        var tcs = new TaskCompletionSource<QueryState<string>>();
        using var fetchSub = sut.State.Where(s => s.IsFetching).Subscribe(s => tcs.TrySetResult(s));

        sut.Refetch();

        var state = await tcs.Task;
        await Assert.That(state.IsFetching).IsTrue();
    }

    [Test]
    public async Task Refetch_WithNoActiveQuery_DoesNotThrow()
    {
        using var sut = CreateObserver();

        sut.Refetch(); // no args pushed yet, _activeQuery.Value is null

        await Assert.That(sut.CurrentState.IsIdle).IsTrue();
    }

    [Test]
    public async Task Cancel_WhileFetching_ReturnsToIdle()
    {
        var gate = new TaskCompletionSource();
        using var sut = CreateObserver(
            fetcher: async (_, ct) =>
            {
                await gate.Task.WaitAsync(ct);
                return "data";
            }
        );

        using var sub = sut.State.Subscribe();
        sut.SetArgs(0);
        await sut.State.Where(s => s.IsFetching).FirstAsync();

        sut.Cancel();

        var state = await sut.State.Where(s => s.IsIdle).FirstAsync();
        await Assert.That(state.IsIdle).IsTrue();
    }

    [Test]
    public async Task Invalidate_WithActiveQuery_TriggersRefetch()
    {
        using var sut = CreateObserver();

        using var sub = sut.State.Subscribe();
        sut.SetArgs(0);
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        var tcs = new TaskCompletionSource<QueryState<string>>();
        using var fetchSub = sut.State.Where(s => s.IsFetching).Subscribe(s => tcs.TrySetResult(s));

        sut.Invalidate();

        var state = await tcs.Task;
        await Assert.That(state.IsFetching).IsTrue();
    }

    [Test]
    public async Task Invalidate_WithNoActiveQuery_DoesNotThrow()
    {
        using var sut = CreateObserver();

        sut.Invalidate();

        await Assert.That(sut.CurrentState.IsIdle).IsTrue();
    }

    [Test]
    public async Task Detach_SchedulesRemovalOfCurrentKey()
    {
        var fetchCount = 0;
        var sutA = CreateObserver(
            fetcher: (_, _) =>
            {
                fetchCount++;
                return Task.FromResult("data");
            },
            cacheTime: TimeSpan.FromMinutes(5)
        );

        using var sub = sutA.State.Subscribe();
        sutA.SetArgs(0);
        await sutA.State.Where(s => s.IsSuccess).FirstAsync();

        sutA.Detach();

        // Advance past cache time — the detached entry should be evicted
        _scheduler.AdvanceBy(TimeSpan.FromMinutes(5).Ticks + 1);

        // A new observer for the same key now fetches fresh
        var sutB = CreateObserver(
            fetcher: (_, _) =>
            {
                fetchCount++;
                return Task.FromResult("data");
            },
            cacheTime: TimeSpan.FromMinutes(5)
        );

        using var subB = sutB.State.Subscribe();
        sutB.SetArgs(0);
        await sutB.State.Where(s => s.IsSuccess).FirstAsync();

        await Assert.That(fetchCount).IsEqualTo(2);

        sutA.Dispose();
        sutB.Dispose();
    }

    [Test]
    public async Task Success_EmitsUnwrappedData()
    {
        using var sut = CreateObserver(fetcher: (_, _) => Task.FromResult("result"));

        using var _ = sut.State.Subscribe();
        sut.SetArgs(0);

        var data = await sut.Success.FirstAsync();
        await Assert.That(data).IsEqualTo("result");
    }

    [Test]
    public async Task Failure_EmitsException()
    {
        var error = new Exception("boom");
        using var sut = CreateObserver(
            fetcher: (_, _) => Task.FromException<string>(error),
            globalOptions: new QueryClientOptions
            {
                StaleTime = TimeSpan.Zero,
                RetryHandler = new DefaultRetryHandler([]),
            }
        );

        using var _ = sut.State.Subscribe();
        sut.SetArgs(0);

        var emitted = await sut.Failure.FirstAsync();
        await Assert.That(emitted).IsEqualTo(error);
    }

    [Test]
    public async Task Settled_EmitsOnSuccess()
    {
        using var sut = CreateObserver();

        using var _ = sut.State.Subscribe();
        sut.SetArgs(0);

        var state = await sut.Settled.FirstAsync();
        await Assert.That(state.IsSuccess).IsTrue();
    }

    [Test]
    public async Task MergeOptions_LocalStaleTimeOverridesGlobal()
    {
        var fetchCount = 0;
        using var sut = CreateObserver(
            fetcher: (_, _) =>
            {
                fetchCount++;
                return Task.FromResult("data");
            },
            staleTime: TimeSpan.FromMinutes(10),
            globalOptions: new QueryClientOptions { StaleTime = TimeSpan.Zero }
        );

        using var sub = sut.State.Subscribe();
        sut.SetArgs(0);
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        // StaleTime not yet elapsed — Invalidate should be skipped
        sut.Invalidate();
        await Task.Delay(50);

        await Assert.That(fetchCount).IsEqualTo(1);
    }

    [Test]
    public async Task MergeOptions_GlobalStaleTimeUsedWhenLocalIsNull()
    {
        var fetchCount = 0;
        using var sut = CreateObserver(
            fetcher: (_, _) =>
            {
                fetchCount++;
                return Task.FromResult("data");
            },
            staleTime: null,
            globalOptions: new QueryClientOptions { StaleTime = TimeSpan.FromMinutes(10) }
        );

        using var sub = sut.State.Subscribe();
        sut.SetArgs(0);
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        // Global StaleTime not yet elapsed — Invalidate should be skipped
        sut.Invalidate();
        await Task.Delay(50);

        await Assert.That(fetchCount).IsEqualTo(1);
    }

    [Test]
    public async Task Dispose_IsIdempotent()
    {
        var sut = CreateObserver();

        sut.Dispose();
        sut.Dispose(); // should not throw

        await Assert.That(sut.Key).IsEqualTo(QueryKey.Default);
    }
}
