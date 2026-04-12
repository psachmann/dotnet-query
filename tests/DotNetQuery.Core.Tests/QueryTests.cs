namespace DotNetQuery.Core.Tests;

public class QueryTests
{
    private readonly TestScheduler _scheduler = new();

    private static readonly QueryInstrumentation _instrumentation = new(NullLogger.Instance);

    private Query<int, string> CreateQuery(
        Func<int, CancellationToken, Task<string>>? fetcher = null,
        TimeSpan? staleTime = null,
        TimeSpan? cacheTime = null,
        TimeSpan? refetchInterval = null,
        int args = 0
    )
    {
        var options = new EffectiveQueryOptions<int, string>
        {
            Fetcher = fetcher ?? ((_, _) => Task.FromResult("data")),
            StaleTime = staleTime ?? TimeSpan.Zero,
            CacheTime = cacheTime ?? TimeSpan.FromMinutes(5),
            RefetchInterval = refetchInterval,
            RetryHandler = new DefaultRetryHandler(),
            IsEnabled = true,
            DataComparer = EqualityComparer<string>.Default,
        };

        return new Query<int, string>(QueryKey.From("test"), args, options, _scheduler, _instrumentation);
    }

    [Test]
    public async Task Key_ReturnsConstructedKey()
    {
        var key = QueryKey.From("my", "key");
        var options = new EffectiveQueryOptions<int, string>
        {
            Fetcher = (_, _) => Task.FromResult("data"),
            StaleTime = TimeSpan.Zero,
            CacheTime = TimeSpan.FromMinutes(5),
            RefetchInterval = null,
            RetryHandler = new DefaultRetryHandler(),
            IsEnabled = true,
            DataComparer = EqualityComparer<string>.Default,
        };
        using var sut = new Query<int, string>(key, 0, options, _scheduler, _instrumentation);

        await Assert.That(sut.Key).IsEqualTo(key);
    }

    [Test]
    public async Task CacheTime_ReturnsOptionsValue()
    {
        using var sut = CreateQuery(cacheTime: TimeSpan.FromMinutes(3));

        await Assert.That(sut.CacheTime).IsEqualTo(TimeSpan.FromMinutes(3));
    }

    [Test]
    public async Task InitialCurrentState_IsIdle()
    {
        using var sut = CreateQuery();

        await Assert.That(sut.CurrentState.IsIdle).IsTrue();
    }

    [Test]
    public async Task Refetch_TransitionsToFetching()
    {
        using var sut = CreateQuery();

        var tcs = new TaskCompletionSource<QueryState<string>>();
        using var sub = sut.State.Where(s => s.IsFetching).Subscribe(s => tcs.TrySetResult(s));

        sut.Refetch();

        var state = await tcs.Task;
        await Assert.That(state.IsFetching).IsTrue();
    }

    [Test]
    public async Task Refetch_OnSuccess_TransitionsToSuccess()
    {
        using var sut = CreateQuery(fetcher: (_, _) => Task.FromResult("ok"));
        using var sub = sut.State.Subscribe();

        sut.Refetch();

        var state = await sut.State.Where(s => s.IsSuccess).FirstAsync();
        await Assert.That(state.CurrentData).IsEqualTo("ok");
    }

    [Test]
    public async Task Refetch_OnFailure_TransitionsToFailure()
    {
        var error = new Exception("bang");
        using var sut = CreateQuery(fetcher: (_, _) => Task.FromException<string>(error));
        using var sub = sut.State.Subscribe();

        sut.Refetch();

        var state = await sut.State.Where(s => s.IsFailure).FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(state.IsFailure).IsTrue();
        await Assert.That(state.Error).IsEqualTo(error);
    }

    [Test]
    public async Task Refetch_IgnoresStaleTime()
    {
        var fetchCount = 0;
        using var sut = CreateQuery(
            fetcher: (_, _) =>
            {
                fetchCount++;
                return Task.FromResult("data");
            },
            staleTime: TimeSpan.FromHours(1)
        );
        using var sub = sut.State.Subscribe();

        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        await Assert.That(fetchCount).IsEqualTo(2);
    }

