namespace DotNetQuery.Mvvm.Tests;

public class QueryViewModelCommandTests
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

    [Test]
    public async Task RefetchCommand_WhileFetching_CannotExecute()
    {
        using var sut = CreateSut(QueryState<string>.CreateFetching());

        await Assert.That(sut.RefetchCommand.CanExecute(null)).IsFalse();
        await Assert.That(sut.CancelCommand.CanExecute(null)).IsTrue();
    }

    [Test]
    public async Task RefetchCommand_WhileIdle_CanExecute()
    {
        using var sut = CreateSut(QueryState<string>.CreateIdle());

        await Assert.That(sut.RefetchCommand.CanExecute(null)).IsTrue();
        await Assert.That(sut.CancelCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task CanExecuteChanged_OnIsFetchingFlip_RaisedDuringDrain()
    {
        using var sut = CreateSut(QueryState<string>.CreateIdle());
        var refetchRaised = 0;
        var cancelRaised = 0;
        sut.RefetchCommand.CanExecuteChanged += (_, _) => refetchRaised++;
        sut.CancelCommand.CanExecuteChanged += (_, _) => cancelRaised++;

        _stateSubject.OnNext(QueryState<string>.CreateFetching());

        await Assert.That(refetchRaised).IsEqualTo(0);
        await Assert.That(cancelRaised).IsEqualTo(0);

        _uiContext.DrainAll();

        await Assert.That(refetchRaised).IsEqualTo(1);
        await Assert.That(cancelRaised).IsEqualTo(1);
    }

    [Test]
    public async Task RefetchCommand_Execute_CallsQueryRefetch()
    {
        using var sut = CreateSut(QueryState<string>.CreateIdle());

        sut.RefetchCommand.Execute(null);

        await Assert.That(Mock.Invocations(_queryMock).Any(i => i.MemberName == nameof(IQuery.Refetch))).IsTrue();
    }

    [Test]
    public async Task CancelCommand_Execute_CallsQueryCancel()
    {
        using var sut = CreateSut(QueryState<string>.CreateFetching());

        sut.CancelCommand.Execute(null);

        await Assert.That(Mock.Invocations(_queryMock).Any(i => i.MemberName == nameof(IQuery.Cancel))).IsTrue();
    }
}
