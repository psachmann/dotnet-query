namespace DotNetQuery.Core.Tests;

public class InitialDataTests
{
    private readonly TestScheduler _scheduler = new();
    private QueryCache _cache = default!;

    private static readonly QueryInstrumentation _instrumentation = new(NullLogger.Instance);

    [Before(Test)]
    public void Setup() => _cache = new(_scheduler, _instrumentation);

    [After(Test)]
    public void Teardown() => _cache.Dispose();

    private QueryObserver<int, string> CreateObserver(
        Func<int, CancellationToken, Task<string>>? fetcher = null,
        string? initialData = null
    )
    {
        var options = new QueryOptions<int, string>
        {
            KeyFactory = _ => QueryKey.From("initial-data-test"),
            Fetcher = fetcher ?? ((_, _) => Task.FromResult("real")),
            InitialData = initialData,
        };
        return new QueryObserver<int, string>(
            options,
            new QueryClientOptions { StaleTime = TimeSpan.Zero },
            _cache,
            _scheduler,
            _instrumentation
        );
    }

    [Test]
    public async Task CurrentState_IsSuccess_ImmediatelyAfterSetArgs()
    {
        using var sut = CreateObserver(initialData: "seed");

        sut.SetArgs(0);

        // Synchronous check — no await between SetArgs and assertion
        await Assert.That(sut.CurrentState.IsSuccess).IsTrue();
    }

    [Test]
    public async Task InitialData_IsShownAsCurrentData()
    {
        using var sut = CreateObserver(initialData: "seed");

        sut.SetArgs(0);

        await Assert.That(sut.CurrentState.CurrentData).IsEqualTo("seed");
    }

    [Test]
    public async Task BackgroundFetch_TriggersAfterSubscribe()
    {
        using var sut = CreateObserver(initialData: "seed", fetcher: (_, _) => Task.FromResult("real"));

        var settled = new TaskCompletionSource();
        using var sub = sut.Settled.Subscribe(_ => settled.TrySetResult());

        sut.SetArgs(0);
        await settled.Task;

        await Assert.That(sut.CurrentState.CurrentData).IsEqualTo("real");
    }

    [Test]
    public async Task RealData_ReplacesInitialData()
    {
        using var sut = CreateObserver(initialData: "seed", fetcher: (_, _) => Task.FromResult("real"));

        var settled = new TaskCompletionSource();
        using var sub = sut.Settled.Subscribe(_ => settled.TrySetResult());

        sut.SetArgs(0);
        await settled.Task;

        using var _ = Assert.Multiple();
        await Assert.That(sut.CurrentState.IsSuccess).IsTrue();
        await Assert.That(sut.CurrentState.CurrentData).IsEqualTo("real");
    }

    [Test]
    public async Task InitialData_ShownViaLastData_DuringFetch()
    {
        var fetchStarted = new TaskCompletionSource();
        var releaseAfterFetch = new TaskCompletionSource();

        using var sut = CreateObserver(
            initialData: "seed",
            fetcher: async (_, ct) =>
            {
                fetchStarted.TrySetResult();
                await releaseAfterFetch.Task.WaitAsync(ct);

                return "real";
            }
        );

        var fetchingState = new TaskCompletionSource<QueryState<string>>();
        using var sub = sut.State.Where(s => s.IsFetching).Subscribe(s => fetchingState.TrySetResult(s));

        sut.SetArgs(0);
        await fetchStarted.Task;

        var state = await fetchingState.Task;
        releaseAfterFetch.TrySetResult();

        await Assert.That(state.LastData).IsEqualTo("seed");
    }

    [Test]
    public async Task WithoutInitialData_StartsIdle()
    {
        using var sut = CreateObserver(initialData: null);

        sut.SetArgs(0);

        // Before any subscription triggers a fetch, CurrentState should be Idle
        // (SetArgs creates the Query in Idle, subscribe triggers the invalidate)
        await Assert.That(sut.CurrentState.IsIdle).IsTrue();
    }

    [Test]
    public async Task CachedEntry_TakesPrecedenceOverInitialData()
    {
        // First observer fetches real data into the cache
        using var first = CreateObserver(initialData: "seed", fetcher: (_, _) => Task.FromResult("real"));

        var firstSettled = new TaskCompletionSource();
        using var sub1 = first.Settled.Subscribe(_ => firstSettled.TrySetResult());
        first.SetArgs(0);
        await firstSettled.Task;

        // Second observer with different initial data — cache already has real data
        using var second = CreateObserver(initialData: "other-seed", fetcher: (_, _) => Task.FromResult("real"));
        second.SetArgs(0);

        // Should see real data from cache, not "other-seed"
        await Assert.That(second.CurrentState.CurrentData).IsEqualTo("real");
    }
}