    [Test]
    public async Task Refetch_DuringSecondFetch_LastDataIsFromPreviousSuccess()
    {
        var gate = new TaskCompletionSource();
        var callCount = 0;
        using var sut = CreateQuery(
            fetcher: async (_, ct) =>
            {
                if (++callCount > 1)
                    await gate.Task.WaitAsync(ct);
                return $"result{callCount}";
            }
        );
        using var sub = sut.State.Subscribe();

        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        var tcs = new TaskCompletionSource<QueryState<string>>();
        using var fetchSub = sut.State.Where(s => s.IsFetching).Subscribe(s => tcs.TrySetResult(s));

        sut.Refetch();
        var fetchingState = await tcs.Task;

        await Assert.That(fetchingState.LastData).IsEqualTo("result1");
        gate.SetResult();
    }

    [Test]
    public async Task Fetch_Success_SetsLastData()
    {
        using var sut = CreateQuery(fetcher: (_, _) => Task.FromResult("value"));
        using var sub = sut.State.Subscribe();

        sut.Refetch();
        var state = await sut.State.Where(s => s.IsSuccess).FirstAsync();

        await Assert.That(state.LastData).IsNull(); // first fetch has no prior data
    }

    [Test]
    public async Task Cancel_WhileFetching_TransitionsToIdle()
    {
        var gate = new TaskCompletionSource();
        using var sut = CreateQuery(
            fetcher: async (_, ct) =>
            {
                await gate.Task.WaitAsync(ct);
                return "data";
            }
        );
        using var sub = sut.State.Subscribe();

        sut.Refetch();
        await sut.State.Where(s => s.IsFetching).FirstAsync();

        sut.Cancel();

        var state = await sut.State.Where(s => s.IsIdle).FirstAsync();
        await Assert.That(state.IsIdle).IsTrue();
    }

    [Test]
    public async Task Cancel_WhileFetching_PreservesLastData()
    {
        var gate = new TaskCompletionSource();
        var callCount = 0;
        using var sut = CreateQuery(
            fetcher: async (_, ct) =>
            {
                if (++callCount > 1)
                {
                    await gate.Task.WaitAsync(ct);
                }

                return $"result{callCount}";
            }
        );
        using var sub = sut.State.Subscribe();

        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        sut.Refetch();
        await sut.State.Where(s => s.IsFetching).FirstAsync();

        sut.Cancel();
        var idleState = await sut.State.Where(s => s.IsIdle).FirstAsync();

        await Assert.That(idleState.LastData).IsEqualTo("result1");
        gate.SetResult();
    }

    [Test]
    public async Task Invalidate_WithActiveSubscriber_TriggersRefetch()
    {
        using var sut = CreateQuery();

        var tcs = new TaskCompletionSource<QueryState<string>>();
        using var sub = sut.State.Where(s => s.IsFetching).Subscribe(s => tcs.TrySetResult(s));

        sut.Invalidate();

        var state = await tcs.Task;
        await Assert.That(state.IsFetching).IsTrue();
    }

    [Test]
    public async Task Invalidate_WithNoSubscriber_DoesNotFetchImmediately()
    {
        var fetched = false;
        using var sut = CreateQuery(
            fetcher: (_, _) =>
            {
                fetched = true;
                return Task.FromResult("data");
            }
        );

        sut.Invalidate(); // no subscriber — should mark stale, not fetch
        await Task.Delay(50);

        await Assert.That(fetched).IsFalse();
    }

    [Test]
    public async Task Invalidate_WithNoSubscriber_MarksStale_FetchesWhenFirstSubscriberJoins()
    {
        using var sut = CreateQuery();

        sut.Invalidate(); // stale, no subscribers yet

        var tcs = new TaskCompletionSource<QueryState<string>>();
        using var sub = sut.State.Where(s => s.IsFetching).Subscribe(s => tcs.TrySetResult(s));

        // subscribing should trigger the deferred fetch
        var state = await tcs.Task;
        await Assert.That(state.IsFetching).IsTrue();
    }

