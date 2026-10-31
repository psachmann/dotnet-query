using System.Reactive.Threading.Tasks;
using System.Threading.Channels;
using TUnit.Assertions.Enums;

namespace DotNetQuery.Core.Tests;

public class StreamedQueryTests
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

    private IQuery<string, string> CreateQuery(
        ControlledStream stream,
        StreamRefetchMode refetchMode = StreamRefetchMode.Reset,
        IRetryHandler? retryHandler = null,
        TimeSpan? refetchInterval = null,
        string name = "answer"
    ) =>
        _client.CreateStreamedQuery(
            new StreamedQueryOptions<string, string, string>
            {
                KeyFactory = prompt => QueryKey.From(name, prompt),
                Fetcher = (_, ct) => stream.Open(ct),
                InitialValue = "",
                Reducer = (text, token) => text + token,
                RefetchMode = refetchMode,
                RetryHandler = retryHandler,
                RefetchInterval = refetchInterval,
                Name = name,
            }
        );

    private static Task<QueryState<string>> WaitFor(
        IQuery<string, string> query,
        Func<QueryState<string>, bool> predicate
    ) => query.State.Where(predicate).FirstAsync().ToTask();

    [Test]
    public async Task Stream_IsFetchingUntilFirstChunk_ThenStreamingWithFoldedData_ThenSuccess()
    {
        var stream = new ControlledStream();
        using var query = CreateQuery(stream);
        using var recorder = new StateRecorder(query.State);

        using var active = Activate(query);
        await stream.WaitForOpen(1);
        await Assert.That(query.CurrentState.IsFetching).IsTrue();

        stream.Write("Hel");
        await recorder.WaitFor(s => s.CurrentData == "Hel");
        stream.Write("lo");
        await recorder.WaitFor(s => s.CurrentData == "Hello");
        stream.Complete();
        var success = await recorder.WaitFor(s => s.IsSuccess && !s.IsStreaming);

        var shape = recorder
            .States.SkipWhile(s => s.IsIdle)
            .Select(s => (s.Status, s.IsStreaming, s.CurrentData))
            .ToList();

        using var _ = Assert.Multiple();
        await Assert
            .That(shape)
            .IsEquivalentTo(
                [
                    (QueryStatus.Fetching, false, (string?)null),
                    (QueryStatus.Success, true, "Hel"),
                    (QueryStatus.Success, true, "Hello"),
                    (QueryStatus.Success, false, "Hello"),
                ],
                CollectionOrdering.Matching
            );
        await Assert.That(success.CurrentData).IsEqualTo("Hello");
    }

    [Test]
    public async Task SuccessAndSettled_EmitOnce_WhenStreamCompletes()
    {
        var stream = new ControlledStream();
        using var query = CreateQuery(stream);
        var successes = new List<string>();
        var settled = new List<QueryState<string>>();
        var settledOnce = new TaskCompletionSource();
        using var successSubscription = query.Success.Subscribe(successes.Add);
        using var settledSubscription = query.Settled.Subscribe(state =>
        {
            settled.Add(state);
            settledOnce.TrySetResult();
        });

        using var active = Activate(query);
        await stream.WaitForOpen(1);
        stream.Write("a");
        stream.Write("b");
        await WaitFor(query, s => s.CurrentData == "ab");

        await Assert.That(successes).IsEmpty();

        stream.Complete();
        // Settled is subscribed after Success, so once it has fired, Success has been recorded too.
        await settledOnce.Task;

        using var _ = Assert.Multiple();
        await Assert.That(successes).IsEquivalentTo(["ab"]);
        await Assert.That(settled.Count).IsEqualTo(1);
    }

    [Test]
    public async Task EmptyStream_CompletesWithInitialValue()
    {
        var stream = new ControlledStream();
        using var query = CreateQuery(stream);

        using var active = Activate(query);
        await stream.WaitForOpen(1);
        stream.Complete();
        var success = await query.Success.FirstAsync();

        await Assert.That(success).IsEqualTo("");
    }

    [Test]
    public async Task ObserversOfSameKey_ShareOneStream()
    {
        var stream = new ControlledStream();
        using var first = CreateQuery(stream);
        using var second = CreateQuery(stream);

        using var firstActive = Activate(first);
        using var secondActive = Activate(second);
        await stream.WaitForOpen(1);
        stream.Write("x");
        stream.Complete();

        var results = await Task.WhenAll(first.Success.FirstAsync().ToTask(), second.Success.FirstAsync().ToTask());

        using var _ = Assert.Multiple();
        await Assert.That(results).IsEquivalentTo(["x", "x"]);
        await Assert.That(stream.OpenCount).IsEqualTo(1);
    }

    [Test]
    public async Task FailureMidStream_KeepsPartialDataInLastData()
    {
        var stream = new ControlledStream();
        using var query = CreateQuery(stream);

        using var active = Activate(query);
        await stream.WaitForOpen(1);
        stream.Write("partial");
        await WaitFor(query, s => s.CurrentData == "partial");
        stream.Fail(new InvalidOperationException("connection lost"));
        var failure = await WaitFor(query, s => s.IsFailure);

        using var _ = Assert.Multiple();
        await Assert.That(failure.Error).IsTypeOf<InvalidOperationException>();
        await Assert.That(failure.LastData).IsEqualTo("partial");
        await Assert.That(failure.IsStreaming).IsFalse();
    }

    [Test]
    public async Task Retry_RestartsStreamFromInitialValue()
    {
        var stream = new ControlledStream();
        using var query = CreateQuery(stream, retryHandler: new RetryOnFailureHandler(maxAttempts: 2));
        using var recorder = new StateRecorder(query.State);

        using var active = Activate(query);
        await stream.WaitForOpen(1);
        stream.Write("a");
        await recorder.WaitFor(s => s.CurrentData == "a");
        stream.Fail(new Exception("transient"));

        await stream.WaitForOpen(2);
        stream.Write("x");
        stream.Write("y");
        stream.Complete();
        var success = await recorder.WaitFor(s => s.IsSuccess && !s.IsStreaming);

        var shape = recorder.States.SkipWhile(s => s.IsIdle).Select(s => (s.Status, s.CurrentData)).ToList();

        using var _ = Assert.Multiple();
        await Assert.That(success.CurrentData).IsEqualTo("xy");
        await Assert
            .That(shape)
            .IsEquivalentTo(
                [
                    (QueryStatus.Fetching, (string?)null),
                    (QueryStatus.Success, "a"),
                    (QueryStatus.Fetching, null), // the retry drops what the failed attempt showed
                    (QueryStatus.Success, "x"),
                    (QueryStatus.Success, "xy"),
                    (QueryStatus.Success, "xy"),
                ],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task Refetch_ResetMode_DropsPreviousDataAndStreamsAgain()
    {
        var stream = new ControlledStream();
        using var query = CreateQuery(stream, StreamRefetchMode.Reset);
        using var active = await CompleteFirstStream(query, stream, "old");
        using var recorder = new StateRecorder(query.State.Skip(1));

        query.Refetch();
        await stream.WaitForOpen(2);
        stream.Write("new");
        stream.Complete();
        var success = await recorder.WaitFor(s => s.IsSuccess && !s.IsStreaming && s.CurrentData == "new");

        var shape = recorder.States.Select(s => (s.Status, s.IsStreaming, s.CurrentData, s.LastData)).ToList();

        using var _ = Assert.Multiple();
        await Assert
            .That(shape)
            .IsEquivalentTo(
                [
                    (QueryStatus.Fetching, false, (string?)null, (string?)null),
                    (QueryStatus.Success, true, "new", null),
                    (QueryStatus.Success, false, "new", "old"),
                ],
                CollectionOrdering.Matching
            );
        await Assert.That(success.LastData).IsEqualTo("old");
    }

    [Test]
    public async Task Refetch_ReplaceMode_KeepsPreviousDataUntilStreamCompletes()
    {
        var stream = new ControlledStream();
        using var query = CreateQuery(stream, StreamRefetchMode.Replace);
        using var active = await CompleteFirstStream(query, stream, "old");
        using var recorder = new StateRecorder(query.State.Skip(1));

        query.Refetch();
        await stream.WaitForOpen(2);
        stream.Write("ne");
        stream.Write("w");
        stream.Complete();
        await recorder.WaitFor(s => s.IsSuccess && s.CurrentData == "new");

        var shape = recorder.States.Select(s => (s.Status, s.IsStreaming, s.CurrentData, s.LastData)).ToList();

        await Assert
            .That(shape)
            .IsEquivalentTo(
                [
                    (QueryStatus.Fetching, false, (string?)null, (string?)"old"),
                    (QueryStatus.Success, false, "new", "old"),
                ],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task FirstFetch_ReplaceMode_StillStreamsChunks()
    {
        var stream = new ControlledStream();
        using var query = CreateQuery(stream, StreamRefetchMode.Replace);

        using var active = Activate(query);
        await stream.WaitForOpen(1);
        stream.Write("a");
        var streaming = await WaitFor(query, s => s.IsStreaming);

        await Assert.That(streaming.CurrentData).IsEqualTo("a");
        stream.Complete();
    }

    [Test]
    public async Task Invalidate_WhileStreaming_DoesNotRestartStream()
    {
        var stream = new ControlledStream();
        using var query = CreateQuery(stream);

        using var active = Activate(query);
        await stream.WaitForOpen(1);
        stream.Write("a");
        await WaitFor(query, s => s.IsStreaming);

        query.Invalidate();
        _client.Invalidate(_ => true);
        stream.Write("b");
        stream.Complete();
        var success = await query.Success.FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(success).IsEqualTo("ab");
        await Assert.That(stream.OpenCount).IsEqualTo(1);
        await Assert.That(stream.Token(1).IsCancellationRequested).IsFalse();
    }

    [Test]
    public async Task Invalidate_AfterStreamCompleted_Refetches()
    {
        var stream = new ControlledStream();
        using var query = CreateQuery(stream);
        using var active = await CompleteFirstStream(query, stream, "a");

        query.Invalidate();

        await stream.WaitForOpen(2);
        await Assert.That(stream.OpenCount).IsEqualTo(2);
        stream.Complete();
    }

    [Test]
    public async Task Refetch_WhileStreaming_RestartsStreamAndCancelsTheOldOne()
    {
        var stream = new ControlledStream();
        using var query = CreateQuery(stream);

        using var active = Activate(query);
        await stream.WaitForOpen(1);
        stream.Write("a");
        await WaitFor(query, s => s.IsStreaming);

        query.Refetch();
        await stream.WaitForOpen(2);
        stream.Write("z");
        stream.Complete();
        var success = await query.Success.FirstAsync();

        using var _ = Assert.Multiple();
        await Assert.That(success).IsEqualTo("z");
        await Assert.That(stream.Token(1).IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task Cancel_MidStream_GoesIdle_KeepingPartialData_AndCancelsFetcherToken()
    {
        var stream = new ControlledStream();
        using var query = CreateQuery(stream);

        using var active = Activate(query);
        await stream.WaitForOpen(1);
        stream.Write("par");
        await WaitFor(query, s => s.IsStreaming);

        query.Cancel();
        var idle = await WaitFor(query, s => s.IsIdle);

        using var _ = Assert.Multiple();
        await Assert.That(idle.LastData).IsEqualTo("par");
        await Assert.That(stream.Token(1).IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task Dispose_CancelsFetcherToken()
    {
        var stream = new ControlledStream();
        var query = CreateQuery(stream);

        using var active = Activate(query);
        await stream.WaitForOpen(1);
        query.Dispose();
        _client.Dispose();

        await Assert.That(stream.Token(1).IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task GlobalRefetchInterval_IsIgnored()
    {
        using var client = new QueryClient(
            new QueryClientOptions { RefetchInterval = TimeSpan.FromSeconds(1) },
            _scheduler,
            _instrumentation
        );
        var stream = new ControlledStream();
        using var query = client.CreateStreamedQuery(
            new StreamedQueryOptions<string, string, string>
            {
                KeyFactory = prompt => QueryKey.From("answer", prompt),
                Fetcher = (_, ct) => stream.Open(ct),
                InitialValue = "",
                Reducer = (text, token) => text + token,
            }
        );
        using var subscription = query.State.Subscribe(_ => { });

        using var active = Activate(query);
        await stream.WaitForOpen(1);
        stream.Complete();
        await query.Success.FirstAsync();
        _scheduler.AdvanceBy(TimeSpan.FromSeconds(10).Ticks);

        await Assert.That(stream.OpenCount).IsEqualTo(1);
    }

    [Test]
    public async Task OwnRefetchInterval_Refetches()
    {
        var stream = new ControlledStream();
        using var query = CreateQuery(stream, refetchInterval: TimeSpan.FromSeconds(1));
        using var active = await CompleteFirstStream(query, stream, "a");

        _scheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks);

        await stream.WaitForOpen(2);
        await Assert.That(stream.OpenCount).IsEqualTo(2);
        stream.Complete();
    }

    [Test]
    public async Task FetchSpan_IsTaggedWithChunkCount()
    {
        var name = $"chunks-{Guid.NewGuid():N}";
        var recorded = new TaskCompletionSource<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == QueryTelemetry.SourceName,
            Sample = (ref _) => ActivitySamplingResult.AllData,
            ActivityStopped = a =>
            {
                if (
                    a.OperationName == QueryTelemetryTags.ActivityQueryFetch
                    && (string?)a.GetTagItem(QueryTelemetryTags.TagQueryName) == name
                )
                {
                    recorded.TrySetResult(a);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        var stream = new ControlledStream();
        using var query = CreateQuery(stream, name: name);
        using var active = Activate(query);
        await stream.WaitForOpen(1);
        stream.Write("a");
        stream.Write("b");
        stream.Write("c");
        stream.Complete();

        var activity = await recorded.Task;

        await Assert.That(activity.GetTagItem(QueryTelemetryTags.TagChunks)).IsEqualTo(3);
    }

    // A query with no subscriber defers its fetch, so every test keeps one alive for the query it drives.
    private static IDisposable Activate(IQuery<string, string> query)
    {
        var subscription = query.State.Subscribe(_ => { });
        query.SetArgs("q");

        return subscription;
    }

    private static async Task<IDisposable> CompleteFirstStream(
        IQuery<string, string> query,
        ControlledStream stream,
        string text
    )
    {
        var active = Activate(query);
        await stream.WaitForOpen(1);
        stream.Write(text);
        stream.Complete();
        await WaitFor(query, s => s.IsSuccess && !s.IsStreaming && s.CurrentData == text);

        return active;
    }

    /// <summary>
    /// A stream the test drives chunk by chunk. Every <see cref="Open"/> starts a fresh channel, so refetches
    /// and retries each get their own stream; <see cref="Write"/> and friends act on the most recent one.
    /// </summary>
    private sealed class ControlledStream
    {
        private readonly Lock _lock = new();
        private readonly List<(Channel<string> Channel, CancellationToken Token)> _opened = [];
        private readonly List<TaskCompletionSource> _openSignals = [];

        public int OpenCount
        {
            get
            {
                lock (_lock)
                {
                    return _opened.Count;
                }
            }
        }

        public IAsyncEnumerable<string> Open(CancellationToken ct)
        {
            var channel = Channel.CreateUnbounded<string>();

            lock (_lock)
            {
                _opened.Add((channel, ct));
                Signal(_opened.Count).TrySetResult();
            }

            return channel.Reader.ReadAllAsync(ct);
        }

        public Task WaitForOpen(int count)
        {
            lock (_lock)
            {
                return Signal(count).Task;
            }
        }

        public CancellationToken Token(int open)
        {
            lock (_lock)
            {
                return _opened[open - 1].Token;
            }
        }

        public void Write(string chunk) => Latest().Writer.TryWrite(chunk);

        public void Complete() => Latest().Writer.TryComplete();

        public void Fail(Exception error) => Latest().Writer.TryComplete(error);

        private Channel<string> Latest()
        {
            lock (_lock)
            {
                return _opened[^1].Channel;
            }
        }

        private TaskCompletionSource Signal(int count)
        {
            while (_openSignals.Count < count)
            {
                _openSignals.Add(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            }

            return _openSignals[count - 1];
        }
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

    /// <summary>
    /// Records every state a subscription sees, and lets a test wait for a state <b>in that record</b>. Waiting
    /// on a separate, later subscription instead is racy: <c>BehaviorSubject</c> updates its value before it
    /// notifies observers, so a late subscriber can see a state through replay before an earlier subscriber
    /// has been handed it.
    /// </summary>
    private sealed class StateRecorder : IDisposable
    {
        private readonly Lock _lock = new();
        private readonly List<QueryState<string>> _states = [];
        private readonly List<(
            Func<QueryState<string>, bool> Predicate,
            TaskCompletionSource<QueryState<string>> Signal
        )> _waiters = [];
        private readonly IDisposable _subscription;

        public StateRecorder(IObservable<QueryState<string>> source) => _subscription = source.Subscribe(Record);

        public IReadOnlyList<QueryState<string>> States
        {
            get
            {
                lock (_lock)
                {
                    return [.. _states];
                }
            }
        }

        public Task<QueryState<string>> WaitFor(Func<QueryState<string>, bool> predicate)
        {
            lock (_lock)
            {
                if (_states.FirstOrDefault(predicate) is { } recorded)
                {
                    return Task.FromResult(recorded);
                }

                var signal = new TaskCompletionSource<QueryState<string>>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                _waiters.Add((predicate, signal));

                return signal.Task;
            }
        }

        public void Dispose() => _subscription.Dispose();

        private void Record(QueryState<string> state)
        {
            lock (_lock)
            {
                _states.Add(state);
                _waiters.RemoveAll(waiter => waiter.Predicate(state) && waiter.Signal.TrySetResult(state));
            }
        }
    }
}
