namespace DotNetQuery.Core.Tests;

public class InfiniteQueryTests
{
    private readonly TestScheduler _scheduler = new();

    private static readonly QueryInstrumentation _instrumentation = new(NullLogger.Instance);

    private InfiniteQuery<int, string, int> CreateQuery(
        Func<int, int, CancellationToken, Task<string>>? fetcher = null,
        Func<InfinitePageInfo<string, int>, PageParam<int>>? getNextPageParam = null,
        Func<InfinitePageInfo<string, int>, PageParam<int>>? getPreviousPageParam = null,
        int initialPageParam = 0,
        int? maxPages = null,
        TimeSpan? staleTime = null,
        TimeSpan? cacheTime = null,
        TimeSpan? refetchInterval = null,
        int args = 0,
        string? initialData = null,
        string? name = null
    )
    {
        var options = new EffectiveInfiniteQueryOptions<int, string, int>
        {
            Fetcher = fetcher ?? ((_, page, _) => Task.FromResult($"page{page}")),
            InitialPageParam = initialPageParam,
            GetNextPageParam = getNextPageParam ?? (info => info.PageParam + 1),
            GetPreviousPageParam = getPreviousPageParam,
            MaxPages = maxPages,
            StaleTime = staleTime ?? TimeSpan.Zero,
            CacheTime = cacheTime ?? TimeSpan.FromMinutes(5),
            RefetchInterval = refetchInterval,
            RetryHandler = new DefaultRetryHandler(),
            IsEnabled = true,
            DataComparer = EqualityComparer<string>.Default,
            InitialData = initialData,
            Name = name,
        };

        return new InfiniteQuery<int, string, int>(QueryKey.From("test"), args, options, _scheduler, _instrumentation);
    }

    [Test]
    public async Task InitialState_IsIdle()
    {
        using var sut = CreateQuery();

        using var _ = Assert.Multiple();
        await Assert.That(sut.CurrentState.IsIdle).IsTrue();
        await Assert.That(sut.CurrentState.Pages.Count).IsEqualTo(0);
        await Assert.That(sut.CurrentState.HasNextPage).IsFalse();
    }

    [Test]
    public async Task Refetch_TransitionsToFetching()
    {
        using var sut = CreateQuery();

        var tcs = new TaskCompletionSource<InfiniteQueryState<string, int>>();
        using var sub = sut.State.Where(s => s.IsFetching).Subscribe(s => tcs.TrySetResult(s));

        sut.Refetch();

        var state = await tcs.Task;
        await Assert.That(state.IsFetching).IsTrue();
    }

    [Test]
    public async Task Refetch_FetchesFirstPage()
    {
        using var sut = CreateQuery(fetcher: (_, page, _) => Task.FromResult($"data:{page}"), initialPageParam: 1);

        using var sub = sut.State.Subscribe();
        sut.Refetch();

        var state = await sut.State.Where(s => s.IsSuccess).FirstAsync();
        using var _ = Assert.Multiple();
        await Assert.That(state.Pages.Count).IsEqualTo(1);
        await Assert.That(state.Pages[0]).IsEqualTo("data:1");
        await Assert.That(state.PageParams[0]).IsEqualTo(1);
    }

    [Test]
    public async Task FetchNextPage_AppendsPage()
    {
        using var sut = CreateQuery(
            fetcher: (_, page, _) => Task.FromResult($"page{page}"),
            getNextPageParam: info => info.PageParam + 1,
            initialPageParam: 0
        );

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        sut.FetchNextPage();
        var state = await sut.State.Where(s => s.IsSuccess && s.Pages.Count == 2).FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(state.Pages[0]).IsEqualTo("page0");
        await Assert.That(state.Pages[1]).IsEqualTo("page1");
        await Assert.That(state.PageParams[1]).IsEqualTo(1);
    }

    [Test]
    public async Task FetchNextPage_HasNextPage_ReflectsGetNextPageParam()
    {
        using var sut = CreateQuery(getNextPageParam: info =>
            info.PageParam < 2 ? PageParam<int>.Some(info.PageParam + 1) : PageParam<int>.None
        );

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        sut.FetchNextPage();
        await sut.State.Where(s => s.IsSuccess && s.Pages.Count == 2).FirstAsync();

        sut.FetchNextPage();
        var state = await sut.State.Where(s => s.IsSuccess && s.Pages.Count == 3).FirstAsync();

        await Assert.That(state.HasNextPage).IsFalse();
    }

