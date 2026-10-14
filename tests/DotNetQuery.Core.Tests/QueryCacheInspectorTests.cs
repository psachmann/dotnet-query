using System.Reactive.Threading.Tasks;

namespace DotNetQuery.Core.Tests;

/// <summary>
/// Exercises the public inspection contract exactly as a consumer sees it — a client from
/// <see cref="QueryClientFactory"/>, reached through <see cref="QueryClientInspectionExtensions.TryGetInspector"/>,
/// with no internal types involved.
/// </summary>
public class QueryCacheInspectorTests
{
    private readonly TestScheduler _scheduler = new();
    private IQueryClient _client = default!;
    private IQueryCacheInspector _inspector = default!;

    [Before(Test)]
    public void Setup()
    {
        _client = QueryClientFactory.Create(new QueryClientOptions(), _scheduler);
        _client.TryGetInspector(out var inspector);
        _inspector = inspector!;
    }

    [After(Test)]
    public void Teardown() => _client.Dispose();

    private IQuery<int, string> CreateQuery(
        string key = "todos",
        Func<int, CancellationToken, Task<string>>? fetcher = null,
        TimeSpan? staleTime = null,
        TimeSpan? cacheTime = null,
        string? name = null
    ) =>
        _client.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = id => QueryKey.From(key, id),
                Fetcher = fetcher ?? ((id, _) => Task.FromResult($"data-{id}")),
                StaleTime = staleTime,
                CacheTime = cacheTime,
                Name = name,
            }
        );

    private IReadOnlyList<IQueryCacheEntry> Latest()
    {
        IReadOnlyList<IQueryCacheEntry> latest = [];
        using var _ = _inspector.Entries.Subscribe(entries => latest = entries);
        return latest;
    }

    [Test]
    public async Task TryGetInspector_ClientFromFactory_ReturnsTrue()
    {
        var supported = _client.TryGetInspector(out var inspector);

        using var _ = Assert.Multiple();
        await Assert.That(supported).IsTrue();
        await Assert.That(inspector).IsNotNull();
    }

    [Test]
    public async Task TryGetInspector_CustomClient_ReturnsFalse()
    {
        var custom = Mock.Of<IQueryClient>().Object;

        var supported = custom.TryGetInspector(out var inspector);

        using var _ = Assert.Multiple();
        await Assert.That(supported).IsFalse();
        await Assert.That(inspector).IsNull();
    }

    [Test]
    public async Task TryGetInspector_NullClient_Throws()
    {
        await Assert.That(() => ((IQueryClient)null!).TryGetInspector(out _)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Entries_OnSubscribe_EmitsEmptySnapshotForEmptyCache()
    {
        var emissions = new List<IReadOnlyList<IQueryCacheEntry>>();
        using var _ = _inspector.Entries.Subscribe(emissions.Add);

        using var _2 = Assert.Multiple();
        await Assert.That(emissions.Count).IsEqualTo(1);
        await Assert.That(emissions[0]).IsEmpty();
    }

    [Test]
    public async Task Entries_OnSubscribe_ReplaysExistingEntries()
    {
        using var query = CreateQuery();
        using var sub = query.State.Subscribe();
        query.SetArgs(1);
        await query.State.Where(s => s.IsSuccess).FirstAsync();

        var entries = Latest();

        using var _ = Assert.Multiple();
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Key).IsEqualTo(QueryKey.From("todos", 1));
    }

    [Test]
    public async Task Entries_WhenEntryAdded_EmitsSnapshotContainingIt()
    {
        var emissions = new List<IReadOnlyList<IQueryCacheEntry>>();
        using var _ = _inspector.Entries.Subscribe(emissions.Add);

        using var query = CreateQuery();
        query.SetArgs(1);

        await Assert.That(emissions[^1].Select(e => e.Key)).Contains(QueryKey.From("todos", 1));
    }

    [Test]
    public async Task Entries_WhenEntryEvicted_EmitsSnapshotWithoutIt()
    {
        using var query = CreateQuery(cacheTime: TimeSpan.FromMinutes(1));
        var sub = query.State.Subscribe();
        query.SetArgs(1);
        await query.State.Where(s => s.IsSuccess).FirstAsync();

        var emissions = new List<IReadOnlyList<IQueryCacheEntry>>();
        using var _ = _inspector.Entries.Subscribe(emissions.Add);

        sub.Dispose();
        await Assert.That(emissions[^1].Count).IsEqualTo(1); // still cached, counting down to eviction

        _scheduler.AdvanceBy(TimeSpan.FromMinutes(1).Ticks);

        await Assert.That(emissions[^1]).IsEmpty();
    }

    [Test]
    public async Task Entries_WhenEntryChangesState_EmitsAgain()
    {
        using var query = CreateQuery();
        query.SetArgs(1);

        var emissions = new List<IReadOnlyList<IQueryCacheEntry>>();
        using var _ = _inspector.Entries.Subscribe(emissions.Add);
        var before = emissions.Count;

        using var sub = query.State.Subscribe();
        await query.State.Where(s => s.IsSuccess).FirstAsync();

        await Assert.That(emissions.Count).IsGreaterThan(before);
    }

    [Test]
    public async Task Entries_WhenClientDisposed_Completes()
    {
        var completed = false;
        using var _ = _inspector.Entries.Subscribe(_ => { }, () => completed = true);

        _client.Dispose();

        await Assert.That(completed).IsTrue();
    }

    [Test]
    public async Task Entry_ReflectsSuccessfulQuery()
    {
        _scheduler.AdvanceTo(TimeSpan.FromHours(1).Ticks);
        using var query = CreateQuery(name: "todo-list");
        using var sub = query.State.Subscribe();
        query.SetArgs(7);
        await query.State.Where(s => s.IsSuccess).FirstAsync();

        var entry = Latest().Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.Name).IsEqualTo("todo-list");
        await Assert.That(entry.Status).IsEqualTo(QueryStatus.Success);
        await Assert.That(entry.CurrentData).IsEqualTo("data-7");
        await Assert.That(entry.Error).IsNull();
        await Assert.That(entry.LastUpdatedAt).IsEqualTo(_scheduler.Now);
        await Assert.That(entry.ObserverCount).IsEqualTo(1);
    }

    [Test]
    public async Task Entry_Name_FallsBackToFirstKeyPart()
    {
        using var query = CreateQuery(key: "users");
        query.SetArgs(1);

        await Assert.That(Latest().Single().Name).IsEqualTo("users");
    }

    [Test]
    public async Task Entry_BeforeFirstFetch_HasNoDataAndNoTimestamp()
    {
        using var query = CreateQuery();
        query.SetArgs(1);

        var entry = Latest().Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.Status).IsEqualTo(QueryStatus.Idle);
        await Assert.That(entry.CurrentData).IsNull();
        await Assert.That(entry.LastUpdatedAt).IsNull();
        await Assert.That(entry.ObserverCount).IsEqualTo(0);
        await Assert.That(entry.IsStale).IsTrue();
    }

    [Test]
    public async Task Entry_ObserverCount_TracksSubscribers()
    {
        using var query = CreateQuery();
        query.SetArgs(1);
        var entry = Latest().Single();

        var first = query.State.Subscribe();
        var second = query.State.Subscribe();
        await Assert.That(entry.ObserverCount).IsEqualTo(2);

        first.Dispose();
        await Assert.That(entry.ObserverCount).IsEqualTo(1);

        second.Dispose();
        await Assert.That(entry.ObserverCount).IsEqualTo(0);
    }

    [Test]
    public async Task Entry_LastUpdatedAt_AdvancesOnEachSuccess()
    {
        using var query = CreateQuery();
        using var sub = query.State.Subscribe();
        query.SetArgs(1);
        await query.State.Where(s => s.IsSuccess).FirstAsync();
        var entry = Latest().Single();
        var firstUpdate = entry.LastUpdatedAt;

        _scheduler.AdvanceBy(TimeSpan.FromMinutes(5).Ticks);
        query.SetData("manual");

        using var _ = Assert.Multiple();
        await Assert.That(entry.LastUpdatedAt).IsEqualTo(firstUpdate!.Value.AddMinutes(5));
        await Assert.That(entry.CurrentData).IsEqualTo("manual");
    }

    [Test]
    public async Task Entry_WhileRefetching_CarriesPreviousDataInLastData()
    {
        var calls = 0;
        var secondFetch = new TaskCompletionSource<string>();
        using var query = CreateQuery(fetcher: (_, _) => ++calls == 1 ? Task.FromResult("v1") : secondFetch.Task);
        using var sub = query.State.Subscribe();
        query.SetArgs(1);
        await query.State.Where(s => s.IsSuccess).FirstAsync();
        var entry = Latest().Single();

        var fetching = query.State.Where(s => s.IsFetching).FirstAsync().ToTask();
        entry.Refetch();
        await fetching;

        using var _ = Assert.Multiple();
        await Assert.That(entry.Status).IsEqualTo(QueryStatus.Fetching);
        await Assert.That(entry.CurrentData).IsNull();
        await Assert.That(entry.LastData).IsEqualTo("v1");

        secondFetch.SetResult("v2");
    }

    [Test]
    public async Task Entry_AfterFailure_ExposesError()
    {
        var error = new InvalidOperationException("boom");
        using var query = CreateQuery(fetcher: (_, _) => Task.FromException<string>(error));
        using var sub = query.State.Subscribe();
        query.SetArgs(1);
        await query.State.Where(s => s.IsFailure).FirstAsync();

        var entry = Latest().Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.Status).IsEqualTo(QueryStatus.Failure);
        await Assert.That(entry.Error).IsSameReferenceAs(error);
    }

    [Test]
    public async Task Entry_IsStale_FollowsStaleTime()
    {
        using var query = CreateQuery(staleTime: TimeSpan.FromMinutes(1));
        using var sub = query.State.Subscribe();
        query.SetArgs(1);
        await query.State.Where(s => s.IsSuccess).FirstAsync();
        var entry = Latest().Single();

        await Assert.That(entry.IsStale).IsFalse();

        _scheduler.AdvanceBy(TimeSpan.FromMinutes(1).Ticks);

        await Assert.That(entry.IsStale).IsTrue();
    }

    [Test]
    public async Task Entry_IsStale_WhenInvalidatedWithoutSubscribers()
    {
        using var query = CreateQuery();
        var sub = query.State.Subscribe();
        query.SetArgs(1);
        await query.State.Where(s => s.IsSuccess).FirstAsync();
        sub.Dispose();
        var entry = Latest().Single();

        entry.Invalidate();

        await Assert.That(entry.IsStale).IsTrue();
    }

    [Test]
    public async Task Entry_Refetch_FetchesAgain()
    {
        var calls = 0;
        using var query = CreateQuery(fetcher: (id, _) => Task.FromResult($"call-{++calls}"));
        using var sub = query.State.Subscribe();
        query.SetArgs(1);
        await query.State.Where(s => s.IsSuccess).FirstAsync();
        var entry = Latest().Single();

        var next = query.State.Where(s => s.CurrentData == "call-2").FirstAsync().ToTask();
        entry.Refetch();
        await next;

        await Assert.That(entry.CurrentData).IsEqualTo("call-2");
    }

    [Test]
    public async Task Entry_Cancel_StopsInFlightFetch()
    {
        var started = new TaskCompletionSource();
        CancellationToken observed = default;
        using var query = CreateQuery(
            fetcher: async (_, ct) =>
            {
                observed = ct;
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return "never";
            }
        );
        using var sub = query.State.Subscribe();
        query.SetArgs(1);
        await started.Task;

        Latest().Single().Cancel();

        await Assert.That(observed.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task Entry_DoesNotExposeQueryLifecycle()
    {
        using var query = CreateQuery();
        query.SetArgs(1);

        var entry = Latest().Single();

        // The public entry is deliberately not an IQuery — callers must not be handed Dispose().
        await Assert.That(entry is IQuery).IsFalse();
    }

    [Test]
    public async Task InfiniteEntry_ReflectsLoadedPages()
    {
        using var query = _client.CreateInfiniteQuery(
            new InfiniteQueryOptions<int, string, int>
            {
                KeyFactory = _ => QueryKey.From("feed"),
                Fetcher = (_, page, _) => Task.FromResult($"page-{page}"),
                InitialPageParam = 0,
                GetNextPageParam = info => info.PageParam + 1,
            }
        );
        using var sub = query.State.Subscribe();
        query.SetArgs(0);
        await query.State.Where(s => s.IsSuccess).FirstAsync();

        var next = query.State.Where(s => s.IsSuccess && s.Pages.Count == 2).FirstAsync().ToTask();
        query.FetchNextPage();
        await next;

        var entry = Latest().Single();

        using var _ = Assert.Multiple();
        await Assert.That(entry.Key).IsEqualTo(QueryKey.From("feed"));
        await Assert.That(entry.Name).IsEqualTo("feed");
        await Assert.That(entry.Status).IsEqualTo(QueryStatus.Success);
        await Assert.That((string[])entry.CurrentData!).IsEquivalentTo(["page-0", "page-1"]);
        await Assert.That(entry.LastData).IsNull();
        await Assert.That(entry.LastUpdatedAt).IsNotNull();
        await Assert.That(entry.ObserverCount).IsEqualTo(1);
        await Assert.That(entry.Error).IsNull();
    }

    [Test]
    public async Task Entries_ListsRegularAndInfiniteEntriesTogether()
    {
        using var regular = CreateQuery();
        regular.SetArgs(1);
        using var infinite = _client.CreateInfiniteQuery(
            new InfiniteQueryOptions<int, string, int>
            {
                KeyFactory = _ => QueryKey.From("feed"),
                Fetcher = (_, page, _) => Task.FromResult($"page-{page}"),
                InitialPageParam = 0,
                GetNextPageParam = info => info.PageParam + 1,
            }
        );
        infinite.SetArgs(0);

        await Assert
            .That(Latest().Select(e => e.Key))
            .IsEquivalentTo([QueryKey.From("todos", 1), QueryKey.From("feed")]);
    }
}
