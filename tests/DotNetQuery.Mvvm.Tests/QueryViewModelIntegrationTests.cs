namespace DotNetQuery.Mvvm.Tests;

public class QueryViewModelIntegrationTests
{
    private readonly RecordingSynchronizationContext _uiContext = new();

    [Test]
    public async Task RealClient_SetArgs_FetchesAndNotifiesThroughDispatcher()
    {
        using var client = QueryClientFactory.Create(new QueryClientOptions());
        using var sut = new QueryViewModel<int, string>(
            client,
            new QueryOptions<int, string>
            {
                KeyFactory = args => QueryKey.From("integration", args),
                Fetcher = (args, _) => Task.FromResult($"value-{args}"),
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
        await Assert.That(sut.Data).IsEqualTo("value-1");
        await Assert.That(raised).Contains(nameof(sut.Data));
    }
}
