namespace DotNetQuery.Mvvm.Tests;

public class InfiniteQueryViewModelCommandTests
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

    [Test]
    public async Task RefetchCommand_WhileFetching_CannotExecute()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateFetching([], [], false, false));

        await Assert.That(sut.RefetchCommand.CanExecute(null)).IsFalse();
        await Assert.That(sut.CancelCommand.CanExecute(null)).IsTrue();
    }

    [Test]
    public async Task RefetchCommand_WhileIdle_CanExecute()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateIdle());

        await Assert.That(sut.RefetchCommand.CanExecute(null)).IsTrue();
        await Assert.That(sut.CancelCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task RefetchCommand_WhileFetchingNextPage_CannotExecute()
    {
        // CreateFetchingNext reports Status == Success, so IsFetching alone would miss this.
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateFetchingNext(["page0"], [0], true, false));

        await Assert.That(sut.RefetchCommand.CanExecute(null)).IsFalse();
        await Assert.That(sut.CancelCommand.CanExecute(null)).IsTrue();
    }

    [Test]
    public async Task FetchNextPageCommand_WithNextPageAvailableAndIdle_CanExecute()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateSuccess(["page0"], [0], true, false));

        await Assert.That(sut.FetchNextPageCommand.CanExecute(null)).IsTrue();
    }

    [Test]
    public async Task FetchNextPageCommand_WithNoNextPage_CannotExecute()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateSuccess(["page0"], [0], false, false));

        await Assert.That(sut.FetchNextPageCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task FetchNextPageCommand_WhileFetchingNextPage_CannotExecute()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateFetchingNext(["page0"], [0], true, false));

        await Assert.That(sut.FetchNextPageCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task FetchNextPageCommand_WhileFetchingPreviousPage_CannotExecute()
    {
        // "Any fetch in flight" blocks FetchNextPage too, not just its own direction.
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateFetchingPrevious(["page0"], [0], true, true));

        await Assert.That(sut.FetchNextPageCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task FetchPreviousPageCommand_WithPreviousPageAvailableAndIdle_CanExecute()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateSuccess(["page0"], [0], false, true));

        await Assert.That(sut.FetchPreviousPageCommand.CanExecute(null)).IsTrue();
    }

    [Test]
    public async Task FetchPreviousPageCommand_WithNoPreviousPage_CannotExecute()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateSuccess(["page0"], [0], false, false));

        await Assert.That(sut.FetchPreviousPageCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task CanExecuteChanged_OnIsFetchingFlip_RaisedDuringDrainForAllCommands()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateIdle());
        var refetchRaised = 0;
        var cancelRaised = 0;
        var nextRaised = 0;
        var previousRaised = 0;
        sut.RefetchCommand.CanExecuteChanged += (_, _) => refetchRaised++;
        sut.CancelCommand.CanExecuteChanged += (_, _) => cancelRaised++;
        sut.FetchNextPageCommand.CanExecuteChanged += (_, _) => nextRaised++;
        sut.FetchPreviousPageCommand.CanExecuteChanged += (_, _) => previousRaised++;

        _stateSubject.OnNext(InfiniteQueryState<string, int>.CreateFetching([], [], false, false));

        await Assert.That(refetchRaised).IsEqualTo(0);

        _uiContext.DrainAll();

        await Assert.That(refetchRaised).IsEqualTo(1);
        await Assert.That(cancelRaised).IsEqualTo(1);
        await Assert.That(nextRaised).IsEqualTo(1);
        await Assert.That(previousRaised).IsEqualTo(1);
    }

    [Test]
    public async Task CanExecuteChanged_OnHasNextPageFlipWithoutFetchChange_RaisedForNextPageCommandOnly()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateSuccess(["page0"], [0], false, false));
        var nextRaised = 0;
        var refetchRaised = 0;
        sut.FetchNextPageCommand.CanExecuteChanged += (_, _) => nextRaised++;
        sut.RefetchCommand.CanExecuteChanged += (_, _) => refetchRaised++;

        _stateSubject.OnNext(InfiniteQueryState<string, int>.CreateSuccess(["page0", "page1"], [0, 1], true, false));
        _uiContext.DrainAll();

        await Assert.That(nextRaised).IsEqualTo(1);
        await Assert.That(refetchRaised).IsEqualTo(0);
    }

    [Test]
    public async Task RefetchCommand_Execute_CallsQueryRefetch()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateIdle());

        sut.RefetchCommand.Execute(null);

        await Assert.That(Mock.Invocations(_queryMock).Any(i => i.MemberName == nameof(IQuery.Refetch))).IsTrue();
    }

    [Test]
    public async Task CancelCommand_Execute_CallsQueryCancel()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateFetching([], [], false, false));

        sut.CancelCommand.Execute(null);

        await Assert.That(Mock.Invocations(_queryMock).Any(i => i.MemberName == nameof(IQuery.Cancel))).IsTrue();
    }

    [Test]
    public async Task FetchNextPageCommand_Execute_CallsQueryFetchNextPage()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateSuccess(["page0"], [0], true, false));

        sut.FetchNextPageCommand.Execute(null);

        await Assert
            .That(
                Mock.Invocations(_queryMock)
                    .Any(i => i.MemberName == nameof(IInfiniteQuery<int, string, int>.FetchNextPage))
            )
            .IsTrue();
    }

    [Test]
    public async Task FetchPreviousPageCommand_Execute_CallsQueryFetchPreviousPage()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateSuccess(["page0"], [0], false, true));

        sut.FetchPreviousPageCommand.Execute(null);

        await Assert
            .That(
                Mock.Invocations(_queryMock)
                    .Any(i => i.MemberName == nameof(IInfiniteQuery<int, string, int>.FetchPreviousPage))
            )
            .IsTrue();
    }
}
