namespace DotNetQuery.Core.Tests;

public class QueryClientTests
{
    private readonly TestScheduler _scheduler = new();
    private QueryClient _sut = default!;

    [Before(Test)]
    public void Setup()
    {
        _sut = new QueryClient(new QueryClientOptions(), _scheduler);
    }

    [After(Test)]
    public void Teardown()
    {
        _sut.Dispose();
    }

    [Test]
    public async Task CreateQuery_PushingArgs_FetchesAndEmitsSuccess()
    {
        var query = _sut.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = _ => QueryKey.From("a"),
                Fetcher = (_, _) => Task.FromResult("data"),
            }
        );

        using var _ = query.State.Subscribe();
        query.Args.OnNext(0);

        var state = await query.State.Where(s => s.IsSuccess).FirstAsync();
        await Assert.That(state.CurrentData).IsEqualTo("data");
    }

    [Test]
    public async Task CreateQuery_SameKey_SharesCacheEntry()
    {
        var fetchCount = 0;
        var key = QueryKey.From("shared");
        var options = new QueryOptions<int, string>
        {
            KeyFactory = _ => key,
            Fetcher = (_, _) =>
            {
                fetchCount++;
                return Task.FromResult("data");
            },
            StaleTime = TimeSpan.FromMinutes(5),
        };

        var queryA = _sut.CreateQuery(options);
        var queryB = _sut.CreateQuery(options);

        using var subA = queryA.State.Subscribe();
        using var subB = queryB.State.Subscribe();

        queryA.Args.OnNext(0);
        await queryA.State.Where(s => s.IsSuccess).FirstAsync();

        // Push the same key to queryB — cache hit, StaleTime not elapsed, no second fetch
        queryB.Args.OnNext(0);
        await Task.Delay(50);

        await Assert.That(fetchCount).IsEqualTo(1);
    }

    [Test]
    public async Task CreateQuery_DifferentKeys_FetchIndependently()
    {
        var fetchCount = 0;
        var query = _sut.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = id => QueryKey.From("item", id),
                Fetcher = (id, _) =>
                {
                    fetchCount++;
                    return Task.FromResult(id.ToString());
                },
            }
        );

        using var _ = query.State.Subscribe();

        query.Args.OnNext(1);
        await query.State.Where(s => s.IsSuccess).FirstAsync();

        query.Args.OnNext(2);
        await query.State.Where(s => s.IsSuccess && s.CurrentData == "2").FirstAsync();

        await Assert.That(fetchCount).IsEqualTo(2);
    }

    [Test]
    public async Task CreateMutation_Execute_ReturnsSuccessState()
    {
        var mutation = _sut.CreateMutation(
            new MutationOptions<int, string> { Mutator = (args, _) => Task.FromResult(args.ToString()) }
        );

        mutation.Execute(42);
        var data = await mutation.Success.FirstAsync();

        await Assert.That(data).IsEqualTo("42");
    }

    [Test]
    public async Task CreateMutation_WithInvalidateKeys_InvalidatesQueryOnSuccess()
    {
        var key = QueryKey.From("todos");

        var query = _sut.CreateQuery(
            new QueryOptions<int, string> { KeyFactory = _ => key, Fetcher = (_, _) => Task.FromResult("data") }
        );

        using var sub = query.State.Subscribe();
        query.Args.OnNext(0);
        await query.State.Where(s => s.IsSuccess).FirstAsync();

        var mutation = _sut.CreateMutation(
            new MutationOptions<int, Unit> { Mutator = (_, _) => Task.FromResult(Unit.Default), InvalidateKeys = [key] }
        );

        var tcs = new TaskCompletionSource<QueryState<string>>();
        using var fetchSub = query.State.Where(s => s.IsFetching).Subscribe(s => tcs.TrySetResult(s));

        mutation.Execute(0);

        var state = await tcs.Task;
        await Assert.That(state.IsFetching).IsTrue();
    }

    [Test]
    public async Task Invalidate_Key_TriggersRefetchOnMatchingQuery()
    {
        var key = QueryKey.From("a");
        var query = _sut.CreateQuery(
            new QueryOptions<int, string> { KeyFactory = _ => key, Fetcher = (_, _) => Task.FromResult("data") }
        );

        using var sub = query.State.Subscribe();
        query.Args.OnNext(0);
        await query.State.Where(s => s.IsSuccess).FirstAsync();

        var tcs = new TaskCompletionSource<QueryState<string>>();
        using var fetchSub = query.State.Where(s => s.IsFetching).Subscribe(s => tcs.TrySetResult(s));

        _sut.Invalidate(key);

        var state = await tcs.Task;
        await Assert.That(state.IsFetching).IsTrue();
    }

    [Test]
    public async Task Invalidate_Key_DoesNotRefetchUnrelatedQuery()
    {
        var fetchCount = 0;
        var query = _sut.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = _ => QueryKey.From("users"),
                Fetcher = (_, _) =>
                {
                    fetchCount++;
                    return Task.FromResult("data");
                },
            }
        );

        using var _ = query.State.Subscribe();
        query.Args.OnNext(0);
        await query.State.Where(s => s.IsSuccess).FirstAsync();

        var countAfterFirstFetch = fetchCount;
        _sut.Invalidate(QueryKey.From("todos"));
        await Task.Delay(50);

        await Assert.That(fetchCount).IsEqualTo(countAfterFirstFetch);
    }

    [Test]
    public async Task Invalidate_Predicate_TriggersRefetchOnMatchingQueries()
    {
        var queryA = _sut.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = _ => QueryKey.From("todos", "A"),
                Fetcher = (_, _) => Task.FromResult("a"),
            }
        );
        var queryB = _sut.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = _ => QueryKey.From("todos", "B"),
                Fetcher = (_, _) => Task.FromResult("b"),
            }
        );

        using var subA = queryA.State.Subscribe();
        using var subB = queryB.State.Subscribe();
        queryA.Args.OnNext(0);
        queryB.Args.OnNext(0);
        await queryA.State.Where(s => s.IsSuccess).FirstAsync();
        await queryB.State.Where(s => s.IsSuccess).FirstAsync();

        var tcsA = new TaskCompletionSource<QueryState<string>>();
        var tcsB = new TaskCompletionSource<QueryState<string>>();
        using var fA = queryA.State.Where(s => s.IsFetching).Subscribe(s => tcsA.TrySetResult(s));
        using var fB = queryB.State.Where(s => s.IsFetching).Subscribe(s => tcsB.TrySetResult(s));

        _sut.Invalidate(k => k.Parts.Contains("todos"));

        using var _ = Assert.Multiple();
        await Assert.That((await tcsA.Task).IsFetching).IsTrue();
        await Assert.That((await tcsB.Task).IsFetching).IsTrue();
    }

    [Test]
    public async Task Dispose_DisposingQueryObserver_CompletesStateObservable()
    {
        var query = _sut.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = _ => QueryKey.From("a"),
                Fetcher = (_, _) => Task.FromResult("data"),
            }
        );

        using var sub = query.State.Subscribe();
        query.Args.OnNext(0);
        await query.State.Where(s => s.IsSuccess).FirstAsync();

        var completed = false;
        using var _ = query.State.Subscribe(_ => { }, () => completed = true);

        // Switch only propagates OnCompleted when BOTH the outer (_activeQuery, via observer.Dispose)
        // AND the inner (Query.State, via cache Dispose) complete.
        query.Dispose();
        _sut.Dispose();

        await Assert.That(completed).IsTrue();
    }
}
