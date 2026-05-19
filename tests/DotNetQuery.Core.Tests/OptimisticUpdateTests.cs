namespace DotNetQuery.Core.Tests;

public class OptimisticUpdateTests
{
    private static readonly QueryInstrumentation _instrumentation = new(NullLogger.Instance);

    private IQueryClient CreateClient(TimeSpan? staleTime = null) =>
        QueryClientFactory.Create(new QueryClientOptions { StaleTime = staleTime ?? TimeSpan.FromMinutes(5) });

    private QueryOptions<int, string> MakeQueryOptions(Func<int, CancellationToken, Task<string>>? fetcher = null) =>
        new()
        {
            KeyFactory = id => QueryKey.From("item", id),
            Fetcher = fetcher ?? ((_, _) => Task.FromResult("server-data")),
            StaleTime = TimeSpan.FromMinutes(5),
        };

    [Test]
    public async Task SetData_UpdatesActiveEntry_ToSuccess()
    {
        using var client = CreateClient();
        using var query = client.CreateQuery(MakeQueryOptions());
        query.SetArgs(1);

        query.SetData("optimistic");

        using var _ = Assert.Multiple();
        await Assert.That(query.CurrentState.IsSuccess).IsTrue();
        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("optimistic");
    }

    [Test]
    public async Task SetData_MarksCacheAsFresh_SoSubsequentFetchIsSkipped()
    {
        var fetchCount = 0;
        using var client = CreateClient();
        using var query = client.CreateQuery(
            MakeQueryOptions(
                fetcher: (_, _) =>
                {
                    fetchCount++;
                    return Task.FromResult("server-data");
                }
            )
        );

        query.SetArgs(1);
        query.SetData("optimistic");

        // Subscribe — should not trigger a fetch because SetData stamped the entry as fresh
        var states = new List<QueryState<string>>();
        using var sub = query.State.Subscribe(s => states.Add(s));

        await Task.Delay(50);

        await Assert.That(fetchCount).IsEqualTo(0);
        await Assert.That(states.All(s => s.IsSuccess)).IsTrue();
    }

    [Test]
    public async Task SetData_IsVisibleToSecondObserver_SharingTheSameCacheEntry()
    {
        using var client = CreateClient();
        var options = MakeQueryOptions();

        using var writer = client.CreateQuery(options);
        writer.SetArgs(1);
        writer.SetData("optimistic");

        using var reader = client.CreateQuery(options);
        reader.SetArgs(1);

        using var _ = Assert.Multiple();
        await Assert.That(reader.CurrentState.IsSuccess).IsTrue();
        await Assert.That(reader.CurrentState.CurrentData).IsEqualTo("optimistic");
    }

    [Test]
    public async Task SetData_EmitsStateTransition_ToSubscribers()
    {
        using var client = CreateClient();
        using var query = client.CreateQuery(MakeQueryOptions());
        query.SetArgs(1);

        var states = new List<QueryState<string>>();
        using var sub = query.State.Subscribe(s => states.Add(s));

        query.SetData("optimistic");

        await Assert.That(states.Last().CurrentData).IsEqualTo("optimistic");
    }

    [Test]
    public async Task SetData_BeforeSetArgs_IsNoOp()
    {
        using var client = CreateClient();
        using var query = client.CreateQuery(MakeQueryOptions());

        // No args set yet — SetData should not throw, just be silently ignored
        query.SetData("optimistic");

        await Assert.That(query.CurrentState.IsIdle).IsTrue();
    }

    [Test]
    public async Task OnMutate_IsCalledWithArgs_BeforeMutatorRuns()
    {
        var callOrder = new List<string>();
        using var client = CreateClient();

        var mutation = client.CreateMutation(
            new MutationOptions<int, string>
            {
                OnMutate = _ => callOrder.Add("onMutate"),
                Mutator = (_, _) =>
                {
                    callOrder.Add("mutator");
                    return Task.FromResult("ok");
                },
            }
        );

        mutation.Execute(1);
        await mutation.Success.FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(callOrder[0]).IsEqualTo("onMutate");
        await Assert.That(callOrder[1]).IsEqualTo("mutator");
    }

    [Test]
    public async Task OnMutate_ReceivesCorrectArgs()
    {
        int? captured = null;
        using var client = CreateClient();

        var mutation = client.CreateMutation(
            new MutationOptions<int, string>
            {
                OnMutate = args => captured = args,
                Mutator = (_, _) => Task.FromResult("ok"),
            }
        );

        mutation.Execute(42);
        await mutation.Success.FirstAsync();

        await Assert.That(captured).IsEqualTo(42);
    }

    [Test]
    public async Task OptimisticUpdate_OnSuccess_LeavesOptimisticDataUntilInvalidation()
    {
        using var client = CreateClient();
        var options = MakeQueryOptions();
        using var query = client.CreateQuery(options);
        query.SetArgs(1);

        string? snapshot = null;

        var mutation = client.CreateMutation(
            new MutationOptions<int, string>
            {
                OnMutate = _ =>
                {
                    snapshot = query.CurrentState.CurrentData;
                    query.Cancel();
                    query.SetData("optimistic");
                },
                Mutator = (_, _) => Task.FromResult("server-confirmed"),
            }
        );

        mutation.Execute(1);
        await mutation.Success.FirstAsync();

        // After success the optimistic data is still in the cache (invalidation would replace it)
        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("optimistic");
    }

    [Test]
    public async Task OptimisticUpdate_OnFailure_RollsBackToSnapshot()
    {
        using var client = CreateClient();
        var options = MakeQueryOptions(fetcher: (_, _) => Task.FromResult("original"));
        using var query = client.CreateQuery(options);

        // Warm the cache with real data first
        await query.PrefetchAsync(1);
        query.SetArgs(1);

        string? snapshot = null;

        var mutation = client.CreateMutation(
            new MutationOptions<int, string>
            {
                OnMutate = _ =>
                {
                    snapshot = query.CurrentState.CurrentData;
                    query.Cancel();
                    query.SetData("optimistic");
                },
                Mutator = (_, _) => Task.FromException<string>(new Exception("network error")),
                OnFailure = _ =>
                {
                    if (snapshot is not null)
                        query.SetData(snapshot);
                },
                RetryHandler = new DefaultRetryHandler(),
            }
        );

        mutation.Execute(1);
        await mutation.Failure.FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(snapshot).IsEqualTo("original");
        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("original");
    }
}
