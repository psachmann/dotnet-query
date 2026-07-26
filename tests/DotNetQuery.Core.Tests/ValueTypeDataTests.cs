namespace DotNetQuery.Core.Tests;

public class ValueTypeDataTests
{
    private readonly TestScheduler _scheduler = new();
    private QueryCache _cache = default!;

    private static readonly QueryInstrumentation _instrumentation = new(NullLogger.Instance);

    [Before(Test)]
    public void Setup() => _cache = new(_scheduler, _instrumentation);

    [After(Test)]
    public void Teardown() => _cache.Dispose();

    private QueryObserver<int, int> CreateObserver(
        Func<int, CancellationToken, Task<int>>? fetcher = null,
        Optional<int> initialData = default
    )
    {
        var options = new QueryOptions<int, int>
        {
            KeyFactory = _ => QueryKey.From("value-type-test"),
            Fetcher = fetcher ?? ((_, _) => Task.FromResult(42)),
            InitialData = initialData,
        };
        return new QueryObserver<int, int>(
            options,
            new QueryClientOptions { StaleTime = TimeSpan.Zero },
            _cache,
            _scheduler,
            _instrumentation
        );
    }

    [Test]
    public async Task WithoutInitialData_StartsIdle_EvenForValueType()
    {
        // Regression: `options.InitialData is { } initial` always matched for value-typed TData
        // (default(int) == 0 is "not null"), so every value-typed query started in Success(0)
        // instead of Idle, even when no InitialData was supplied.
        using var sut = CreateObserver();

        sut.SetArgs(0);

        await Assert.That(sut.CurrentState.IsIdle).IsTrue();
    }

    [Test]
    public async Task WithInitialDataZero_StartsAsSuccessWithZero()
    {
        // default(int) is itself a valid, explicitly-supplied initial value and must be honored.
        using var sut = CreateObserver(initialData: 0);

        sut.SetArgs(0);

        using var _ = Assert.Multiple();
        await Assert.That(sut.CurrentState.IsSuccess).IsTrue();
        await Assert.That(sut.CurrentState.CurrentData).IsEqualTo(0);
        await Assert.That(sut.CurrentState.HasData).IsTrue();
    }

    [Test]
    public async Task RealData_ReplacesInitialDataZero()
    {
        using var sut = CreateObserver(initialData: 0, fetcher: (_, _) => Task.FromResult(7));

        var settled = new TaskCompletionSource();
        using var sub = sut.Settled.Subscribe(_ => settled.TrySetResult());

        sut.SetArgs(0);
        await settled.Task;

        using var _ = Assert.Multiple();
        await Assert.That(sut.CurrentState.IsSuccess).IsTrue();
        await Assert.That(sut.CurrentState.CurrentData).IsEqualTo(7);
    }
}
