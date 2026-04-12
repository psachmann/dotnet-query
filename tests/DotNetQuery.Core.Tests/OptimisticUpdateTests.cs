namespace DotNetQuery.Core.Tests;

public class OptimisticUpdateTests
{
    private readonly TestScheduler _scheduler = new();
    private QueryClient _client = default!;

    private static readonly QueryInstrumentation _instrumentation = new(NullLogger.Instance);

    [Before(Test)]
    public void Setup() => _client = new QueryClient(new QueryClientOptions(), _scheduler, _instrumentation);

    [After(Test)]
    public void Teardown() => _client.Dispose();

    private static readonly QueryKey TodosKey = QueryKey.From("todos");

    private IQuery<int, string> CreateQuery(
        Func<int, CancellationToken, Task<string>>? fetcher = null,
        TimeSpan? staleTime = null
    )
    {
        return _client.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = _ => TodosKey,
                Fetcher = fetcher ?? ((_, _) => Task.FromResult("real")),
                StaleTime = staleTime,
            }
        );
    }

    private IMutation<string, string> CreateMutation(
        Func<string, CancellationToken, Task<string>>? mutator = null,
        Func<string, Action?>? onMutate = null,
        Action<string, string>? onSuccess = null,
        Action<Exception>? onFailure = null,
        Action? onSettled = null
    )
    {
        return _client.CreateMutation(
            new MutationOptions<string, string>
            {
                Mutator = mutator ?? ((args, _) => Task.FromResult(args)),
                OnMutate = onMutate,
                OnSuccess = onSuccess,
                OnFailure = onFailure,
                OnSettled = onSettled,
            }
        );
    }

    // ── SetQueryData / GetQueryData ──────────────────────────────────────────

    [Test]
    public async Task SetQueryData_UpdatesCachedQueryState()
    {
        var query = CreateQuery(staleTime: TimeSpan.FromHours(1));

        // Populate the cache with an initial fetch
        var settled = new TaskCompletionSource();
        using var sub = query.Settled.Subscribe(_ => settled.TrySetResult());
        query.SetArgs(0);
        await settled.Task;

        // Imperatively overwrite the cached value
        _client.SetQueryData(TodosKey, "overwritten");

        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("overwritten");
    }

    [Test]
    public async Task SetQueryData_BroadcastsToAllObserversOnSameKey()
    {
        var staleTime = TimeSpan.FromHours(1);
        var queryA = CreateQuery(staleTime: staleTime);
        var queryB = CreateQuery(staleTime: staleTime);

        var settledA = new TaskCompletionSource();
        var settledB = new TaskCompletionSource();
        using var subA = queryA.Settled.Subscribe(_ => settledA.TrySetResult());
        using var subB = queryB.Settled.Subscribe(_ => settledB.TrySetResult());

        queryA.SetArgs(0);
        queryB.SetArgs(0);
        await Task.WhenAll(settledA.Task, settledB.Task);

        _client.SetQueryData(TodosKey, "broadcast");

        using var _ = Assert.Multiple();
        await Assert.That(queryA.CurrentState.CurrentData).IsEqualTo("broadcast");
        await Assert.That(queryB.CurrentState.CurrentData).IsEqualTo("broadcast");
    }

    [Test]
    public async Task GetQueryData_ReturnsCachedValue()
    {
        var query = CreateQuery(staleTime: TimeSpan.FromHours(1));

        var settled = new TaskCompletionSource();
        using var sub = query.Settled.Subscribe(_ => settled.TrySetResult());
        query.SetArgs(0);
        await settled.Task;

        var data = _client.GetQueryData<string>(TodosKey);

        await Assert.That(data).IsEqualTo("real");
    }

    [Test]
    public async Task GetQueryData_ReturnsDefault_WhenKeyMissing()
    {
        var data = _client.GetQueryData<string>(QueryKey.From("nonexistent"));

        await Assert.That(data).IsNull();
    }

    // ── OnMutate ─────────────────────────────────────────────────────────────

    [Test]
    public async Task OnMutate_CalledBeforeRunningState()
    {
        var order = new List<string>();

        var mutation = CreateMutation(
            mutator: (args, _) => Task.FromResult(args),
            onMutate: _ =>
            {
                order.Add("onMutate");
                return null;
            }
        );

        using var sub = mutation.State.Where(s => s.IsRunning).Subscribe(_ => order.Add("running"));

        var settled = new TaskCompletionSource();
        using var settledSub = mutation.Settled.Subscribe(_ => settled.TrySetResult());

        mutation.Execute("x");
        await settled.Task;

        await Assert.That(order).IsEquivalentTo(["onMutate", "running"]);
    }

    [Test]
    public async Task OnMutate_RollbackNotCalled_OnSuccess()
    {
        var rollbackCalled = false;

        var mutation = CreateMutation(
            mutator: (args, _) => Task.FromResult(args),
            onMutate: _ => () => rollbackCalled = true
        );

        var settled = new TaskCompletionSource();
        using var sub = mutation.Settled.Subscribe(_ => settled.TrySetResult());

        mutation.Execute("ok");
        await settled.Task;

        await Assert.That(rollbackCalled).IsFalse();
    }

    [Test]
    public async Task OnMutate_RollbackCalled_OnFailure()
    {
        var rollbackCalled = false;

        var mutation = CreateMutation(
            mutator: (_, _) => Task.FromException<string>(new InvalidOperationException("fail")),
            onMutate: _ => () => rollbackCalled = true
        );

        var failed = new TaskCompletionSource();
        using var sub = mutation.Failure.Subscribe(_ => failed.TrySetResult());

        mutation.Execute("x");
        await failed.Task;

        await Assert.That(rollbackCalled).IsTrue();
    }

    [Test]
    public async Task OnMutate_RollbackCalled_OnCancellation()
    {
        var rollbackCalled = false;
        var mutatorStarted = new TaskCompletionSource();

        var mutation = CreateMutation(
            mutator: async (_, ct) =>
            {
                mutatorStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return "";
            },
            onMutate: _ => () => rollbackCalled = true
        );

        var settledCount = 0;
        var secondSettled = new TaskCompletionSource();
        using var sub = mutation.State.Where(s => s.IsIdle).Subscribe(_ =>
        {
            settledCount++;
            if (settledCount == 2) secondSettled.TrySetResult();
        });

        mutation.Execute("x");
        await mutatorStarted.Task;

        // Supersede with a new execution — cancels the in-flight one
        mutation.Execute("y");
        await secondSettled.Task;

        await Assert.That(rollbackCalled).IsTrue();
    }

    [Test]
    public async Task OnMutate_NullRollback_DoesNotThrow()
    {
        var mutation = CreateMutation(
            mutator: (args, _) => Task.FromResult(args),
            onMutate: _ => null // no rollback
        );

        var settled = new TaskCompletionSource();
        using var sub = mutation.Settled.Subscribe(_ => settled.TrySetResult());

        mutation.Execute("ok");
        await settled.Task; // should not throw
    }

    // ── Full optimistic update flow ───────────────────────────────────────────

    [Test]
    public async Task OptimisticUpdate_FullFlow_RollsBackOnFailure()
    {
        var query = CreateQuery(
            fetcher: (_, _) => Task.FromResult("original"),
            staleTime: TimeSpan.FromHours(1)
        );

        // Populate cache with initial fetch
        var firstSettled = new TaskCompletionSource();
        using var querySub = query.Settled.Subscribe(_ => firstSettled.TrySetResult());
        query.SetArgs(0);
        await firstSettled.Task;

        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("original");

        var allowMutatorToFail = new TaskCompletionSource();

        var mutation = CreateMutation(
            mutator: async (_, ct) =>
            {
                // Yield so the optimistic state can be observed before the failure propagates
                await allowMutatorToFail.Task.WaitAsync(ct);
                throw new InvalidOperationException("server error");
            },
            onMutate: args =>
            {
                var snapshot = _client.GetQueryData<string>(TodosKey);
                _client.SetQueryData(TodosKey, $"optimistic:{args}");
                return () => _client.SetQueryData(TodosKey, snapshot);
            }
        );

        var mutationFailed = new TaskCompletionSource();
        using var failSub = mutation.Failure.Subscribe(_ => mutationFailed.TrySetResult());

        mutation.Execute("new-item");

        // OnMutate is synchronous and runs before the mutator awaits, so the
        // optimistic value is already in the cache by the time Execute returns.
        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("optimistic:new-item");

        // Let the mutator fail and trigger the rollback
        allowMutatorToFail.TrySetException(new InvalidOperationException("server error"));
        await mutationFailed.Task;

        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("original");
    }
}
