using System.Reactive.Threading.Tasks;

namespace Fetchling.Core.Tests;

/// <summary>Writing pushed data into the cache through <see cref="IQueryClient"/>, without a query observer.</summary>
public class QueryClientSetDataTests
{
    private readonly TestScheduler _scheduler = new();
    private QueryClient _client = default!;
    private ControlledFetcher _fetcher = default!;

    private static readonly QueryInstrumentation _instrumentation = new(NullLogger.Instance);

    [Before(Test)]
    public void Setup()
    {
        _client = new(new(), _scheduler, _instrumentation);
        _fetcher = new();
    }

    [After(Test)]
    public void Teardown()
    {
        _client.Dispose();
    }

    private static QueryKey Todo(int id) => QueryKey.From("todos", id);

    private IQuery<int, string> CreateQuery(TimeSpan? staleTime = null, bool honourToken = true) =>
        _client.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = Todo,
                Fetcher = (_, ct) => _fetcher.Fetch(ct, honourToken),
                StaleTime = staleTime ?? TimeSpan.FromMinutes(1),
            }
        );

    // A query with no subscriber defers its fetch, so tests keep one alive for the query they drive.
    private static IDisposable Activate(IQuery<int, string> query, int id = 1)
    {
        var subscription = query.State.Subscribe(_ => { });
        query.SetArgs(id);

        return subscription;
    }

    private static Task<QueryState<string>> WaitFor(
        IQuery<int, string> query,
        Func<QueryState<string>, bool> predicate
    ) => query.State.Where(predicate).FirstAsync().ToTask();

    private async Task<IDisposable> Loaded(IQuery<int, string> query, string data, int id = 1)
    {
        var call = _fetcher.Count + 1;
        var active = Activate(query, id);
        await _fetcher.WaitForCall(call);
        _fetcher.Complete(call, data);
        await WaitFor(query, s => s.IsSuccess && s.CurrentData == data);

        return active;
    }

    [Test]
    public async Task SetData_ForAKeyThatIsNotCached_ReturnsFalse()
    {
        var written = _client.SetData(Todo(1), "pushed");

        await Assert.That(written).IsFalse();
    }

    [Test]
    public async Task SetData_WritesAFreshSuccessState_KeepingThePreviousDataAsLastData()
    {
        using var query = CreateQuery();
        using var active = await Loaded(query, "server");
        _scheduler.AdvanceBy(TimeSpan.FromMinutes(2).Ticks);

        var written = _client.SetData(Todo(1), "pushed");
        _client.InvalidateStale(_ => true); // within StaleTime of the write, so a no-op

        using var _ = Assert.Multiple();
        await Assert.That(written).IsTrue();
        await Assert.That(query.CurrentState.IsSuccess).IsTrue();
        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("pushed");
        await Assert.That(query.CurrentState.LastData).IsEqualTo("server");
        await Assert.That(_fetcher.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SetData_ForAnEntryWithoutDataYet_IsWritten()
    {
        using var query = CreateQuery();
        query.SetArgs(1); // cached, but never fetched: nobody is subscribed

        var written = _client.SetData(Todo(1), "pushed");

        using var _ = Assert.Multiple();
        await Assert.That(written).IsTrue();
        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("pushed");
    }

    [Test]
    public async Task SetData_WithADifferentDataType_Throws()
    {
        using var query = CreateQuery();
        query.SetArgs(1);

        await Assert
            .That(() => _client.SetData(Todo(1), new List<string>()))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("todos:1");
    }

    [Test]
    public async Task SetData_ForAnInfiniteQuery_Throws()
    {
        using var query = _client.CreateInfiniteQuery(
            new InfiniteQueryOptions<int, string, int>
            {
                KeyFactory = Todo,
                Fetcher = (_, _, _) => Task.FromResult("page"),
                InitialPageParam = 0,
                GetNextPageParam = _ => PageParam<int>.None,
            }
        );
        query.SetArgs(1);

        await Assert.That(() => _client.SetData(Todo(1), "pushed")).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Updater_ReceivesTheCurrentData()
    {
        using var query = CreateQuery();
        using var active = await Loaded(query, "server");

        var written = _client.SetData<string>(Todo(1), current => current + "+patch");

        using var _ = Assert.Multiple();
        await Assert.That(written).IsTrue();
        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("server+patch");
    }

    [Test]
    public async Task Updater_IsNotCalled_WhenTheEntryHasNoData()
    {
        using var query = CreateQuery();
        query.SetArgs(1);
        var called = false;

        var written = _client.SetData<string>(Todo(1), current =>
        {
            called = true;
            return current;
        });

        using var _ = Assert.Multiple();
        await Assert.That(written).IsFalse();
        await Assert.That(called).IsFalse();
        await Assert.That(query.CurrentState.IsIdle).IsTrue();
    }

    [Test]
    public async Task Updater_DuringARefetch_PatchesTheDataStillShown()
    {
        using var query = CreateQuery(staleTime: TimeSpan.Zero);
        using var active = await Loaded(query, "server");
        query.Refetch();
        await WaitFor(query, s => s.IsFetching);

        _client.SetData<string>(Todo(1), current => current + "+patch");

        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("server+patch");
    }

    [Test]
    public async Task SetData_SupersedesAFetchInFlight_AndCancelsItsToken()
    {
        using var query = CreateQuery(staleTime: TimeSpan.Zero);
        using var active = await Loaded(query, "server");
        query.Refetch();
        await _fetcher.WaitForCall(2);

        _client.SetData(Todo(1), "pushed");

        await Assert.That(_fetcher.Token(2).IsCancellationRequested).IsTrue();
        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("pushed");
    }

    [Test]
    public async Task SetData_SupersededFetchThatIgnoresItsToken_DoesNotOverwriteTheWrite()
    {
        using var query = CreateQuery(staleTime: TimeSpan.Zero, honourToken: false);
        using var active = await Loaded(query, "server");
        var recorder = query.State.Skip(1).Replay();
        using var connection = recorder.Connect();
        query.Refetch();
        await _fetcher.WaitForCall(2);

        _client.SetData(Todo(1), "pushed");
        _fetcher.Complete(2, "older");
        await _fetcher.WhenObserved(2);

        var states = await recorder.TakeUntil(Observable.Timer(TimeSpan.FromMilliseconds(100))).ToList();

        using var _ = Assert.Multiple();
        await Assert.That(states.Any(s => s.CurrentData == "older")).IsFalse();
        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("pushed");
        await Assert.That(query.CurrentState.IsSuccess).IsTrue();
    }

    [Test]
    public async Task SetData_CompletesAPendingPrefetch()
    {
        using var query = CreateQuery();
        var prefetch = query.PrefetchAsync(1);
        await _fetcher.WaitForCall(1);

        _client.SetData(Todo(1), "pushed");
        await prefetch;
        query.SetArgs(1); // binds the observer to the entry; fresh after the write, so no fetch

        using var _ = Assert.Multiple();
        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("pushed");
        await Assert.That(_fetcher.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SetData_AfterTheEntryWasEvicted_ReturnsFalse()
    {
        using var query = CreateQuery();
        query.SetArgs(1);
        _scheduler.AdvanceBy(TimeSpan.FromMinutes(5).Ticks);

        var written = _client.SetData(Todo(1), "pushed");

        await Assert.That(written).IsFalse();
    }

    [Test]
    public async Task PredicateOverload_PatchesMatchingEntriesOfTheDataType_AndSkipsTheRest()
    {
        using var first = CreateQuery();
        using var second = CreateQuery();
        using var empty = CreateQuery();
        using var firstActive = await Loaded(first, "one", id: 1);
        using var secondActive = await Loaded(second, "two", id: 2);
        empty.SetArgs(3); // no data
        using var other = _client.CreateQuery(
            new QueryOptions<int, List<string>>
            {
                KeyFactory = id => QueryKey.From("todos", "list", id),
                Fetcher = (_, _) => Task.FromResult(new List<string>()),
            }
        );
        other.SetArgs(1); // matches the predicate, different data type
        using var unrelated = _client.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = id => QueryKey.From("users", id),
                Fetcher = (_, _) => Task.FromResult("user"),
            }
        );
        using var unrelatedActive = Activate(unrelated);
        await WaitFor(unrelated, s => s.IsSuccess);

        var written = _client.SetData<string>(key => key.Parts is ["todos", ..], current => current.ToUpperInvariant());

        using var _ = Assert.Multiple();
        await Assert.That(written).IsEqualTo(2);
        await Assert.That(first.CurrentState.CurrentData).IsEqualTo("ONE");
        await Assert.That(second.CurrentState.CurrentData).IsEqualTo("TWO");
        await Assert.That(unrelated.CurrentState.CurrentData).IsEqualTo("user");
        await Assert.That(empty.CurrentState.IsIdle).IsTrue();
    }

    [Test]
    public async Task ConcurrentWritesAndFetches_NeverThrow_AndEndInASuccessState()
    {
        using var query = CreateQuery(staleTime: TimeSpan.Zero);
        using var active = await Loaded(query, "server");

        await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(worker =>
                    Task.Run(() =>
                    {
                        for (var i = 0; i < 200; i++)
                        {
                            _client.SetData(Todo(1), $"{worker}:{i}");
                            _client.SetData<string>(Todo(1), current => current + ".");
                            if (i % 20 == 0)
                            {
                                query.Refetch();
                            }
                        }
                    })
                )
        );
        _client.SetData(Todo(1), "last");

        using var _ = Assert.Multiple();
        await Assert.That(query.CurrentState.IsSuccess).IsTrue();
        await Assert.That(query.CurrentState.CurrentData).IsEqualTo("last");
    }

    [Test]
    public async Task ObserverSetData_StillLetsAFetchInFlightFinish()
    {
        // IQuery.SetData (optimistic updates) keeps its behaviour: it does not cancel the fetch in flight.
        using var query = CreateQuery(staleTime: TimeSpan.Zero);
        using var active = await Loaded(query, "server");
        query.Refetch();
        await _fetcher.WaitForCall(2);

        query.SetData("optimistic");
        var cancelled = _fetcher.Token(2).IsCancellationRequested;
        _fetcher.Complete(2, "confirmed");
        await WaitFor(query, s => s.IsSuccess && s.CurrentData == "confirmed");

        await Assert.That(cancelled).IsFalse();
    }

    /// <summary>A fetcher the test completes call by call.</summary>
    private sealed class ControlledFetcher
    {
        private readonly Lock _lock = new();
        private readonly List<(TaskCompletionSource<string> Result, CancellationToken Token, Task Observed)> _calls =
        [];
        private readonly List<TaskCompletionSource> _callSignals = [];

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _calls.Count;
                }
            }
        }

        public Task<string> Fetch(CancellationToken ct, bool honourToken)
        {
            var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = honourToken ? result.Task.WaitAsync(ct) : result.Task;

            lock (_lock)
            {
                _calls.Add((result, ct, task));
                Signal(_calls.Count).TrySetResult();
            }

            return task;
        }

        public Task WaitForCall(int call)
        {
            lock (_lock)
            {
                return Signal(call).Task;
            }
        }

        public CancellationToken Token(int call)
        {
            lock (_lock)
            {
                return _calls[call - 1].Token;
            }
        }

        public void Complete(int call, string data)
        {
            lock (_lock)
            {
                _calls[call - 1].Result.TrySetResult(data);
            }
        }

        /// <summary>Completes once the fetcher's returned task has completed (its awaiter then runs).</summary>
        public Task WhenObserved(int call)
        {
            lock (_lock)
            {
                return _calls[call - 1].Observed.ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }

        private TaskCompletionSource Signal(int call)
        {
            while (_callSignals.Count < call)
            {
                _callSignals.Add(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            }

            return _callSignals[call - 1];
        }
    }
}
