namespace DotNetQuery.Mvvm.Tests;

using System.ComponentModel;

public class InfiniteQueryViewModelStateTests
{
    private readonly Mock<IInfiniteQuery<int, string, int>> _queryMock = Mock.Of<IInfiniteQuery<int, string, int>>();
    private readonly RecordingSynchronizationContext _uiContext = new();
    private BehaviorSubject<InfiniteQueryState<string, int>> _stateSubject = default!;

    [After(Test)]
    public void Teardown()
    {
        _stateSubject.OnCompleted();
        _stateSubject.Dispose();
    }

    private InfiniteQueryViewModel<int, string, int> CreateSut(InfiniteQueryState<string, int> initialState)
    {
        _stateSubject = new(initialState);
        _queryMock.CurrentState.Returns(_stateSubject.Value);
        _queryMock.State.Returns(_stateSubject.AsObservable());

        return new InfiniteQueryViewModel<int, string, int>(
            _queryMock.Object,
            new SynchronizationContextUiDispatcher(_uiContext)
        );
    }

    private static List<string?> CollectPropertyChanges(InfiniteQueryViewModel<int, string, int> sut)
    {
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        return raised;
    }

    [Test]
    public async Task Ctor_WithSuccessCurrentState_ExposesPagesSynchronously()
    {
        IReadOnlyList<string> pages = ["page0"];
        IReadOnlyList<int> pageParams = [0];
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateSuccess(pages, pageParams, true, false));

