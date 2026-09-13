namespace DotNetQuery.Mvvm.Tests;

public class InfiniteQueryViewModelIntegrationTests
{
    private readonly RecordingSynchronizationContext _uiContext = new();

    [Test]
    public async Task RealClient_SetArgs_FetchesAndNotifiesThroughDispatcher()
    {
        using var client = QueryClientFactory.Create(new QueryClientOptions());
        using var sut = new InfiniteQueryViewModel<int, string, int>(
            client,
            new InfiniteQueryOptions<int, string, int>
            {
                KeyFactory = args => QueryKey.From("infinite-integration", args),
                Fetcher = (args, page, _) => Task.FromResult($"value-{args}-page{page}"),
                InitialPageParam = 0,
                GetNextPageParam = info => info.PageParam + 1,
            },
            new SynchronizationContextUiDispatcher(_uiContext)
        );
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        sut.SetArgs(1);

        // The VM subscribed to State before this subscriber, so once this await completes,
        // the success emission has already been queued on the recording context.
        _ = await sut.Query.State.Where(s => s.IsSuccess).FirstAsync();
        _uiContext.DrainAll();

        await Assert.That(sut.IsSuccess).IsTrue();
        await Assert.That(sut.Pages).IsEquivalentTo(new[] { "value-1-page0" });
        await Assert.That(raised).Contains(nameof(sut.Pages));
    }

    [Test]
    public async Task RealClient_FetchNextPage_AppendsPage()
    {
        using var client = QueryClientFactory.Create(new QueryClientOptions());
        using var sut = new InfiniteQueryViewModel<int, string, int>(
            client,
            new InfiniteQueryOptions<int, string, int>
            {
                KeyFactory = args => QueryKey.From("infinite-integration-next", args),
                Fetcher = (args, page, _) => Task.FromResult($"value-{args}-page{page}"),
                InitialPageParam = 0,
                GetNextPageParam = info => info.PageParam + 1,
            },
            new SynchronizationContextUiDispatcher(_uiContext)
        );

        sut.SetArgs(1);
        _ = await sut.Query.State.Where(s => s.IsSuccess).FirstAsync();
        _uiContext.DrainAll();

        sut.FetchNextPageCommand.Execute(null);
        _ = await sut.Query.State.Where(s => s.Pages.Count == 2).FirstAsync();
        _uiContext.DrainAll();

        await Assert.That(sut.Pages).IsEquivalentTo(new[] { "value-1-page0", "value-1-page1" });
    }
}
