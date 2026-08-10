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
        Func<int, int, CancellationToken, Task<string>>? fetcher = null
    )
    {
        var options = new InfiniteQueryOptions<int, string, int>
        {
            KeyFactory = args => QueryKey.From("observer-test", args),
            Fetcher = fetcher ?? ((_, page, _) => Task.FromResult(new string($"page{page}".ToCharArray()))),
            InitialPageParam = 0,
            GetNextPageParam = info => info.PageParam + 1,
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
}