    [Test]
    public async Task Invalidate_WithinStaleTime_DoesNotRefetch()
    {
        var fetchCount = 0;
        using var sut = CreateQuery(
            fetcher: (_, _) =>
            {
                fetchCount++;
                return Task.FromResult("data");
            },
            staleTime: TimeSpan.FromMinutes(5)
        );
        using var sub = sut.State.Subscribe();

        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        // No virtual time has advanced — still within StaleTime
        sut.Invalidate();
        await Task.Delay(50);

        await Assert.That(fetchCount).IsEqualTo(1);
    }

    [Test]
    public async Task Invalidate_AfterStaleTimeElapsed_Refetches()
    {
        var fetchCount = 0;
        using var sut = CreateQuery(
            fetcher: (_, _) =>
            {
                fetchCount++;
                return Task.FromResult("data");
            },
            staleTime: TimeSpan.FromMinutes(5)
        );

        var tcs = new TaskCompletionSource<QueryState<string>>();
        using var sub = sut.State.Where(s => s.IsFetching).Subscribe(s => tcs.TrySetResult(s));

        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        _scheduler.AdvanceBy(TimeSpan.FromMinutes(5).Ticks + 1);

        sut.Invalidate();

        await tcs.Task;
        await Assert.That(fetchCount).IsEqualTo(2);
    }

    [Test]
    public async Task Invalidate_SecondSubscriberJoining_DoesNotTriggerRedundantFetch()
    {
        var fetchCount = 0;
        using var sut = CreateQuery(
            fetcher: (_, _) =>
            {
                fetchCount++;
                return Task.FromResult("data");
            }
        );

        sut.Invalidate(); // marks stale

        var tcs = new TaskCompletionSource();
        using var subA = sut.State.Where(s => s.IsSuccess).Subscribe(_ => tcs.TrySetResult());
        await tcs.Task; // first subscriber triggers the deferred fetch

        // A second subscriber joining after the fetch should NOT re-trigger
        using var subB = sut.State.Subscribe();
        await Task.Delay(50);

        await Assert.That(fetchCount).IsEqualTo(1);
    }

    [Test]
    public async Task State_ReplayCurrentValueToNewSubscriber()
    {
        using var sut = CreateQuery(fetcher: (_, _) => Task.FromResult("cached"));
        using var sub = sut.State.Subscribe();

        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        // New subscriber should immediately receive Success (current BehaviorSubject value)
        QueryState<string>? received = null;
        using var late = sut.State.Subscribe(s => received = s);

        await Assert.That(received?.IsSuccess).IsTrue();
    }

    [Test]
    public async Task RefetchInterval_PeriodicallTriggersRefetch()
    {
        var fetchCount = 0;
        using var sut = CreateQuery(
            fetcher: (_, _) =>
            {
                fetchCount++;
                return Task.FromResult("data");
            },
            refetchInterval: TimeSpan.FromMinutes(1)
        );
        using var sub = sut.State.Subscribe();

        // Advance past two intervals
        _scheduler.AdvanceBy(TimeSpan.FromMinutes(2).Ticks);

        // Wait for async fetches triggered by the scheduler to complete
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        await Assert.That(fetchCount).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task NoRefetchInterval_DoesNotFetchAutomatically()
    {
        var fetchCount = 0;
        using var sut = CreateQuery(
            fetcher: (_, _) =>
            {
                fetchCount++;
                return Task.FromResult("data");
            },
            refetchInterval: null
        );
        using var sub = sut.State.Subscribe();

        _scheduler.AdvanceBy(TimeSpan.FromHours(1).Ticks);
        await Task.Delay(50);

        await Assert.That(fetchCount).IsEqualTo(0);
    }

    [Test]
    public async Task CurrentState_ReflectsMostRecentState()
    {
        using var sut = CreateQuery(fetcher: (_, _) => Task.FromResult("latest"));
        using var sub = sut.State.Subscribe();

        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        await Assert.That(sut.CurrentState.CurrentData).IsEqualTo("latest");
    }

    [Test]
    public async Task Dispose_CompletesStateObservable()
    {
        using var sut = CreateQuery();

        var completed = false;
        using var sub = sut.State.Subscribe(_ => { }, () => completed = true);

        sut.Dispose();

        await Assert.That(completed).IsTrue();
    }

    [Test]
    public async Task Dispose_IsIdempotent()
    {
        var sut = CreateQuery();

        sut.Dispose();
        sut.Dispose();

        await Assert.That(sut.Key).IsEqualTo(QueryKey.From("test"));
    }

    [Test]
    public async Task Refetch_OnSuccess_RecordsActivityWithQueryKeyTag()
    {
        var key = QueryKey.From("activity-fetch-success");
        Activity? recorded = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == QueryTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a =>
            {
                if (Equals(a.GetTagItem(QueryTelemetryTags.TagQueryKey), key.ToString()))
                    recorded = a;
            },
        };
        ActivitySource.AddActivityListener(listener);

        var options = new EffectiveQueryOptions<int, string>
        {
            Fetcher = (_, _) => Task.FromResult("ok"),
            StaleTime = TimeSpan.Zero,
            CacheTime = TimeSpan.FromMinutes(5),
            RefetchInterval = null,
            RetryHandler = new DefaultRetryHandler(),
            IsEnabled = true,
            DataComparer = EqualityComparer<string>.Default,
        };
        using var sut = new Query<int, string>(key, 0, options, _scheduler, _instrumentation);
        using var sub = sut.State.Subscribe();

        sut.Refetch();
        await sut.State.Where(s => s.IsSuccess).FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(recorded).IsNotNull();
        await Assert.That(recorded!.Status).IsEqualTo(ActivityStatusCode.Ok);
    }