    [Test]
    public async Task FetchNextPage_WhenGetNextPageParamReturnsNone_IsNoOp()
    {
        using var sut = CreateQuery(getNextPageParam: _ => PageParam<int>.None);

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        var successState = await sut.State.Where(s => s.IsSuccess).FirstAsync();

        sut.FetchNextPage();
        await Task.Delay(50); // give time for any state change

        var current = sut.CurrentState;
        await Assert.That(current.Pages.Count).IsEqualTo(successState.Pages.Count);
    }

    [Test]
    public async Task FetchNextPage_WhenNoPages_IsNoOp()
    {
        using var sut = CreateQuery();

        var stateCount = 0;
        using var sub = sut.State.Subscribe(_ => stateCount++);
        var countBefore = stateCount;

        sut.FetchNextPage();
        await Task.Delay(50);

        await Assert.That(stateCount).IsEqualTo(countBefore);
    }

    [Test]
    public async Task FetchNextPage_EmitsFetchingNextState()
    {
        var fetchTcs = new TaskCompletionSource<string>();

        using var sut = CreateQuery(
            fetcher: async (_, page, ct) =>
            {
                if (page == 1)
                {
                    return await fetchTcs.Task.WaitAsync(ct);
                }

                return $"page{page}";
            }
        );

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        sut.FetchNextPage();
        var fetchingState = await sut.State.Where(s => s.IsFetchingNextPage).FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(fetchingState.IsSuccess).IsTrue();
        await Assert.That(fetchingState.IsFetchingNextPage).IsTrue();
        await Assert.That(fetchingState.Pages.Count).IsEqualTo(1);

        fetchTcs.SetResult("page1");
    }

    [Test]
    public async Task FetchPreviousPage_PrependPage()
    {
        using var sut = CreateQuery(
            fetcher: (_, page, _) => Task.FromResult($"page{page}"),
            getNextPageParam: _ => PageParam<int>.None,
            getPreviousPageParam: info =>
                info.PageParam > 0 ? PageParam<int>.Some(info.PageParam - 1) : PageParam<int>.None,
            initialPageParam: 5
        );

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        sut.FetchPreviousPage();
        var state = await sut.State.Where(s => s.IsSuccess && s.Pages.Count == 2).FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(state.Pages[0]).IsEqualTo("page4");
        await Assert.That(state.Pages[1]).IsEqualTo("page5");
        await Assert.That(state.PageParams[0]).IsEqualTo(4);
    }

