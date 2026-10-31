using System.Reactive.Threading.Tasks;
using TUnit.Assertions.Enums;

namespace DotNetQuery.Core.Tests;

public class MutationProgressTests
{
    private readonly TestScheduler _scheduler = new();
    private QueryClient _client = default!;

    private static readonly QueryInstrumentation _instrumentation = new(NullLogger.Instance);

    [Before(Test)]
    public void Setup()
    {
        _client = new(new(), _scheduler, _instrumentation);
    }

    [After(Test)]
    public void Teardown()
    {
        _client.Dispose();
    }

    [Test]
    public async Task InitialState_IsIdle_WithDefaultProgress()
    {
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string, long> { Mutator = (_, _, _) => Task.FromResult("ok") }
        );

        using var _ = Assert.Multiple();
        await Assert.That(mutation.CurrentState.IsIdle).IsTrue();
        await Assert.That(mutation.CurrentState.Progress).IsEqualTo(0L);
    }

    [Test]
    public async Task Execute_BeforeFirstReport_IsRunningWithDefaultProgress()
    {
        var tcs = new TaskCompletionSource<string>();
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string, long> { Mutator = (_, _, _) => tcs.Task }
        );

        mutation.Execute(0);

        using var _ = Assert.Multiple();
        await Assert.That(mutation.CurrentState.IsRunning).IsTrue();
        await Assert.That(mutation.CurrentState.Progress).IsEqualTo(0L);
        tcs.SetResult("done");
    }

    [Test]
    public async Task Report_EmitsRunningStateWithEachValue_InOrder()
    {
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string, long>
            {
                Mutator = (_, progress, _) =>
                {
                    progress.Report(10);
                    progress.Report(20);
                    progress.Report(30);
                    return Task.FromResult("ok");
                },
            }
        );
        var states = new List<MutationState<string, long>>();
        using var subscription = mutation.State.Subscribe(states.Add);

        mutation.Execute(0);
        await mutation.Success.FirstAsync();

        var running = states.Where(s => s.IsRunning).Select(s => s.Progress).ToList();
        await Assert.That(running).IsEquivalentTo(new long[] { 0, 10, 20, 30 }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Report_UpdatesCurrentStateSynchronously()
    {
        IProgress<long>? captured = null;
        var tcs = new TaskCompletionSource<string>();
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string, long>
            {
                Mutator = (_, progress, _) =>
                {
                    captured = progress;
                    return tcs.Task;
                },
            }
        );

        mutation.Execute(0);
        captured!.Report(42);

        using var _ = Assert.Multiple();
        await Assert.That(mutation.CurrentState.IsRunning).IsTrue();
        await Assert.That(mutation.CurrentState.Progress).IsEqualTo(42L);
        tcs.SetResult("done");
    }

    [Test]
    public async Task Success_KeepsLastReportedProgress()
    {
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string, long>
            {
                Mutator = (_, progress, _) =>
                {
                    progress.Report(100);
                    return Task.FromResult("ok");
                },
            }
        );

        mutation.Execute(0);
        var state = await mutation.State.Where(s => s.IsSuccess).FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(state.CurrentData).IsEqualTo("ok");
        await Assert.That(state.Progress).IsEqualTo(100L);
    }

    [Test]
    public async Task Failure_KeepsLastReportedProgress()
    {
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string, double>
            {
                Mutator = (_, progress, _) =>
                {
                    progress.Report(0.6);
                    return Task.FromException<string>(new InvalidOperationException("boom"));
                },
            }
        );

        mutation.Execute(0);
        var state = await mutation.State.Where(s => s.IsFailure).FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(state.Error).IsTypeOf<InvalidOperationException>();
        await Assert.That(state.Progress).IsEqualTo(0.6);
    }

    [Test]
    public async Task Cancel_ReturnsToIdle_AndClearsProgress()
    {
        IProgress<long>? captured = null;
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string, long>
            {
                Mutator = async (_, progress, ct) =>
                {
                    captured = progress;
                    progress.Report(50);
                    await Task.Delay(Timeout.Infinite, ct);
                    return "never";
                },
            }
        );

        mutation.Execute(0);
        var idle = mutation.State.Skip(1).Where(s => s.IsIdle).FirstAsync().ToTask();
        mutation.Cancel();
        var state = await idle;

        using var _ = Assert.Multiple();
        await Assert.That(state.Progress).IsEqualTo(0L);
        await Assert.That(mutation.CurrentState.IsIdle).IsTrue();
    }

    [Test]
    public async Task Report_AfterCancel_IsIgnored()
    {
        IProgress<long>? captured = null;
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string, long>
            {
                Mutator = async (_, progress, ct) =>
                {
                    captured = progress;
                    await Task.Delay(Timeout.Infinite, ct);
                    return "never";
                },
            }
        );

        mutation.Execute(0);
        var idle = mutation.State.Skip(1).Where(s => s.IsIdle).FirstAsync().ToTask();
        mutation.Cancel();
        await idle;
        captured!.Report(100);

        using var _ = Assert.Multiple();
        await Assert.That(mutation.CurrentState.IsIdle).IsTrue();
        await Assert.That(mutation.CurrentState.Progress).IsEqualTo(0L);
    }

    [Test]
    public async Task Report_AfterCancelRequested_IsIgnored_EvenIfMutatorIgnoresToken()
    {
        // A mutator that ignores its token keeps running after Cancel(), so the run has not settled
        // yet; its reports must still stop the moment cancellation is requested.
        IProgress<long>? captured = null;
        var release = new TaskCompletionSource<string>();
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string, long>
            {
                Mutator = (_, progress, _) =>
                {
                    captured = progress;
                    return release.Task;
                },
            }
        );

        mutation.Execute(0);
        captured!.Report(50);
        mutation.Cancel();
        captured.Report(99);

        await Assert.That(mutation.CurrentState.Progress).IsEqualTo(50L);
        release.SetResult("late");
    }

    [Test]
    public async Task Report_AfterSuccess_IsIgnored()
    {
        IProgress<long>? captured = null;
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string, long>
            {
                Mutator = (_, progress, _) =>
                {
                    captured = progress;
                    progress.Report(10);
                    return Task.FromResult("ok");
                },
            }
        );

        mutation.Execute(0);
        await mutation.Success.FirstAsync();
        captured!.Report(99);

        using var _ = Assert.Multiple();
        await Assert.That(mutation.CurrentState.IsSuccess).IsTrue();
        await Assert.That(mutation.CurrentState.Progress).IsEqualTo(10L);
    }

    [Test]
    public async Task Retry_ResetsProgressAtStartOfEachAttempt()
    {
        var attempt = 0;
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string, long>
            {
                RetryHandler = new RetryOnFailureHandler(maxAttempts: 2),
                Mutator = (_, progress, _) =>
                {
                    if (++attempt == 1)
                    {
                        progress.Report(60);
                        return Task.FromException<string>(new Exception("transient"));
                    }

                    progress.Report(5);
                    return Task.FromResult("ok");
                },
            }
        );
        var states = new List<MutationState<string, long>>();
        using var subscription = mutation.State.Subscribe(states.Add);

        mutation.Execute(0);
        var success = await mutation.State.Where(s => s.IsSuccess).FirstAsync();

        var running = states.Where(s => s.IsRunning).Select(s => s.Progress).ToList();

        using var _ = Assert.Multiple();
        await Assert.That(running).IsEquivalentTo(new long[] { 0, 60, 0, 5 }, CollectionOrdering.Matching);
        await Assert.That(success.Progress).IsEqualTo(5L);
    }

    [Test]
    public async Task Report_FromPreviousAttempt_IsIgnored()
    {
        IProgress<long>? firstAttempt = null;
        var secondAttemptStarted = new TaskCompletionSource();
        var finish = new TaskCompletionSource<string>();
        var attempt = 0;
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string, long>
            {
                RetryHandler = new RetryOnFailureHandler(maxAttempts: 2),
                Mutator = (_, progress, _) =>
                {
                    if (++attempt == 1)
                    {
                        firstAttempt = progress;
                        return Task.FromException<string>(new Exception("transient"));
                    }

                    secondAttemptStarted.SetResult();
                    return finish.Task;
                },
            }
        );

        mutation.Execute(0);
        await secondAttemptStarted.Task;
        firstAttempt!.Report(99);

        await Assert.That(mutation.CurrentState.Progress).IsEqualTo(0L);
        finish.SetResult("ok");
    }

    [Test]
    public async Task Report_FromSupersededExecution_IsIgnored()
    {
        var progressByArgs = new Dictionary<int, IProgress<long>>();
        var firstSettled = new TaskCompletionSource();
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string, long>
            {
                Mutator = async (args, progress, ct) =>
                {
                    progressByArgs[args] = progress;
                    await Task.Delay(Timeout.Infinite, ct);
                    return "never";
                },
                OnSettled = () => firstSettled.TrySetResult(),
            }
        );

        mutation.Execute(1);
        mutation.Execute(2);
        await firstSettled.Task;
        progressByArgs[1].Report(99);
        progressByArgs[2].Report(7);

        using var _ = Assert.Multiple();
        await Assert.That(mutation.CurrentState.IsRunning).IsTrue();
        await Assert.That(mutation.CurrentState.Progress).IsEqualTo(7L);
        mutation.Cancel();
    }

    [Test]
    public async Task Report_FromBackgroundThreads_NeverFollowsTerminalState()
    {
        var reports = 0;
        var stop = false;
        Task? reporter = null;
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string, int>
            {
                Mutator = async (_, progress, _) =>
                {
                    // Keeps reporting from the thread pool after the mutator has returned.
                    reporter = Task.Run(() =>
                    {
                        while (!Volatile.Read(ref stop))
                        {
                            progress.Report(Interlocked.Increment(ref reports));
                        }
                    });

                    while (Volatile.Read(ref reports) < 100)
                    {
                        await Task.Yield();
                    }

                    return "ok";
                },
            }
        );
        var states = new List<MutationState<string, int>>();
        using var subscription = mutation.State.Subscribe(states.Add);

        mutation.Execute(0);
        await mutation.Success.FirstAsync();
        var reportsAtSuccess = Volatile.Read(ref reports);

        while (Volatile.Read(ref reports) < reportsAtSuccess + 1_000)
        {
            await Task.Yield();
        }

        Volatile.Write(ref stop, true);
        await reporter!;

        var successIndex = states.FindIndex(s => s.IsSuccess);
        var running = states.Where(s => s.IsRunning).Select(s => s.Progress).ToList();

        using var _ = Assert.Multiple();
        await Assert.That(successIndex).IsEqualTo(states.Count - 1);
        await Assert.That(running).IsOrderedBy(p => p);
    }

    [Test]
    public async Task InvalidateKeys_InvalidatesAfterSuccess()
    {
        var fetches = 0;
        var query = _client.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = _ => QueryKey.From("inbox"),
                Fetcher = (_, _) => Task.FromResult($"fetch {Interlocked.Increment(ref fetches)}"),
            }
        );
        using var querySubscription = query.State.Subscribe(_ => { });
        query.SetArgs(0);
        await query.Success.Where(data => data == "fetch 1").FirstAsync();

        var mutation = _client.CreateMutation(
            new MutationOptions<int, string, long>
            {
                Mutator = (_, progress, _) =>
                {
                    progress.Report(1);
                    return Task.FromResult("uploaded");
                },
                InvalidateKeys = [QueryKey.From("inbox")],
            }
        );

        mutation.Execute(0);
        var refetched = await query.Success.Where(data => data == "fetch 2").FirstAsync();

        await Assert.That(refetched).IsEqualTo("fetch 2");
    }

    [Test]
    public async Task Callbacks_RunInOrder_WithProgressMutator()
    {
        var calls = new List<string>();
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string, long>
            {
                Mutator = (_, progress, _) =>
                {
                    calls.Add("mutator");
                    progress.Report(1);
                    return Task.FromResult("ok");
                },
                OnMutate = _ => calls.Add("mutate"),
                OnSuccess = (_, _) => calls.Add("success"),
                OnSettled = () => calls.Add("settled"),
            }
        );
        var settled = new TaskCompletionSource();
        using var subscription = mutation.Settled.Subscribe(_ => settled.TrySetResult());

        mutation.Execute(0);
        await settled.Task;

        // OnSuccess / OnSettled run after the Success state is published, so wait for them too.
        while (calls.Count < 4)
        {
            await Task.Yield();
        }

        await Assert
            .That(calls)
            .IsEquivalentTo(new[] { "mutate", "mutator", "success", "settled" }, CollectionOrdering.Matching);
    }

    private sealed class RetryOnFailureHandler(int maxAttempts) : IRetryHandler
    {
        public async Task<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, Task<TResult>> action,
            CancellationToken cancellationToken = default
        )
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await action(cancellationToken);
                }
                catch (Exception) when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
                {
                    // retry
                }
            }
        }
    }
}