    [Test]
    public async Task Refetch_OnFailure_RecordsActivityWithErrorTags()
    {
        var key = QueryKey.From("activity-fetch-failure");
        Activity? recorded = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == QueryTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a =>
            {
                if (Equals(a.GetTagItem(QueryTelemetryTags.TagQueryKey), key.ToString()))
                    recorded = a;
            },
        };
        ActivitySource.AddActivityListener(listener);

        var options = new EffectiveQueryOptions<int, string>
        {
            Fetcher = (_, _) => Task.FromException<string>(new InvalidOperationException("err")),
            StaleTime = TimeSpan.Zero,
            CacheTime = TimeSpan.FromMinutes(5),
            RefetchInterval = null,
            RetryHandler = new DefaultRetryHandler(),
            IsEnabled = true,
            DataComparer = EqualityComparer<string>.Default,
        };
        using var sut = new Query<int, string>(key, 0, options, _scheduler, _instrumentation);
        using var sub = sut.State.Subscribe();

        sut.Refetch();
        await sut.State.Where(s => s.IsFailure).FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(recorded).IsNotNull();
        await Assert.That(recorded!.Status).IsEqualTo(ActivityStatusCode.Error);
        await Assert
            .That(recorded.GetTagItem(QueryTelemetryTags.TagErrorType))
            .IsEqualTo(nameof(InvalidOperationException));
    }

    [Test]
    public async Task Dispose_WhileFetchInFlight_DoesNotRaiseObjectDisposedException()
    {
        // Regression: without the _disposed guard in FetchAsync, calling Dispose() while a fetch
        // was in-flight caused _state.OnNext() to be invoked on an already-disposed BehaviorSubject,
        // raising ObjectDisposedException on a ThreadPool thread.
        var started = new TaskCompletionSource();
        var gate = new TaskCompletionSource();
        Exception? firstChanceOde = null;

        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> handler = (_, e) =>
        {
            if (e.Exception is ObjectDisposedException)
            {
                firstChanceOde = e.Exception;
            }
        };

        AppDomain.CurrentDomain.FirstChanceException += handler;

        try
        {
            var sut = CreateQuery(
                fetcher: async (_, ct) =>
                {
                    started.TrySetResult();
                    await gate.Task.WaitAsync(ct);
                    return "data";
                }
            );

            using var sub = sut.State.Subscribe();
            sut.Refetch();
            await started.Task;

            sut.Dispose();
            gate.SetResult();

            await Task.Delay(100);

            await Assert.That(firstChanceOde).IsNull();
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= handler;
        }
    }
}
