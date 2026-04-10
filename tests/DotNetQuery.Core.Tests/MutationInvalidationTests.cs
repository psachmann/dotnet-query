namespace DotNetQuery.Core.Tests;

public class MutationInvalidationTests
{
    private readonly TestScheduler _scheduler = new();
    private QueryClient _client = default!;

    [Before(Test)]
    public void Setup()
    {
        _client = new(new(), _scheduler);
    }

    [After(Test)]
    public void Teardown()
    {
        _client.Dispose();
    }

    [Test]
    public async Task CreateMutation_OnSuccess_IsCalledWithArgsOnSuccess()
    {
        int? capturedArgs = null;
        var mutation = _client.CreateMutation(
            new MutationOptions<int, Unit>
            {
                Mutator = (_, _) => Task.FromResult(Unit.Default),
                OnSuccess = (args, _) => capturedArgs = args,
            }
        );

        mutation.Execute(42);
        await mutation.Success.FirstAsync();

        await Assert.That(capturedArgs).IsEqualTo(42);
    }

    [Test]
    public async Task CreateMutation_OnSuccess_IsNotCalledOnFailure()
    {
        var called = false;
        var mutation = _client.CreateMutation(
            new MutationOptions<int, Unit>
            {
                Mutator = (_, _) => Task.FromException<Unit>(new Exception("fail")),
                RetryHandler = new NoRetryHandler(),
                OnSuccess = (_, _) => called = true,
            }
        );

        mutation.Execute(0);
        await mutation.Failure.FirstAsync();

        await Assert.That(called).IsFalse();
    }

    [Test]
    public async Task CreateMutation_InvalidateKeys_InvalidatesMatchingQueryOnSuccess()
    {
        var key = QueryKey.From("todos");
        var query = _client.CreateQuery(
            new QueryOptions<int, string> { KeyFactory = _ => key, Fetcher = (_, _) => Task.FromResult("data") }
        );

        query.Args.OnNext(0);
        await query.Success.FirstAsync();

        var mutation = _client.CreateMutation(
            new MutationOptions<int, Unit> { Mutator = (_, _) => Task.FromResult(Unit.Default), InvalidateKeys = [key] }
        );

        var fetchingTask = query.State.Where(s => s.IsFetching).FirstAsync();

        mutation.Execute(0);

        await fetchingTask;
    }

    [Test]
    public async Task CreateMutation_InvalidateKeys_DoesNotInvalidateOnFailure()
    {
        var key = QueryKey.From("todos");
        var fetchCount = 0;
        var query = _client.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = _ => key,
                Fetcher = (_, _) =>
                {
                    fetchCount++;
                    return Task.FromResult("data");
                },
            }
        );

        query.Args.OnNext(0);
        await query.Success.FirstAsync();

        var countAfterFirstFetch = fetchCount;
        var mutation = _client.CreateMutation(
            new MutationOptions<int, Unit>
            {
                Mutator = (_, _) => Task.FromException<Unit>(new Exception("fail")),
                RetryHandler = new NoRetryHandler(),
                InvalidateKeys = [key],
            }
        );

        mutation.Execute(0);
        await mutation.Failure.FirstAsync();

        await Assert.That(fetchCount).IsEqualTo(countAfterFirstFetch);
    }

    [Test]
    public async Task CreateMutation_BothInvalidateKeysAndOnSuccess_BothFireOnSuccess()
    {
        var key = QueryKey.From("todos");
        var onSuccessFired = false;

        var query = _client.CreateQuery(
            new QueryOptions<int, string> { KeyFactory = _ => key, Fetcher = (_, _) => Task.FromResult("data") }
        );

        query.Args.OnNext(0);
        await query.Success.FirstAsync();

        var mutation = _client.CreateMutation(
            new MutationOptions<int, Unit>
            {
                Mutator = (_, _) => Task.FromResult(Unit.Default),
                InvalidateKeys = [key],
                OnSuccess = (_, _) => onSuccessFired = true,
            }
        );

        var fetchingTask = query.State.Where(s => s.IsFetching).FirstAsync();

        mutation.Execute(0);
        await mutation.Success.FirstAsync();
        await fetchingTask;

        await Assert.That(onSuccessFired).IsTrue();
    }

    [Test]
    public async Task CreateMutation_InvalidateKeys_DoesNotInvalidateAfterMutationDisposed()
    {
        var key = QueryKey.From("todos");
        var fetchCount = 0;
        var mutatorTcs = new TaskCompletionSource<Unit>();

        var query = _client.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = _ => key,
                Fetcher = (_, _) =>
                {
                    fetchCount++;
                    return Task.FromResult("data");
                },
            }
        );

        query.Args.OnNext(0);
        await query.Success.FirstAsync();
        var countAfterFirstFetch = fetchCount;

        var mutation = _client.CreateMutation(
            new MutationOptions<int, Unit>
            {
                Mutator = (_, _) => mutatorTcs.Task,
                InvalidateKeys = [key],
            }
        );

        mutation.Execute(0);     // in-flight
        mutation.Dispose();      // disposes the invalidation subscription
        mutatorTcs.SetResult(Unit.Default); // mutator completes after dispose

        // Give the pipeline a tick to process
        await Task.Delay(50);

        await Assert.That(fetchCount).IsEqualTo(countAfterFirstFetch);
    }
}
