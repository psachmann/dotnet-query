namespace DotNetQuery.Mvvm.Tests;

using System.ComponentModel;

public class QueryViewModelStateTests
{
    private readonly Mock<IQuery<int, string>> _queryMock = Mock.Of<IQuery<int, string>>();
    private readonly RecordingSynchronizationContext _uiContext = new();
    private BehaviorSubject<QueryState<string>> _stateSubject = default!;

    [After(Test)]
    public void Teardown()
    {
        _stateSubject.OnCompleted();
        _stateSubject.Dispose();
    }

    private QueryViewModel<int, string> CreateSut(QueryState<string> initialState)
    {
        _stateSubject = new(initialState);
        _queryMock.CurrentState.Returns(_stateSubject.Value);
        _queryMock.State.Returns(_stateSubject.AsObservable());

        return new QueryViewModel<int, string>(_queryMock.Object, new SynchronizationContextUiDispatcher(_uiContext));
    }

    private static List<string?> CollectPropertyChanges(QueryViewModel<int, string> sut)
    {
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        return raised;
    }

    [Test]
    public async Task Ctor_WithSuccessCurrentState_ExposesDataSynchronously()
    {
        using var sut = CreateSut(QueryState<string>.CreateSuccess("hello"));

        // No DrainAll — the initial state must be visible before any dispatcher hop.
        await Assert.That(sut.Data).IsEqualTo("hello");
        await Assert.That(sut.IsSuccess).IsTrue();
        await Assert.That(sut.Status).IsEqualTo(QueryStatus.Success);
        await Assert.That(sut.HasData).IsTrue();
    }

    [Test]
    public async Task StateEmission_BeforeContextDrains_DoesNotNotify()
    {
        using var sut = CreateSut(QueryState<string>.CreateIdle());
        var raised = CollectPropertyChanges(sut);

        _stateSubject.OnNext(QueryState<string>.CreateFetching());

        await Assert.That(sut.IsFetching).IsFalse();
        await Assert.That(raised).IsEmpty();

        _uiContext.DrainAll();

        await Assert.That(sut.IsFetching).IsTrue();
        await Assert.That(raised).Contains(nameof(sut.IsFetching));
    }

    [Test]
    public async Task Ctor_FetchingWithoutLastData_SetsIsLoadingTrue()
    {
        using var sut = CreateSut(QueryState<string>.CreateFetching());

        await Assert.That(sut.IsLoading).IsTrue();
        await Assert.That(sut.IsFetching).IsTrue();
    }

    [Test]
    public async Task Ctor_FetchingWithLastData_SetsIsLoadingFalse()
    {
        using var sut = CreateSut(QueryState<string>.CreateFetching(lastData: "old"));

        await Assert.That(sut.IsLoading).IsFalse();
        await Assert.That(sut.IsFetching).IsTrue();
    }

    [Test]
    public async Task DisplayData_FetchingWithLastData_FallsBackToLastData()
    {
        using var sut = CreateSut(QueryState<string>.CreateFetching(lastData: "old"));

        await Assert.That(sut.Data).IsNull();
        await Assert.That(sut.DisplayData).IsEqualTo("old");
    }

    [Test]
    public async Task PropertyChanged_OnFetchingToSuccess_RaisesOnlyChangedProperties()
    {
        using var sut = CreateSut(QueryState<string>.CreateFetching());
        var raised = CollectPropertyChanges(sut);

        _stateSubject.OnNext(QueryState<string>.CreateSuccess("hello"));
        _uiContext.DrainAll();

        await Assert.That(raised).Contains(nameof(sut.CurrentState));
        await Assert.That(raised).Contains(nameof(sut.Status));
        await Assert.That(raised).Contains(nameof(sut.IsFetching));
        await Assert.That(raised).Contains(nameof(sut.IsSuccess));
        await Assert.That(raised).Contains(nameof(sut.IsLoading));
        await Assert.That(raised).Contains(nameof(sut.Data));
        await Assert.That(raised).Contains(nameof(sut.HasData));
        await Assert.That(raised).Contains(nameof(sut.DisplayData));

        // Unchanged between Fetching and Success — must not be raised.
        await Assert.That(raised).DoesNotContain(nameof(sut.IsIdle));
        await Assert.That(raised).DoesNotContain(nameof(sut.IsFailure));
        await Assert.That(raised).DoesNotContain(nameof(sut.Error));
        await Assert.That(raised).DoesNotContain(nameof(sut.HasError));
        await Assert.That(raised).DoesNotContain(nameof(sut.LastData));
    }

    [Test]
    public async Task PropertyChanged_HandlerReadingSiblings_SeesConsistentSnapshot()
    {
        using var sut = CreateSut(QueryState<string>.CreateFetching());
        var observedPairs = new List<(QueryStatus Status, string? Data)>();
        sut.PropertyChanged += (_, _) => observedPairs.Add((sut.Status, sut.Data));

        _stateSubject.OnNext(QueryState<string>.CreateSuccess("hello"));
        _uiContext.DrainAll();

        // Every notification must observe the fully applied new snapshot — never a torn mix.
        await Assert.That(observedPairs.Count).IsGreaterThan(0);
        await Assert.That(observedPairs.All(p => p is { Status: QueryStatus.Success, Data: "hello" })).IsTrue();
    }

    [Test]
    public async Task BurstEmissions_BeforeDrain_CoalesceToSingleApplyOfLatestState()
    {
        using var sut = CreateSut(QueryState<string>.CreateIdle());
        var raised = CollectPropertyChanges(sut);

        _stateSubject.OnNext(QueryState<string>.CreateFetching());
        _stateSubject.OnNext(QueryState<string>.CreateSuccess("hello"));

        await Assert.That(_uiContext.PostCount).IsEqualTo(1);

        _uiContext.DrainAll();

        await Assert.That(sut.IsSuccess).IsTrue();
        await Assert.That(sut.Data).IsEqualTo("hello");
        // The intermediate Fetching state was skipped: Idle -> Success never flips IsFetching.
        await Assert.That(raised).DoesNotContain(nameof(sut.IsFetching));
    }

    [Test]
    public async Task Failure_WithError_ExposesErrorAndFlags()
    {
        using var sut = CreateSut(QueryState<string>.CreateFetching());
        var error = new InvalidOperationException("boom");

        _stateSubject.OnNext(QueryState<string>.CreateFailure(error));
        _uiContext.DrainAll();

        await Assert.That(sut.IsFailure).IsTrue();
        await Assert.That(sut.HasError).IsTrue();
        await Assert.That(sut.Error).IsSameReferenceAs(error);
    }
}
