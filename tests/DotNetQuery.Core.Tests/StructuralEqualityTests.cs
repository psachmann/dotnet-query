namespace DotNetQuery.Core.Tests;

public class StructuralEqualityTests
{
    private readonly TestScheduler _scheduler = new();
    private QueryCache _cache = default!;

    private static readonly QueryInstrumentation _instrumentation = new(NullLogger.Instance);

    [Before(Test)]
    public void Setup() => _cache = new(_scheduler, _instrumentation);

    [After(Test)]
    public void Teardown() => _cache.Dispose();

    private QueryObserver<int, string> CreateObserver(
        Func<int, CancellationToken, Task<string>>? fetcher = null,
        IEqualityComparer<string>? dataComparer = null
    )
    {
        var options = new QueryOptions<int, string>
        {
            KeyFactory = _ => QueryKey.From("eq-test"),
            Fetcher = fetcher ?? ((_, _) => Task.FromResult("data")),
            DataComparer = dataComparer,
        };
        return new QueryObserver<int, string>(
            options,
            new QueryClientOptions { StaleTime = TimeSpan.Zero },
            _cache,
            _scheduler,
            _instrumentation
        );
    }

    [Test]
    public async Task Success_WhenDataUnchanged_DoesNotReEmit()
    {
        var fetchCount = 0;
        using var sut = CreateObserver(
            fetcher: (_, _) =>
            {
                fetchCount++;
                return Task.FromResult("same");
            }
        );

        var emissions = new List<string>();
        using var successSub = sut.Success.Subscribe(d => emissions.Add(d));

        var firstSettled = new TaskCompletionSource();
        var secondSettled = new TaskCompletionSource();
        var settledCount = 0;
        using var settledSub = sut.Settled.Subscribe(_ =>
        {
            settledCount++;
            if (settledCount == 1)
                firstSettled.TrySetResult();
            if (settledCount == 2)
                secondSettled.TrySetResult();
        });

        sut.SetArgs(0);
        await firstSettled.Task;

        sut.Refetch();
        await secondSettled.Task;

        using var _ = Assert.Multiple();
        await Assert.That(fetchCount).IsEqualTo(2); // fetch happened twice
        await Assert.That(emissions.Count).IsEqualTo(1); // Success emitted only once
    }

    [Test]
    public async Task Success_WhenDataChanges_ReEmits()
    {
        var callCount = 0;
        using var sut = CreateObserver(
            fetcher: (_, _) =>
            {
                callCount++;
                return Task.FromResult(callCount == 1 ? "first" : "second");
            }
        );

        var emissions = new List<string>();
        using var successSub = sut.Success.Subscribe(d => emissions.Add(d));

        var firstSettled = new TaskCompletionSource();
        var secondSettled = new TaskCompletionSource();
        var settledCount = 0;
        using var settledSub = sut.Settled.Subscribe(_ =>
        {
            settledCount++;
            if (settledCount == 1)
                firstSettled.TrySetResult();
            if (settledCount == 2)
                secondSettled.TrySetResult();
        });

        sut.SetArgs(0);
        await firstSettled.Task;

        sut.Refetch();
        await secondSettled.Task;

        await Assert.That(emissions).IsEquivalentTo(["first", "second"]);
    }

    [Test]
    public async Task State_StillTransitionsThroughFetching_EvenWhenDataUnchanged()
    {
        using var sut = CreateObserver(fetcher: (_, _) => Task.FromResult("same"));

        var firstSettled = new TaskCompletionSource();
        var fetchingAfterFirst = new TaskCompletionSource();
        var secondSettled = new TaskCompletionSource();
        var settledCount = 0;
        using var settledSub = sut.Settled.Subscribe(_ =>
        {
            settledCount++;
            if (settledCount == 1)
                firstSettled.TrySetResult();
            if (settledCount == 2)
                secondSettled.TrySetResult();
        });

        sut.SetArgs(0);
        await firstSettled.Task;

        using var fetchingSub = sut.State.Where(s => s.IsFetching).Subscribe(_ => fetchingAfterFirst.TrySetResult());

        sut.Refetch();
        await secondSettled.Task;

        await Assert.That(fetchingAfterFirst.Task.IsCompleted).IsTrue();
    }

    [Test]
    public async Task Success_WithCustomComparer_UsesProvidedComparer()
    {
        // Case-insensitive: "DATA" equals "data"
        var callCount = 0;
        using var sut = CreateObserver(
            fetcher: (_, _) =>
            {
                callCount++;
                return Task.FromResult(callCount == 1 ? "data" : "DATA");
            },
            dataComparer: StringComparer.OrdinalIgnoreCase
        );

        var emissions = new List<string>();
        using var successSub = sut.Success.Subscribe(d => emissions.Add(d));

        var firstSettled = new TaskCompletionSource();
        var secondSettled = new TaskCompletionSource();
        var settledCount = 0;
        using var settledSub = sut.Settled.Subscribe(_ =>
        {
            settledCount++;
            if (settledCount == 1)
                firstSettled.TrySetResult();
            if (settledCount == 2)
                secondSettled.TrySetResult();
        });

        sut.SetArgs(0);
        await firstSettled.Task;

        sut.Refetch();
        await secondSettled.Task;

        using var _ = Assert.Multiple();
        await Assert.That(callCount).IsEqualTo(2); // two fetches
        await Assert.That(emissions.Count).IsEqualTo(1); // custom comparer suppressed re-emission
    }

    [Test]
    public async Task DataReference_IsPreservedWhenEqual()
    {
        var original = new string("preserved".ToCharArray()); // unique reference
        var duplicate = new string("preserved".ToCharArray()); // same value, different reference

        var callCount = 0;
        using var sut = CreateObserver(
            fetcher: (_, _) =>
            {
                callCount++;
                return Task.FromResult(callCount == 1 ? original : duplicate);
            }
        );

        var firstSettled = new TaskCompletionSource();
        var secondSettled = new TaskCompletionSource();
        var settledCount = 0;
        using var settledSub = sut.Settled.Subscribe(_ =>
        {
            settledCount++;
            if (settledCount == 1)
                firstSettled.TrySetResult();
            if (settledCount == 2)
                secondSettled.TrySetResult();
        });

        sut.SetArgs(0);
        await firstSettled.Task;
        var firstRef = sut.CurrentState.CurrentData;

        sut.Refetch();
        await secondSettled.Task;
        var secondRef = sut.CurrentState.CurrentData;

        using var _ = Assert.Multiple();
        await Assert.That(callCount).IsEqualTo(2);
        await Assert.That(ReferenceEquals(firstRef, secondRef)).IsTrue(); // old reference preserved
    }
}