        // No DrainAll — the initial state must be visible before any dispatcher hop.
        await Assert.That(sut.Pages).IsEqualTo(pages);
        await Assert.That(sut.IsSuccess).IsTrue();
        await Assert.That(sut.HasNextPage).IsTrue();
        await Assert.That(sut.HasData).IsTrue();
    }

    [Test]
    public async Task StateEmission_BeforeContextDrains_DoesNotNotify()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateIdle());
        var raised = CollectPropertyChanges(sut);

        _stateSubject.OnNext(InfiniteQueryState<string, int>.CreateFetching([], [], false, false));

        await Assert.That(sut.IsFetching).IsFalse();
        await Assert.That(raised).IsEmpty();

        _uiContext.DrainAll();

        await Assert.That(sut.IsFetching).IsTrue();
        await Assert.That(raised).Contains(nameof(sut.IsFetching));
    }

    [Test]
    public async Task Ctor_FetchingWithNoPages_SetsIsLoadingTrue()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateFetching([], [], false, false));

        await Assert.That(sut.IsLoading).IsTrue();
        await Assert.That(sut.IsFetching).IsTrue();
    }

    [Test]
    public async Task Ctor_FetchingWithExistingPages_SetsIsLoadingFalse()
    {
        IReadOnlyList<string> pages = ["old"];
        IReadOnlyList<int> pageParams = [0];
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateFetching(pages, pageParams, true, false));

        await Assert.That(sut.IsLoading).IsFalse();
        await Assert.That(sut.IsFetching).IsTrue();
        await Assert.That(sut.Pages).IsEqualTo(pages);
    }

    [Test]
    public async Task PropertyChanged_OnFetchingToSuccess_RaisesOnlyChangedProperties()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateFetching([], [], false, false));
        var raised = CollectPropertyChanges(sut);

        IReadOnlyList<string> pages = ["page0"];
        IReadOnlyList<int> pageParams = [0];
        _stateSubject.OnNext(InfiniteQueryState<string, int>.CreateSuccess(pages, pageParams, true, false));
        _uiContext.DrainAll();

        await Assert.That(raised).Contains(nameof(sut.CurrentState));
        await Assert.That(raised).Contains(nameof(sut.Status));
        await Assert.That(raised).Contains(nameof(sut.IsFetching));
        await Assert.That(raised).Contains(nameof(sut.IsSuccess));
        await Assert.That(raised).Contains(nameof(sut.IsLoading));
        await Assert.That(raised).Contains(nameof(sut.Pages));
        await Assert.That(raised).Contains(nameof(sut.PageParams));
        await Assert.That(raised).Contains(nameof(sut.HasData));
        await Assert.That(raised).Contains(nameof(sut.HasNextPage));

        // Unchanged between Fetching and Success — must not be raised.
        await Assert.That(raised).DoesNotContain(nameof(sut.IsIdle));
        await Assert.That(raised).DoesNotContain(nameof(sut.IsFailure));
        await Assert.That(raised).DoesNotContain(nameof(sut.Error));
        await Assert.That(raised).DoesNotContain(nameof(sut.HasError));
        await Assert.That(raised).DoesNotContain(nameof(sut.HasPreviousPage));
    }

    [Test]
    public async Task PropertyChanged_HandlerReadingSiblings_SeesConsistentSnapshot()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateFetching([], [], false, false));
        var observedPairs = new List<(QueryStatus Status, int PageCount)>();
        sut.PropertyChanged += (_, _) => observedPairs.Add((sut.Status, sut.Pages.Count));

        _stateSubject.OnNext(InfiniteQueryState<string, int>.CreateSuccess(["page0"], [0], true, false));
        _uiContext.DrainAll();

        // Every notification must observe the fully applied new snapshot — never a torn mix.
        await Assert.That(observedPairs.Count).IsGreaterThan(0);
        await Assert.That(observedPairs.All(p => p is { Status: QueryStatus.Success, PageCount: 1 })).IsTrue();
    }

    [Test]
    public async Task BurstEmissions_BeforeDrain_CoalesceToSingleApplyOfLatestState()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateIdle());
        var raised = CollectPropertyChanges(sut);

        _stateSubject.OnNext(InfiniteQueryState<string, int>.CreateFetching([], [], false, false));
        _stateSubject.OnNext(InfiniteQueryState<string, int>.CreateSuccess(["page0"], [0], true, false));

        await Assert.That(_uiContext.PostCount).IsEqualTo(1);

        _uiContext.DrainAll();

        await Assert.That(sut.IsSuccess).IsTrue();
        await Assert.That(sut.Pages).IsEquivalentTo(new[] { "page0" });
        // The intermediate Fetching state was skipped: Idle -> Success never flips IsFetching.
        await Assert.That(raised).DoesNotContain(nameof(sut.IsFetching));
    }

    [Test]
    public async Task Failure_WithError_ExposesErrorAndFlags()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateFetching([], [], false, false));
        var error = new InvalidOperationException("boom");

        _stateSubject.OnNext(InfiniteQueryState<string, int>.CreateFailure(error, [], [], false, false));
        _uiContext.DrainAll();

        await Assert.That(sut.IsFailure).IsTrue();
        await Assert.That(sut.HasError).IsTrue();
        await Assert.That(sut.Error).IsSameReferenceAs(error);
    }

    [Test]
    public async Task FetchingNext_ReportsIsFetchingNextPageWithoutSettingIsFetching()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateSuccess(["page0"], [0], true, false));

        _stateSubject.OnNext(InfiniteQueryState<string, int>.CreateFetchingNext(["page0"], [0], true, false));
        _uiContext.DrainAll();

        await Assert.That(sut.IsFetchingNextPage).IsTrue();
        await Assert.That(sut.IsFetching).IsFalse();
    }

    [Test]
    public async Task StateChanged_OnAppliedState_IsRaisedAfterEveryPropertyChanged()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateFetching([], [], false, false));
        var events = new List<string>();
        sut.PropertyChanged += (_, e) => events.Add(e.PropertyName!);
        sut.StateChanged += (_, _) => events.Add(nameof(sut.StateChanged));

        _stateSubject.OnNext(InfiniteQueryState<string, int>.CreateSuccess(["page0"], [0], true, false));
        _uiContext.DrainAll();

        await Assert.That(events).Contains(nameof(sut.StateChanged));
        await Assert.That(events[^1]).IsEqualTo(nameof(sut.StateChanged));
    }

    [Test]
    public async Task StateChanged_WhenStateDoesNotActuallyChange_IsNotRaised()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateIdle());
        var raisedStateChanged = false;
        sut.StateChanged += (_, _) => raisedStateChanged = true;

        _stateSubject.OnNext(_stateSubject.Value);
        _uiContext.DrainAll();

        await Assert.That(raisedStateChanged).IsFalse();
    }

    [Test]
    public async Task StateChanged_BurstEmissionsBeforeDrain_RaisesOnceForTheCoalescedState()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateIdle());
        var raiseCount = 0;
        sut.StateChanged += (_, _) => raiseCount++;

        _stateSubject.OnNext(InfiniteQueryState<string, int>.CreateFetching([], [], false, false));
        _stateSubject.OnNext(InfiniteQueryState<string, int>.CreateSuccess(["page0"], [0], true, false));
        _uiContext.DrainAll();

        await Assert.That(raiseCount).IsEqualTo(1);
    }
}
