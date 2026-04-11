namespace DotNetQuery.Core.Tests;

public class MutationTests
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
    public async Task InitialState_IsIdle()
    {
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string> { Mutator = (_, _) => Task.FromResult("ok") }
        );

        var state = await mutation.State.FirstAsync();

        await Assert.That(state.IsIdle).IsTrue();
    }

    [Test]
    public async Task Execute_TransitionsToRunning()
    {
        var tcs = new TaskCompletionSource<string>();
        var mutation = _client.CreateMutation(new MutationOptions<int, string> { Mutator = (_, ct) => tcs.Task });

        var runningTask = mutation.State.Where(s => s.IsRunning).FirstAsync();
        mutation.Execute(0);

        var state = await runningTask;

        await Assert.That(state.IsRunning).IsTrue();
        tcs.SetResult("done");
    }

    [Test]
    public async Task Execute_OnSuccess_TransitionsToSuccess()
    {
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string> { Mutator = (_, _) => Task.FromResult("result") }
        );

        mutation.Execute(0);
        var state = await mutation.State.Where(s => s.IsSuccess).FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(state.IsSuccess).IsTrue();
        await Assert.That(state.CurrentData).IsEqualTo("result");
        await Assert.That(state.HasData).IsTrue();
    }

    [Test]
    public async Task Execute_OnFailure_TransitionsToFailure()
    {
        var error = new Exception("bang");
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string>
            {
                Mutator = (_, _) => Task.FromException<string>(error),
                RetryHandler = new DefaultRetryHandler(),
            }
        );

        mutation.Execute(0);
        var state = await mutation.State.Where(s => s.IsFailure).FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(state.IsFailure).IsTrue();
        await Assert.That(state.Error).IsEqualTo(error);
        await Assert.That(state.HasError).IsTrue();
        await Assert.That(state.HasData).IsFalse();
    }

    [Test]
    public async Task Execute_WhenDisabled_DoesNotRun()
    {
        var called = false;
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string>
            {
                Mutator = (_, _) =>
                {
                    called = true;
                    return Task.FromResult("ok");
                },
                IsEnabled = false,
            }
        );

        mutation.Execute(0);
        await Task.Delay(50);

        await Assert.That(called).IsFalse();
    }

    [Test]
    public async Task IsEnabled_WhenToggledFalse_SubsequentExecuteIsIgnored()
    {
        var called = false;
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string>
            {
                Mutator = (_, _) =>
                {
                    called = true;
                    return Task.FromResult("ok");
                },
            }
        );

        mutation.SetEnabled(false);
        mutation.Execute(0);
        await Task.Delay(50);

        await Assert.That(called).IsFalse();
    }

    [Test]
    public async Task IsEnabled_WhenToggledTrueAfterFalse_ExecuteRuns()
    {
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string> { Mutator = (_, _) => Task.FromResult("ok"), IsEnabled = false }
        );

        mutation.SetEnabled(true);
        mutation.Execute(0);

        var state = await mutation.State.Where(s => s.IsSuccess).FirstAsync();

        await Assert.That(state.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Cancel_WhileRunning_ReturnsToIdle()
    {
        var tcs = new TaskCompletionSource<string>();
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string>
            {
                Mutator = async (_, ct) =>
                {
                    await tcs.Task.WaitAsync(ct);
                    return "ok";
                },
            }
        );

        mutation.Execute(0);
        await mutation.State.Where(s => s.IsRunning).FirstAsync();

        mutation.Cancel();

        var state = await mutation.State.Where(s => s.IsIdle).FirstAsync();

        await Assert.That(state.IsIdle).IsTrue();
    }

    [Test]
    public async Task Success_Observable_EmitsData()
    {
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string> { Mutator = (_, _) => Task.FromResult("data") }
        );

        mutation.Execute(0);
        var data = await mutation.Success.FirstAsync();

        await Assert.That(data).IsEqualTo("data");
    }

    [Test]
    public async Task Failure_Observable_EmitsError()
    {
        var error = new Exception("oops");
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string>
            {
                Mutator = (_, _) => Task.FromException<string>(error),
                RetryHandler = new DefaultRetryHandler(),
            }
        );

        mutation.Execute(0);
        var emitted = await mutation.Failure.FirstAsync();

        await Assert.That(emitted).IsEqualTo(error);
    }

    [Test]
    public async Task Settled_Observable_EmitsOnSuccess()
    {
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string> { Mutator = (_, _) => Task.FromResult("ok") }
        );

        mutation.Execute(0);
        var state = await mutation.Settled.FirstAsync();

        await Assert.That(state.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Settled_Observable_EmitsOnFailure()
    {
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string>
            {
                Mutator = (_, _) => Task.FromException<string>(new Exception("fail")),
                RetryHandler = new DefaultRetryHandler(),
            }
        );

        mutation.Execute(0);
        var state = await mutation.Settled.FirstAsync();

        await Assert.That(state.IsFailure).IsTrue();
    }

    [Test]
    public async Task OnFailure_Callback_IsCalledWithError()
    {
        Exception? captured = null;
        var error = new Exception("boom");
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string>
            {
                Mutator = (_, _) => Task.FromException<string>(error),
                RetryHandler = new DefaultRetryHandler(),
                OnFailure = e => captured = e,
            }
        );

        mutation.Execute(0);
        await mutation.Failure.FirstAsync();

        await Assert.That(captured).IsEqualTo(error);
    }

    [Test]
    public async Task OnSettled_Callback_IsCalledOnSuccess()
    {
        var settled = false;
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string>
            {
                Mutator = (_, _) => Task.FromResult("ok"),
                OnSettled = () => settled = true,
            }
        );

        mutation.Execute(0);
        await mutation.Settled.FirstAsync();

        await Assert.That(settled).IsTrue();
    }

    [Test]
    public async Task OnSettled_Callback_IsCalledOnFailure()
    {
        var settled = false;
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string>
            {
                Mutator = (_, _) => Task.FromException<string>(new Exception("fail")),
                RetryHandler = new DefaultRetryHandler(),
                OnSettled = () => settled = true,
            }
        );

        mutation.Execute(0);
        await mutation.Settled.FirstAsync();

        await Assert.That(settled).IsTrue();
    }

    [Test]
    public async Task Execute_WhileAlreadyRunning_CancelsPreviousExecution()
    {
        var firstStarted = new TaskCompletionSource();
        var completions = new List<string>();
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string>
            {
                Mutator = async (args, ct) =>
                {
                    if (args == 1)
                        firstStarted.TrySetResult();
                    await Task.Delay(200, ct);
                    completions.Add(args.ToString());
                    return args.ToString();
                },
            }
        );

        mutation.Execute(1);
        await firstStarted.Task;
        mutation.Execute(2);

        await mutation.Success.FirstAsync();

        await Assert.That(completions).Contains("2");
        await Assert.That(completions).DoesNotContain("1");
    }

    [Test]
    public async Task Dispose_CompletesStateObservable()
    {
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string> { Mutator = (_, _) => Task.FromResult("ok") }
        );

        var completed = false;
        using var sub = mutation.State.Subscribe(_ => { }, () => completed = true);

        mutation.Dispose();

        await Assert.That(completed).IsTrue();
    }

    [Test]
    public async Task Dispose_IsIdempotent()
    {
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string> { Mutator = (_, _) => Task.FromResult("ok") }
        );

        mutation.Dispose();
        mutation.Dispose();

        await Assert.That(mutation.State).IsNotNull();
    }

    [Test]
    public async Task Dispose_WhileExecuteInFlight_DoesNotRaiseObjectDisposedException()
    {
        // Regression: without the _disposed guard in ExecuteAsync, calling Dispose() while an
        // execution was in-flight caused _state.OnNext() to be invoked on an already-disposed
        // BehaviorSubject, raising ObjectDisposedException on a ThreadPool thread.
        var started = new TaskCompletionSource();
        var gate = new TaskCompletionSource<string>();
        Exception? firstChanceOde = null;

        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> handler = (_, e) =>
        {
            if (e.Exception is ObjectDisposedException)
            {
                firstChanceOde = e.Exception;
            }
        };

        AppDomain.CurrentDomain.FirstChanceException += handler;

        try
        {
            var mutation = _client.CreateMutation(
                new MutationOptions<int, string>
                {
                    Mutator = async (_, _) =>
                    {
                        started.TrySetResult();
                        return await gate.Task;
                    },
                }
            );

            mutation.Execute(0);
            await started.Task;

            mutation.Dispose();
            gate.SetResult("done");

            await Task.Delay(100);

            await Assert.That(firstChanceOde).IsNull();
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= handler;
        }
    }

    [Test]
    public async Task OnSettled_Callback_IsCalledOnCancellation()
    {
        // Regression: OnSettled was previously skipped on OperationCanceledException.
        // Use a dedicated TCS to avoid a race where FirstAsync() can resume the test
        // continuation synchronously inside BehaviorSubject.OnNext, before OnSettled fires.
        var settledTcs = new TaskCompletionSource();
        var started = new TaskCompletionSource();

        var mutation = _client.CreateMutation(
            new MutationOptions<int, string>
            {
                Mutator = async (_, ct) =>
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                    return "";
                },
                OnSettled = () => settledTcs.TrySetResult(),
            }
        );

        mutation.Execute(0);
        await started.Task;
        mutation.Cancel();

        await settledTcs.Task;
        await Assert.That(settledTcs.Task.IsCompleted).IsTrue();
    }

    [Test]
    public async Task Execute_OnSuccess_RecordsActivityWithOkStatus()
    {
        Activity? recorded = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == QueryTelemetry.SourceName,
            Sample = (ref _) => ActivitySamplingResult.AllData,
            ActivityStopped = a =>
            {
                if (a.OperationName == QueryTelemetryTags.ActivityMutationExecute && a.Status == ActivityStatusCode.Ok)
                {
                    recorded = a;
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        var mutation = _client.CreateMutation(
            new MutationOptions<int, string> { Mutator = (_, _) => Task.FromResult("ok") }
        );

        mutation.Execute(0);
        await mutation.State.Where(s => s.IsSuccess).FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(recorded).IsNotNull();
        await Assert.That(recorded!.Status).IsEqualTo(ActivityStatusCode.Ok);
    }

    [Test]
    public async Task Execute_OnFailure_RecordsActivityWithErrorTags()
    {
        Activity? recorded = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == QueryTelemetry.SourceName,
            Sample = (ref _) => ActivitySamplingResult.AllData,
            ActivityStopped = a =>
            {
                if (
                    a.OperationName == QueryTelemetryTags.ActivityMutationExecute
                    && a.Status == ActivityStatusCode.Error
                    && Equals(a.GetTagItem(QueryTelemetryTags.TagErrorType), nameof(InvalidOperationException))
                )
                {
                    recorded = a;
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        var mutation = _client.CreateMutation(
            new MutationOptions<int, string>
            {
                Mutator = (_, _) => Task.FromException<string>(new InvalidOperationException("oops")),
                RetryHandler = new DefaultRetryHandler(),
            }
        );

        mutation.Execute(0);
        await mutation.Failure.FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(recorded).IsNotNull();
        await Assert.That(recorded!.Status).IsEqualTo(ActivityStatusCode.Error);
        await Assert
            .That(recorded.GetTagItem(QueryTelemetryTags.TagErrorType))
            .IsEqualTo(nameof(InvalidOperationException));
    }

    [Test]
    public async Task RetryHandler_NullInOptions_UsesGlobalHandler()
    {
        using var client = new QueryClient(
            new QueryClientOptions { RetryHandler = new DefaultRetryHandler() },
            _scheduler,
            _instrumentation
        );
        var attempts = 0;
        var mutation = client.CreateMutation(
            new MutationOptions<int, string>
            {
                Mutator = (_, _) =>
                {
                    attempts++;
                    return Task.FromException<string>(new Exception("fail"));
                },
                RetryHandler = null,
            }
        );

        mutation.Execute(0);
        await mutation.Failure.FirstAsync();

        await Assert.That(attempts).IsEqualTo(1); // NoRetryHandler: no retries
    }

    [Test]
    public async Task RetryHandler_ExplicitInOptions_OverridesGlobal()
    {
        var attempts = 0;
        var mutation = _client.CreateMutation(
            new MutationOptions<int, string>
            {
                Mutator = (_, _) =>
                {
                    attempts++;
                    return Task.FromException<string>(new Exception("fail"));
                },
                RetryHandler = new DefaultRetryHandler(),
            }
        );

        mutation.Execute(0);
        await mutation.Failure.FirstAsync();

        await Assert.That(attempts).IsEqualTo(1); // explicit NoRetryHandler wins
    }
}
