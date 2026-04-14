namespace DotNetQuery.Core.Tests;

public class PrefetchQueryTests
{
    private static readonly QueryInstrumentation _instrumentation = new(NullLogger.Instance);

    private IQueryClient CreateClient(TimeSpan? staleTime = null) =>
        QueryClientFactory.Create(new QueryClientOptions { StaleTime = staleTime ?? TimeSpan.Zero });

    private QueryOptions<int, string> MakeOptions(
        Func<int, CancellationToken, Task<string>>? fetcher = null,
        TimeSpan? staleTime = null
    ) =>
        new()
        {
            KeyFactory = id => QueryKey.From("prefetch-test", id),
            Fetcher = fetcher ?? ((_, _) => Task.FromResult("data")),
            StaleTime = staleTime,
        };

    [Test]
    public async Task PrefetchQueryAsync_ReturnsTask_ThatCompletes()
    {
        using var client = CreateClient();
        var options = MakeOptions();

        await client.PrefetchQueryAsync(1, options);
    }

    [Test]
    public async Task PrefetchQueryAsync_PopulatesCacheBeforeObserverSubscribes()
    {
        using var client = CreateClient();
        var options = MakeOptions(fetcher: (_, _) => Task.FromResult("prefetched"));

        await client.PrefetchQueryAsync(1, options);

        using var query = client.CreateQuery(options);
        query.SetArgs(1);

        using var _ = Assert.Multiple();
        await Assert.That(query.CurrentState.IsSuccess).IsTrue();
        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("prefetched");
    }

    [Test]
    public async Task PrefetchQueryAsync_ObserverSeesData_WithNoLoadingFlash()
    {
        using var client = CreateClient(staleTime: TimeSpan.FromMinutes(5));
        var options = MakeOptions(fetcher: (_, _) => Task.FromResult("prefetched"), staleTime: TimeSpan.FromMinutes(5));

        await client.PrefetchQueryAsync(1, options);

        using var query = client.CreateQuery(options);

        var states = new List<QueryState<string>>();
        using var sub = query.State.Subscribe(s => states.Add(s));

        query.SetArgs(1);

        using var _ = Assert.Multiple();
        await Assert.That(states.Any(s => s.IsIdle || s.IsFetching)).IsFalse();
        await Assert.That(states.All(s => s.IsSuccess)).IsTrue();
    }

    [Test]
    public async Task PrefetchQueryAsync_SkipsRefetch_WhenCacheIsFresh()
    {
        using var client = CreateClient(staleTime: TimeSpan.FromMinutes(5));
        var fetchCount = 0;

        var options = MakeOptions(
            fetcher: (_, _) =>
            {
                fetchCount++;
                return Task.FromResult("data");
            },
            staleTime: TimeSpan.FromMinutes(5)
        );

        await client.PrefetchQueryAsync(1, options);
        await client.PrefetchQueryAsync(1, options);

        await Assert.That(fetchCount).IsEqualTo(1);
    }

    [Test]
    public async Task PrefetchQueryAsync_RefetchesWhenStale()
    {
        using var client = CreateClient(staleTime: TimeSpan.Zero);
        var fetchCount = 0;
        var allowSecondFetch = new TaskCompletionSource();

        var options = MakeOptions(
            fetcher: async (_, ct) =>
            {
                if (fetchCount > 0)
                {
                    await allowSecondFetch.Task.WaitAsync(ct);
                }

                fetchCount++;
                return "data";
            },
            staleTime: TimeSpan.Zero
        );

        await client.PrefetchQueryAsync(1, options);

        allowSecondFetch.TrySetResult();
        await client.PrefetchQueryAsync(1, options);

        await Assert.That(fetchCount).IsEqualTo(2);
    }

    [Test]
    public async Task PrefetchQueryAsync_MultipleKeys_IndependentEntries()
    {
        using var client = CreateClient();
        var fetched = new List<int>();

        var options = MakeOptions(
            fetcher: (id, _) =>
            {
                fetched.Add(id);
                return Task.FromResult($"data-{id}");
            }
        );

        await Task.WhenAll(
            client.PrefetchQueryAsync(1, options),
            client.PrefetchQueryAsync(2, options),
            client.PrefetchQueryAsync(3, options)
        );

        await Assert.That(fetched.Order().ToList()).IsEquivalentTo([1, 2, 3]);
    }

    [Test]
    public async Task PrefetchQueryAsync_CanBeCancelled()
    {
        using var client = CreateClient();
        using var cts = new CancellationTokenSource();

        var fetchStarted = new TaskCompletionSource();

        var options = MakeOptions(
            fetcher: async (_, ct) =>
            {
                fetchStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return "data";
            }
        );

        var prefetch = client.PrefetchQueryAsync(1, options, cts.Token);
        await fetchStarted.Task;
        cts.Cancel();

        await prefetch; // must not throw

        using var query = client.CreateQuery(options);
        query.SetArgs(1);

        await Assert.That(query.CurrentState.IsSuccess).IsFalse();
    }
}