    [Test]
    public async Task FetchPreviousPage_WhenNoPreviousPageParamConfigured_IsNoOp()
    {
        using var sut = CreateQuery(getPreviousPageParam: null);

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        sut.FetchPreviousPage();
        await Task.Delay(50);

        await Assert.That(sut.CurrentState.Pages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task MaxPages_FetchNext_TrimsOldestPage()
    {
        using var sut = CreateQuery(getNextPageParam: info => info.PageParam + 1, maxPages: 2);

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        sut.FetchNextPage();
        await sut.State.Where(s => s.IsSuccess && s.Pages.Count == 2).FirstAsync();

        sut.FetchNextPage();
        var state = await sut.State.Where(s => s.IsSuccess && s.Pages[0] == "page1").FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(state.Pages.Count).IsEqualTo(2);
        await Assert.That(state.Pages[0]).IsEqualTo("page1");
        await Assert.That(state.Pages[1]).IsEqualTo("page2");
    }

    [Test]
    public async Task MaxPages_FetchPrevious_TrimsNewestPage()
    {
        using var sut = CreateQuery(
            fetcher: (_, page, _) => Task.FromResult($"page{page}"),
            getNextPageParam: _ => PageParam<int>.None,
            getPreviousPageParam: info =>
                info.PageParam > 0 ? PageParam<int>.Some(info.PageParam - 1) : PageParam<int>.None,
            initialPageParam: 5,
            maxPages: 2
        );

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        sut.FetchPreviousPage();
        await sut.State.Where(s => s.IsSuccess && s.Pages.Count == 2).FirstAsync();

        sut.FetchPreviousPage();
        var state = await sut.State.Where(s => s.IsSuccess && s.Pages[0] == "page3").FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(state.Pages.Count).IsEqualTo(2);
        await Assert.That(state.Pages[0]).IsEqualTo("page3");
        await Assert.That(state.Pages[1]).IsEqualTo("page4");
    }

    [Test]
    public async Task Refetch_AfterLoadingMultiplePages_RefetchesAllPages()
    {
        var fetchLog = new List<int>();

        using var sut = CreateQuery(
            fetcher: (_, page, _) =>
            {
                fetchLog.Add(page);
                return Task.FromResult($"page{page}");
            },
            getNextPageParam: info => info.PageParam + 1
        );

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        sut.FetchNextPage();
        await sut.State.Where(s => s.IsSuccess && s.Pages.Count == 2).FirstAsync();

        fetchLog.Clear();
        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess && !s.IsFetching).FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(fetchLog.Count).IsEqualTo(2);
        await Assert.That(fetchLog[0]).IsEqualTo(0);
        await Assert.That(fetchLog[1]).IsEqualTo(1);
    }

    [Test]
    public async Task Invalidate_WhenStaleTimeNotExpired_IsNoOp()
    {
        using var sut = CreateQuery(staleTime: TimeSpan.FromMinutes(5));

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        var stateBeforeInvalidate = sut.CurrentState;
        sut.Invalidate(); // stale time not expired
        await Task.Delay(50);

        await Assert.That(sut.CurrentState).IsEqualTo(stateBeforeInvalidate);
    }

    [Test]
    public async Task Fetch_OnException_TransitionsToFailure()
    {
        var error = new InvalidOperationException("oops");
        using var sut = CreateQuery(fetcher: (_, _, _) => Task.FromException<string>(error));

        using var sub = sut.State.Subscribe();
        sut.Refetch();

        var state = await sut.State.Where(s => s.IsFailure).FirstAsync();
        using var _ = Assert.Multiple();
        await Assert.That(state.Error).IsEqualTo(error);
    }

    [Test]
    public async Task Failure_PreservesExistingPages()
    {
        var failNext = false;
        var error = new InvalidOperationException("fetch failed");

        using var sut = CreateQuery(
            fetcher: (_, page, _) =>
            {
                if (failNext)
                {
                    return Task.FromException<string>(error);
                }

                return Task.FromResult($"page{page}");
            }
        );

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        failNext = true;
        sut.FetchNextPage();

        var failureState = await sut.State.Where(s => s.IsFailure).FirstAsync();
        using var _ = Assert.Multiple();
        await Assert.That(failureState.Pages.Count).IsEqualTo(1);
        await Assert.That(failureState.Pages[0]).IsEqualTo("page0");
    }

    [Test]
    public async Task Cancel_DuringInitialLoad_TransitionsToIdle()
    {
        var fetchTcs = new TaskCompletionSource<string>();

        using var sut = CreateQuery(fetcher: (_, _, ct) => fetchTcs.Task.WaitAsync(ct));

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        await sut.State.Where(s => s.IsFetching).FirstAsync();

        sut.Cancel();

        var state = await sut.State.Where(s => s.IsIdle).FirstAsync();
        await Assert.That(state.Pages.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Cancel_DuringFetchNext_ReturnsToSuccess()
    {
        var blockNext = new TaskCompletionSource<string>();

        using var sut = CreateQuery(
            fetcher: (_, page, ct) =>
            {
                if (page == 1)
                {
                    return blockNext.Task.WaitAsync(ct);
                }

                return Task.FromResult($"page{page}");
            }
        );

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        sut.FetchNextPage();
        await sut.State.Where(s => s.IsFetchingNextPage).FirstAsync();

        sut.Cancel();

        var state = await sut.State.Where(s => s.IsSuccess && !s.IsFetchingNextPage).FirstAsync();
        await Assert.That(state.Pages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task State_FirstSubscriber_TriggersStaleInvalidation()
    {
        using var sut = CreateQuery();

        // Mark stale by calling Invalidate with no subscribers
        sut.Invalidate();
        await Assert.That(sut.CurrentState.IsIdle).IsTrue();

        // First subscriber should trigger the deferred fetch
        using var sub = sut.State.Subscribe();
        var state = await sut.State.Where(s => s.IsSuccess).FirstAsync();
        await Assert.That(state.Pages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task RefetchInterval_TriggersRefetchAll()
    {
        var fetchCount = 0;

        using var sut = CreateQuery(
            fetcher: (_, _, _) =>
            {
                fetchCount++;
                return Task.FromResult("data");
            },
            refetchInterval: TimeSpan.FromSeconds(10)
        );

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();
        var countAfterFirstFetch = fetchCount;

        _scheduler.AdvanceBy(TimeSpan.FromSeconds(10).Ticks);
        await sut.State.Where(s => s.IsSuccess && !s.IsFetching).FirstAsync();

        await Assert.That(fetchCount).IsGreaterThan(countAfterFirstFetch);
    }

    [Test]
    public async Task HasPreviousPage_ReflectsGetPreviousPageParam()
    {
        using var sut = CreateQuery(
            fetcher: (_, page, _) => Task.FromResult($"page{page}"),
            getNextPageParam: _ => PageParam<int>.None,
            getPreviousPageParam: info =>
                info.PageParam > 0 ? PageParam<int>.Some(info.PageParam - 1) : PageParam<int>.None,
            initialPageParam: 3
        );

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        var state = await sut.State.Where(s => s.IsSuccess).FirstAsync();

        await Assert.That(state.HasPreviousPage).IsTrue();
    }

    [Test]
    public async Task HasPreviousPage_WhenAtStart_IsFalse()
    {
        using var sut = CreateQuery(
            getNextPageParam: _ => PageParam<int>.None,
            getPreviousPageParam: info =>
                info.PageParam > 0 ? PageParam<int>.Some(info.PageParam - 1) : PageParam<int>.None,
            initialPageParam: 0
        );

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        var state = await sut.State.Where(s => s.IsSuccess).FirstAsync();

        await Assert.That(state.HasPreviousPage).IsFalse();
    }

    [Test]
    public async Task InitialData_StartsInSuccessStateWithFirstPage()
    {
        using var sut = CreateQuery(initialData: "seed-page", initialPageParam: 1);

        using var _ = Assert.Multiple();
        await Assert.That(sut.CurrentState.IsSuccess).IsTrue();
        await Assert.That(sut.CurrentState.Pages.Count).IsEqualTo(1);
        await Assert.That(sut.CurrentState.Pages[0]).IsEqualTo("seed-page");
        await Assert.That(sut.CurrentState.PageParams[0]).IsEqualTo(1);
        await Assert.That(sut.CurrentState.HasData).IsTrue();
    }

    [Test]
    public async Task InitialData_ComputesHasNextPage()
    {
        using var sut = CreateQuery(
            initialData: "seed-page",
            initialPageParam: 1,
            getNextPageParam: info => PageParam<int>.Some(info.PageParam + 1)
        );

        await Assert.That(sut.CurrentState.HasNextPage).IsTrue();
    }

    [Test]
    public async Task InitialData_HasNextPage_IsFalse_WhenGetNextPageParamReturnsNone()
    {
        using var sut = CreateQuery(
            initialData: "seed-page",
            initialPageParam: 1,
            getNextPageParam: _ => PageParam<int>.None
        );

        await Assert.That(sut.CurrentState.HasNextPage).IsFalse();
    }

    [Test]
    public async Task InitialData_IsReplacedAfterRefetch()
    {
        using var sut = CreateQuery(
            fetcher: (_, page, _) => Task.FromResult($"fetched:{page}"),
            initialData: "seed-page",
            initialPageParam: 1
        );

        using var sub = sut.State.Subscribe();
        sut.Refetch();

        var state = await sut.State.Where(s => s.IsSuccess && s.Pages[0].StartsWith("fetched")).FirstAsync();
        await Assert.That(state.Pages[0]).IsEqualTo("fetched:1");
    }

    [Test]
    public async Task Cancel_ThenRefetch_StillFetches()
    {
        var fetchCount = 0;
        var gate = new TaskCompletionSource();

        using var sut = CreateQuery(
            fetcher: async (_, page, ct) =>
            {
                if (Interlocked.Increment(ref fetchCount) == 1)
                {
                    await gate.Task.WaitAsync(ct);
                }

                return $"page{page}";
            }
        );

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        await sut.State.Where(s => s.IsFetching).FirstAsync();

        sut.Cancel();
        await sut.State.Where(s => s.IsIdle).FirstAsync();

        // Cancel() swaps in a fresh token source, so the query must remain usable afterwards.
        sut.Refetch();
        var state = await sut.State.Where(s => s.IsSuccess).FirstAsync();

        await Assert.That(state.Pages[0]).IsEqualTo("page0");
    }

    [Test]
    public async Task Invalidate_WithNoSubscribers_DefersFetchUntilFirstSubscriber()
    {
        var fetchCount = 0;

        using var sut = CreateQuery(
            fetcher: (_, page, _) =>
            {
                Interlocked.Increment(ref fetchCount);
                return Task.FromResult($"page{page}");
            }
        );

        sut.Invalidate();
        await Assert.That(fetchCount).IsEqualTo(0);

        using var sub = sut.State.Subscribe();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        await Assert.That(fetchCount).IsEqualTo(1);
    }

    [Test]
    public async Task Invalidate_WithinStaleTime_IsNoOp()
    {
        var fetchCount = 0;

        using var sut = CreateQuery(
            fetcher: (_, page, _) =>
            {
                Interlocked.Increment(ref fetchCount);
                return Task.FromResult($"page{page}");
            },
            staleTime: TimeSpan.FromMinutes(5)
        );

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        sut.Invalidate();

        await Assert.That(fetchCount).IsEqualTo(1);
    }

    [Test]
    public async Task Unsubscribed_FiresWhenLastSubscriberDisposes()
    {
        using var sut = CreateQuery();

        var unsubscribed = 0;
        using var signal = sut.Unsubscribed.Subscribe(_ => Interlocked.Increment(ref unsubscribed));

        var first = sut.State.Subscribe();
        var second = sut.State.Subscribe();

        first.Dispose();
        await Assert.That(unsubscribed).IsEqualTo(0);

        second.Dispose();
        await Assert.That(unsubscribed).IsEqualTo(1);
    }

    [Test]
    public async Task CurrentData_ReturnsSnapshot_UnaffectedByLaterFetches()
    {
        using var sut = CreateQuery();

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        var snapshot = (IReadOnlyList<string>)sut.CurrentData!;

        sut.FetchNextPage();
        await sut.State.Where(s => s.IsSuccess && s.Pages.Count == 2).FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(snapshot.Count).IsEqualTo(1);
        await Assert.That(((IReadOnlyList<string>)sut.CurrentData!).Count).IsEqualTo(2);
    }

    [Test]
    public async Task Refetch_UnchangedPage_KeepsPreviousPageInstance()
    {
        // A fresh string instance per fetch — reference equality must come from the DataComparer
        // reuse in ExecuteRefetchAllAsync, not from string interning.
        using var sut = CreateQuery(fetcher: (_, page, _) => Task.FromResult(new string($"page{page}".ToCharArray())));

        using var sub = sut.State.Subscribe();
        sut.Refetch();
        var first = await sut.State.Where(s => s.IsSuccess).FirstAsync();

        var tcs = new TaskCompletionSource<InfiniteQueryState<string, int>>();
        using var secondSub = sut.State.Where(s => s.IsSuccess).Skip(1).Subscribe(s => tcs.TrySetResult(s));
        sut.Refetch();
        var second = await tcs.Task;

        await Assert.That(ReferenceEquals(second.Pages[0], first.Pages[0])).IsTrue();
    }

    [Test]
    public async Task MetricName_FallsBackToFirstKeyPart()
    {
        using var named = CreateQuery(name: "pages");
        using var unnamed = CreateQuery();

        using var _ = Assert.Multiple();
        await Assert.That(named.MetricName).IsEqualTo("pages");
        await Assert.That(unnamed.MetricName).IsEqualTo("test");
    }
}
