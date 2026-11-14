namespace Fetchling.Core.Tests;

/// <summary>
/// Regression tests for the ways a fetch or mutation pipeline used to get stuck, die silently, or publish
/// the wrong result: superseded fetches, no-op commands, throwing user callbacks, cancelled prefetches,
/// eviction, disabled queries and invalidation during a fetch in flight.
/// </summary>
public class FetchRobustnessTests
{
    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(5);
    private static readonly QueryInstrumentation _instrumentation = new(NullLogger.Instance);

    private readonly TestScheduler _scheduler = new();
    private QueryClient _client = default!;

    [Before(Test)]
    public void Setup() =>
        _client = new(
            new QueryClientOptions { StaleTime = TimeSpan.Zero, CacheTime = CacheTime },
            _scheduler,
            _instrumentation
        );

    [After(Test)]
    public void Teardown() => _client.Dispose();

    /// <summary>A fetcher whose calls stay pending until completed by hand, and which ignores its token.</summary>
    private sealed class ManualFetcher
    {
        public List<TaskCompletionSource<string>> Calls { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public Task<string> Fetch(CancellationToken ct)
        {
            var call = new TaskCompletionSource<string>();
            Calls.Add(call);
            Tokens.Add(ct);
            return call.Task;
        }
    }

    private IQuery<int, string> CreateQuery(
        Func<int, CancellationToken, Task<string>> fetcher,
        bool isEnabled = true,
        TimeSpan? refetchInterval = null
    ) =>
        _client.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = id => QueryKey.From("robustness", id),
                Fetcher = fetcher,
                IsEnabled = isEnabled,
                RefetchInterval = refetchInterval,
            }
        );

    private IInfiniteQuery<int, string, int> CreateInfiniteQuery(
        Func<int, int, CancellationToken, Task<string>> fetcher,
        Func<InfinitePageInfo<string, int>, PageParam<int>>? getNextPageParam = null
    ) =>
        _client.CreateInfiniteQuery(
            new InfiniteQueryOptions<int, string, int>
            {
                KeyFactory = id => QueryKey.From("robustness-infinite", id),
                Fetcher = fetcher,
                InitialPageParam = 0,
                GetNextPageParam = getNextPageParam ?? (info => info.PageParam + 1),
            }
        );

    private IReadOnlyList<IQueryCacheEntry> CacheEntries()
    {
        IReadOnlyList<IQueryCacheEntry> entries = [];
        using var subscription = _client.Entries.Subscribe(snapshot => entries = snapshot);
        return entries;
    }

    // ── Superseded fetches ───────────────────────────────────────────────────

    [Test]
    public async Task SupersededFetch_ThatIgnoresItsToken_DoesNotOverwriteTheNewerResult()
    {
        var fetcher = new ManualFetcher();
        using var query = CreateQuery((_, ct) => fetcher.Fetch(ct));
        query.SetArgs(1);
        using var subscription = query.State.Subscribe();

        query.Refetch();
        fetcher.Calls[1].SetResult("new");
        fetcher.Calls[0].SetResult("old");

        using var _ = Assert.Multiple();
        await Assert.That(query.CurrentState.IsSuccess).IsTrue();
        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("new");
    }

    [Test]
    public async Task SupersededFetch_ThatFailsAfterBeingSuperseded_DoesNotPublishItsFailure()
    {
        var fetcher = new ManualFetcher();
        using var query = CreateQuery((_, ct) => fetcher.Fetch(ct));
        query.SetArgs(1);
        using var subscription = query.State.Subscribe();

        query.Refetch();
        fetcher.Calls[1].SetResult("new");
        fetcher.Calls[0].SetException(new InvalidOperationException("late"));

        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("new");
    }

    [Test]
    public async Task InfiniteQuery_SupersededFetchNext_DoesNotAppendItsPageAfterARefetch()
    {
        var pendingNext = new TaskCompletionSource<string>();
        using var query = CreateInfiniteQuery((_, page, _) => page == 0 ? Task.FromResult("page0") : pendingNext.Task);
        query.SetArgs(1);
        using var subscription = query.State.Subscribe();

        query.FetchNextPage();
        query.Refetch();
        pendingNext.SetResult("page1");

        using var _ = Assert.Multiple();
        await Assert.That(query.CurrentState.IsSuccess).IsTrue();
        await Assert.That(query.CurrentState.Pages).IsEquivalentTo(["page0"]);
    }

    // ── No-op page commands ──────────────────────────────────────────────────

    [Test]
    public async Task InfiniteQuery_FetchNextPageDuringInitialLoad_DoesNotCancelTheLoad()
    {
        var calls = 0;
        var initialLoad = new TaskCompletionSource<string>();
        var token = CancellationToken.None;
        using var query = CreateInfiniteQuery(
            (_, _, ct) =>
            {
                calls++;
                token = ct;
                return initialLoad.Task;
            }
        );
        query.SetArgs(1);
        using var subscription = query.State.Subscribe();

        query.FetchNextPage();

        await Assert.That(token.IsCancellationRequested).IsFalse();

        initialLoad.SetResult("page0");

        using var _ = Assert.Multiple();
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(query.CurrentState.IsSuccess).IsTrue();
        await Assert.That(query.CurrentState.Pages).IsEquivalentTo(["page0"]);
    }

    [Test]
    public async Task InfiniteQuery_FetchNextPageWithNoNextPage_DoesNotCancelARefetchInFlight()
    {
        var calls = new List<TaskCompletionSource<string>>();
        using var query = CreateInfiniteQuery(
            (_, _, _) =>
            {
                var call = new TaskCompletionSource<string>();
                calls.Add(call);
                return call.Task;
            },
            getNextPageParam: _ => PageParam<int>.None
        );
        query.SetArgs(1);
        using var subscription = query.State.Subscribe();
        calls[0].SetResult("first");

        query.Refetch();
        query.FetchNextPage();
        calls[1].SetResult("second");

        using var _ = Assert.Multiple();
        await Assert.That(calls.Count).IsEqualTo(2);
        await Assert.That(query.CurrentState.IsSuccess).IsTrue();
        await Assert.That(query.CurrentState.Pages).IsEquivalentTo(["second"]);
    }

    // ── Throwing user code ───────────────────────────────────────────────────

    [Test]
    public async Task InfiniteQuery_ThrowingPageParamDelegate_DoesNotKillTheQuery()
    {
        var calls = 0;
        var shouldThrow = false;
        using var query = CreateInfiniteQuery(
            (_, page, _) =>
            {
                calls++;
                return Task.FromResult($"page{page}");
            },
            getNextPageParam: info => shouldThrow ? throw new InvalidOperationException("boom") : info.PageParam + 1
        );
        query.SetArgs(1);
        using var subscription = query.State.Subscribe();

        shouldThrow = true;
        await Assert.That(query.FetchNextPage).Throws<InvalidOperationException>();
        shouldThrow = false;

        query.FetchNextPage();

        using var _ = Assert.Multiple();
        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(query.CurrentState.Pages).IsEquivalentTo(["page0", "page1"]);
    }

    [Test]
    [Arguments("OnSuccess")]
    [Arguments("OnSettled")]
    public async Task Mutation_ThrowingSettleCallback_DoesNotKillTheMutation(string callback)
    {
        var runs = 0;
        var settled = 0;
        using var mutation = _client.CreateMutation(
            new MutationOptions<int, string>
            {
                Mutator = (args, _) =>
                {
                    runs++;
                    return Task.FromResult(args.ToString());
                },
                OnSuccess = (_, _) =>
                {
                    if (callback == "OnSuccess")
                    {
                        throw new InvalidOperationException("boom");
                    }
                },
                OnSettled = () =>
                {
                    settled++;

                    if (callback == "OnSettled")
                    {
                        throw new InvalidOperationException("boom");
                    }
                },
            }
        );

        mutation.Execute(1);
        mutation.Execute(2);

        using var _ = Assert.Multiple();
        await Assert.That(runs).IsEqualTo(2);
        await Assert.That(settled).IsEqualTo(2);
        await Assert.That(mutation.CurrentState.IsSuccess).IsTrue();
        await Assert.That(mutation.CurrentState.CurrentData).IsEqualTo("2");
    }

    [Test]
    public async Task Mutation_ThrowingOnFailure_DoesNotKillTheMutation()
    {
        var runs = 0;
        using var mutation = _client.CreateMutation(
            new MutationOptions<int, string>
            {
                Mutator = (_, _) =>
                {
                    runs++;
                    return Task.FromException<string>(new InvalidOperationException("mutator"));
                },
                OnFailure = _ => throw new InvalidOperationException("boom"),
            }
        );

        mutation.Execute(1);
        mutation.Execute(2);

        using var _ = Assert.Multiple();
        await Assert.That(runs).IsEqualTo(2);
        await Assert.That(mutation.CurrentState.IsFailure).IsTrue();
        await Assert.That(mutation.CurrentState.Error!.Message).IsEqualTo("mutator");
    }

    [Test]
    public async Task Mutation_ThrowingOnMutate_FailsTheRunAndLeavesTheMutationUsable()
    {
        var runs = 0;
        var shouldThrow = true;
        Exception? reported = null;
        using var mutation = _client.CreateMutation(
            new MutationOptions<int, string>
            {
                Mutator = (args, _) =>
                {
                    runs++;
                    return Task.FromResult(args.ToString());
                },
                OnMutate = _ =>
                {
                    if (shouldThrow)
                    {
                        throw new InvalidOperationException("boom");
                    }
                },
                OnFailure = error => reported = error,
            }
        );

        mutation.Execute(1);

        await Assert.That(runs).IsEqualTo(0);
        await Assert.That(mutation.CurrentState.IsFailure).IsTrue();
        await Assert.That(reported!.Message).IsEqualTo("boom");

        shouldThrow = false;
        mutation.Execute(2);

        await Assert.That(runs).IsEqualTo(1);
        await Assert.That(mutation.CurrentState.CurrentData).IsEqualTo("2");
    }

    // ── Prefetch ─────────────────────────────────────────────────────────────

    [Test]
    public async Task PrefetchAsync_Cancelled_DoesNotLeaveTheEntryFetching()
    {
        using var query = CreateQuery(
            async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return "never";
            }
        );
        using var cts = new CancellationTokenSource();

        var prefetch = query.PrefetchAsync(1, cts.Token);
        cts.Cancel();
        await prefetch;

        await Assert.That(CacheEntries().Single().Status).IsEqualTo(QueryStatus.Idle);
    }

    [Test]
    public async Task PrefetchAsync_Cancelled_RefetchesForASubscriberThatJoinedIt()
    {
        var fetcher = new ManualFetcher();
        using var query = CreateQuery(
            (_, ct) =>
            {
                var call = fetcher.Fetch(ct);
                ct.Register(() => fetcher.Calls[fetcher.Tokens.IndexOf(ct)].TrySetCanceled(ct));
                return call;
            }
        );
        using var cts = new CancellationTokenSource();

        var prefetch = query.PrefetchAsync(1, cts.Token);
        query.SetArgs(1);
        using var subscription = query.State.Subscribe();

        await Assert.That(fetcher.Calls.Count).IsEqualTo(1);

        cts.Cancel();
        await prefetch;
        fetcher.Calls[1].SetResult("data");

        using var _ = Assert.Multiple();
        await Assert.That(fetcher.Calls.Count).IsEqualTo(2);
        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("data");
    }

    [Test]
    public async Task PrefetchAsync_WhileAFetchIsInFlight_WaitsForItInsteadOfStartingAnother()
    {
        var fetcher = new ManualFetcher();
        using var query = CreateQuery((_, ct) => fetcher.Fetch(ct));
        query.SetArgs(1);
        using var subscription = query.State.Subscribe();

        var prefetch = query.PrefetchAsync(1);

        await Assert.That(prefetch.IsCompleted).IsFalse();

        fetcher.Calls[0].SetResult("data");
        await prefetch;

        using var _ = Assert.Multiple();
        await Assert.That(fetcher.Calls.Count).IsEqualTo(1);
        await Assert.That(fetcher.Tokens[0].IsCancellationRequested).IsFalse();
        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("data");
    }

    // ── Eviction ─────────────────────────────────────────────────────────────

    [Test]
    public async Task PrefetchedEntry_ThatNobodySubscribesTo_IsEvictedAfterCacheTime()
    {
        using var query = CreateQuery((_, _) => Task.FromResult("data"));
        await query.PrefetchAsync(1);

        await Assert.That(CacheEntries().Count).IsEqualTo(1);

        _scheduler.AdvanceBy(CacheTime.Ticks + 1);

        await Assert.That(CacheEntries()).IsEmpty();
    }

    [Test]
    public async Task PrefetchedEntry_ThatGetsASubscriber_IsNotEvicted()
    {
        using var query = CreateQuery((_, _) => Task.FromResult("data"));
        await query.PrefetchAsync(1);
        query.SetArgs(1);
        using var subscription = query.State.Subscribe();

        _scheduler.AdvanceBy(CacheTime.Ticks * 2);

        await Assert.That(CacheEntries().Count).IsEqualTo(1);
    }

    [Test]
    public async Task Observer_AfterItsEntryWasEvicted_FetchesAgainInsteadOfThrowing()
    {
        var calls = 0;
        using var query = CreateQuery(
            (_, _) =>
            {
                calls++;
                return Task.FromResult($"data{calls}");
            }
        );
        query.SetArgs(1);
        query.State.Subscribe().Dispose();

        _scheduler.AdvanceBy(CacheTime.Ticks + 1);

        await Assert.That(CacheEntries()).IsEmpty();
        await Assert.That(() => query.CurrentState).ThrowsNothing();

        Exception? error = null;
        using var subscription = query.State.Subscribe(_ => { }, ex => error = ex);

        using var _ = Assert.Multiple();
        await Assert.That(error).IsNull();
        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("data2");
    }

    [Test]
    public async Task InfiniteObserver_AfterItsEntryWasEvicted_FetchesAgainInsteadOfThrowing()
    {
        var calls = 0;
        using var query = CreateInfiniteQuery(
            (_, page, _) =>
            {
                calls++;
                return Task.FromResult($"page{page}");
            }
        );
        query.SetArgs(1);
        query.State.Subscribe().Dispose();

        _scheduler.AdvanceBy(CacheTime.Ticks + 1);

        Exception? error = null;
        using var subscription = query.State.Subscribe(_ => { }, ex => error = ex);

        using var _ = Assert.Multiple();
        await Assert.That(error).IsNull();
        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(query.CurrentState.IsSuccess).IsTrue();
    }

    // ── Disabled queries ─────────────────────────────────────────────────────

    [Test]
    public async Task DisabledQuery_IsNotFetchedByInvalidation_UntilEnabled()
    {
        var calls = 0;
        using var query = CreateQuery(
            (_, _) =>
            {
                calls++;
                return Task.FromResult("data");
            },
            isEnabled: false,
            refetchInterval: TimeSpan.FromSeconds(5)
        );
        query.SetArgs(1);
        using var subscription = query.State.Subscribe();

        _client.Invalidate(_ => true);
        query.Invalidate();
        _scheduler.AdvanceBy(TimeSpan.FromSeconds(11).Ticks);

        await Assert.That(calls).IsEqualTo(0);

        query.SetEnabled(true);

        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task DisabledInfiniteQuery_IsNotFetchedByInvalidation_UntilEnabled()
    {
        var calls = 0;
        using var query = _client.CreateInfiniteQuery(
            new InfiniteQueryOptions<int, string, int>
            {
                KeyFactory = id => QueryKey.From("robustness-infinite", id),
                Fetcher = (_, page, _) =>
                {
                    calls++;
                    return Task.FromResult($"page{page}");
                },
                InitialPageParam = 0,
                GetNextPageParam = info => info.PageParam + 1,
                IsEnabled = false,
            }
        );
        query.SetArgs(1);
        using var subscription = query.State.Subscribe();

        _client.Invalidate(_ => true);
        query.Invalidate();

        await Assert.That(calls).IsEqualTo(0);

        query.SetEnabled(true);

        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task DisabledObserver_DoesNotStopAnEnabledObserverOnTheSameKeyFromFetching()
    {
        var calls = 0;
        Task<string> Fetch(int id, CancellationToken ct)
        {
            calls++;
            return Task.FromResult("data");
        }

        using var disabled = CreateQuery(Fetch, isEnabled: false);
        using var enabled = CreateQuery(Fetch);
        disabled.SetArgs(1);
        using var disabledSubscription = disabled.State.Subscribe();
        enabled.SetArgs(1);
        using var enabledSubscription = enabled.State.Subscribe();

        await Assert.That(calls).IsEqualTo(1);

        _client.Invalidate(_ => true);

        using var _ = Assert.Multiple();
        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(disabled.CurrentState.CurrentData).IsEqualTo("data");
    }

    // ── Invalidation during a fetch in flight ────────────────────────────────

    [Test]
    public async Task Invalidate_WhileAFetchIsInFlight_LetsItFinishThenRefetchesOnce()
    {
        var fetcher = new ManualFetcher();
        var published = new List<string>();
        using var query = CreateQuery((_, ct) => fetcher.Fetch(ct));
        query.SetArgs(1);
        using var subscription = query.Success.Subscribe(published.Add);

        _client.Invalidate(_ => true);
        _client.Invalidate(_ => true);

        await Assert.That(fetcher.Calls.Count).IsEqualTo(1);
        await Assert.That(fetcher.Tokens[0].IsCancellationRequested).IsFalse();

        fetcher.Calls[0].SetResult("first");

        await Assert.That(published).IsEquivalentTo(["first"]);
        await Assert.That(fetcher.Calls.Count).IsEqualTo(2);

        fetcher.Calls[1].SetResult("second");

        using var _ = Assert.Multiple();
        await Assert.That(fetcher.Calls.Count).IsEqualTo(2);
        await Assert.That(published).IsEquivalentTo(["first", "second"]);
    }

    [Test]
    public async Task RefetchInterval_ShorterThanTheFetch_DoesNotStarveIt()
    {
        var fetcher = new ManualFetcher();
        using var query = CreateQuery((_, ct) => fetcher.Fetch(ct), refetchInterval: TimeSpan.FromSeconds(5));
        query.SetArgs(1);
        using var subscription = query.State.Subscribe();

        _scheduler.AdvanceBy(TimeSpan.FromSeconds(16).Ticks);

        await Assert.That(fetcher.Calls.Count).IsEqualTo(1);
        await Assert.That(fetcher.Tokens[0].IsCancellationRequested).IsFalse();

        fetcher.Calls[0].SetResult("data");

        using var _ = Assert.Multiple();
        await Assert.That(fetcher.Calls.Count).IsEqualTo(1);
        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("data");
    }

    [Test]
    public async Task SecondObserverAttaching_WhileAFetchIsInFlight_SharesItWithoutAnotherFetch()
    {
        var fetcher = new ManualFetcher();
        using var first = CreateQuery((_, ct) => fetcher.Fetch(ct));
        using var second = CreateQuery((_, ct) => fetcher.Fetch(ct));
        first.SetArgs(1);
        using var firstSubscription = first.State.Subscribe();

        second.SetArgs(1);
        using var secondSubscription = second.State.Subscribe();
        fetcher.Calls[0].SetResult("data");

        using var _ = Assert.Multiple();
        await Assert.That(fetcher.Calls.Count).IsEqualTo(1);
        await Assert.That(fetcher.Tokens[0].IsCancellationRequested).IsFalse();
        await Assert.That(second.CurrentState.CurrentData).IsEqualTo("data");
    }

    [Test]
    public async Task InfiniteQuery_InvalidateWhileFetchingNextPage_KeepsThePageThenRefetchesAll()
    {
        var calls = new List<(int Page, TaskCompletionSource<string> Call)>();
        using var query = CreateInfiniteQuery(
            (_, page, _) =>
            {
                var call = new TaskCompletionSource<string>();
                calls.Add((page, call));
                return call.Task;
            }
        );
        query.SetArgs(1);
        using var subscription = query.State.Subscribe();
        calls[0].Call.SetResult("page0");

        query.FetchNextPage();
        _client.Invalidate(_ => true);

        await Assert.That(calls.Count).IsEqualTo(2);

        calls[1].Call.SetResult("page1");

        // The page fetch completed and committed; the held-back invalidation now refetches both pages.
        await Assert.That(calls.Count).IsEqualTo(3);

        calls[2].Call.SetResult("page0'");
        calls[3].Call.SetResult("page1'");

        using var _ = Assert.Multiple();
        await Assert.That(calls.Count).IsEqualTo(4);
        await Assert.That(query.CurrentState.Pages).IsEquivalentTo(["page0'", "page1'"]);
    }
}
