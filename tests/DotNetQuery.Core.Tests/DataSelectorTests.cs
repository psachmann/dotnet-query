namespace DotNetQuery.Core.Tests;

public class DataSelectorTests
{
    private readonly TestScheduler _scheduler = new();
    private QueryCache _cache = default!;

    private static readonly QueryInstrumentation _instrumentation = new(NullLogger.Instance);

    [Before(Test)]
    public void Setup() => _cache = new QueryCache(_scheduler, _instrumentation);

    [After(Test)]
    public void Teardown() => _cache.Dispose();

    private QueryObserver<int, string[]> CreateObserver(Func<int, CancellationToken, Task<string[]>>? fetcher = null)
    {
        var options = new QueryOptions<int, string[]>
        {
            KeyFactory = _ => QueryKey.From("selector-test"),
            Fetcher = fetcher ?? ((_, _) => Task.FromResult(new[] { "a", "b", "c" })),
        };
        return new QueryObserver<int, string[]>(
            options,
            new QueryClientOptions { StaleTime = TimeSpan.Zero },
            _cache,
            _scheduler,
            _instrumentation
        );
    }

    [Test]
    public async Task Select_TransformsSuccessData()
    {
        using var sut = CreateObserver();

        sut.SetArgs(0);
        var count = await sut.Select(data => data.Length).FirstAsync();

        await Assert.That(count).IsEqualTo(3);
    }

    [Test]
    public async Task Select_DoesNotTriggerAdditionalFetches()
    {
        var fetchCount = 0;
        using var sut = CreateObserver(
            fetcher: (_, _) =>
            {
                fetchCount++;
                return Task.FromResult(new[] { "x" });
            }
        );

        using var sub1 = sut.Select(d => d.Length).Subscribe(_ => { });
        using var sub2 = sut.Select(d => d[0]).Subscribe(_ => { });

        var settled = new TaskCompletionSource();
        using var sub3 = sut.Settled.Subscribe(_ => settled.TrySetResult());

        sut.SetArgs(0);
        await settled.Task;

        await Assert.That(fetchCount).IsEqualTo(1);
    }

    [Test]
    public async Task Select_SuppressesReEmission_WhenSelectedValueUnchanged()
    {
        var callCount = 0;
        // Both fetches return arrays of same length but different content
        using var sut = CreateObserver(
            fetcher: (_, _) =>
            {
                callCount++;

                return Task.FromResult(callCount == 1 ? new[] { "a", "b" } : new[] { "x", "y" });
            }
        );

        var countEmissions = 0;
        using var selectSub = sut.Select(d => d.Length).Subscribe(_ => countEmissions++);

        var firstSettled = new TaskCompletionSource();
        var secondSettled = new TaskCompletionSource();
        var settledCount = 0;
        using var settledSub = sut.Settled.Subscribe(_ =>
        {
            settledCount++;

            if (settledCount == 1)
            {
                firstSettled.TrySetResult();
            }

            if (settledCount == 2)
            {
                secondSettled.TrySetResult();
            }
        });

        sut.SetArgs(0);
        await firstSettled.Task;

        sut.Refetch();
        await secondSettled.Task;

        using var _ = Assert.Multiple();
        await Assert.That(callCount).IsEqualTo(2); // two fetches
        await Assert.That(countEmissions).IsEqualTo(1); // length unchanged → no re-emission
    }

    [Test]
    public async Task Select_ReEmits_WhenSelectedValueChanges()
    {
        var callCount = 0;
        using var sut = CreateObserver(
            fetcher: (_, _) =>
            {
                callCount++;
                return Task.FromResult(callCount == 1 ? new[] { "a" } : new[] { "a", "b" });
            }
        );

        var emissions = new List<int>();
        using var selectSub = sut.Select(d => d.Length).Subscribe(n => emissions.Add(n));

        var firstSettled = new TaskCompletionSource();
        var secondSettled = new TaskCompletionSource();
        var settledCount = 0;
        using var settledSub = sut.Settled.Subscribe(_ =>
        {
            settledCount++;

            if (settledCount == 1)
            {
                firstSettled.TrySetResult();
            }

            if (settledCount == 2)
            {
                secondSettled.TrySetResult();
            }
        });

        sut.SetArgs(0);
        await firstSettled.Task;

        sut.Refetch();
        await secondSettled.Task;

        await Assert.That(emissions).IsEquivalentTo([1, 2]);
    }

    [Test]
    public async Task Select_WithCustomComparer_UsesProvidedComparer()
    {
        var callCount = 0;
        using var sut = CreateObserver(
            fetcher: (_, _) =>
            {
                callCount++;
                return Task.FromResult(new[] { "hello" });
            }
        );

        var emissions = new List<string>();
        using var selectSub = sut.Select(d => d[0], StringComparer.Ordinal).Subscribe(v => emissions.Add(v));

        var firstSettled = new TaskCompletionSource();
        var secondSettled = new TaskCompletionSource();
        var settledCount = 0;
        using var settledSub = sut.Settled.Subscribe(_ =>
        {
            settledCount++;

            if (settledCount == 1)
            {
                firstSettled.TrySetResult();
            }

            if (settledCount == 2)
            {
                secondSettled.TrySetResult();
            }
        });

        sut.SetArgs(0);
        await firstSettled.Task;

        sut.Refetch();
        await secondSettled.Task;

        using var _ = Assert.Multiple();
        await Assert.That(callCount).IsEqualTo(2);
        await Assert.That(emissions.Count).IsEqualTo(1); // same string value → suppressed
    }

    [Test]
    public async Task Select_MultipleDifferentSelectors_IndependentlyDeduplicate()
    {
        var callCount = 0;
        // Second fetch adds an element — count changes, first element stays the same
        using var sut = CreateObserver(
            fetcher: (_, _) =>
            {
                callCount++;

                return Task.FromResult(callCount == 1 ? new[] { "a" } : new[] { "a", "b" });
            }
        );

        var countEmissions = new List<int>();
        var firstEmissions = new List<string>();

        using var sub1 = sut.Select(d => d.Length).Subscribe(n => countEmissions.Add(n));
        using var sub2 = sut.Select(d => d[0]).Subscribe(v => firstEmissions.Add(v));

        var firstSettled = new TaskCompletionSource();
        var secondSettled = new TaskCompletionSource();
        var settledCount = 0;
        using var settledSub = sut.Settled.Subscribe(_ =>
        {
            settledCount++;

            if (settledCount == 1)
            {
                firstSettled.TrySetResult();
            }

            if (settledCount == 2)
            {
                secondSettled.TrySetResult();
            }
        });

        sut.SetArgs(0);
        await firstSettled.Task;

        sut.Refetch();
        await secondSettled.Task;

        using var _ = Assert.Multiple();
        await Assert.That(countEmissions).IsEquivalentTo([1, 2]); // count changed
        await Assert.That(firstEmissions.Count).IsEqualTo(1); // first element unchanged
    }
}
