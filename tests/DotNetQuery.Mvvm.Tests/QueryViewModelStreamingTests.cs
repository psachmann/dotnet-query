namespace DotNetQuery.Mvvm.Tests;

public class QueryViewModelStreamingTests
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
    public async Task Streaming_ExposesGrowingDataAndIsStreaming()
    {
        using var sut = CreateSut(QueryState<string>.CreateFetching());
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _stateSubject.OnNext(QueryState<string>.CreateStreaming("Hel"));
        _uiContext.DrainAll();

        using var _ = Assert.Multiple();
        await Assert.That(sut.IsStreaming).IsTrue();
        await Assert.That(sut.IsSuccess).IsTrue();
        await Assert.That(sut.Data).IsEqualTo("Hel");
        await Assert.That(raised).Contains(nameof(sut.IsStreaming));
    }

    [Test]
    public async Task StreamCompletion_RaisesIsStreaming_ButNotData_WhenDataUnchanged()
    {
        using var sut = CreateSut(QueryState<string>.CreateStreaming("Hello"));
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _stateSubject.OnNext(QueryState<string>.CreateSuccess("Hello"));
        _uiContext.DrainAll();

        using var _ = Assert.Multiple();
        await Assert.That(sut.IsStreaming).IsFalse();
        await Assert.That(raised).Contains(nameof(sut.IsStreaming));
        await Assert.That(raised).DoesNotContain(nameof(sut.Data));
    }

    [Test]
    public async Task WhileStreaming_CancelIsEnabled_AndRefetchIsDisabled()
    {
        using var sut = CreateSut(QueryState<string>.CreateStreaming("partial"));

        using var _ = Assert.Multiple();
        await Assert.That(sut.CancelCommand.CanExecute(null)).IsTrue();
        await Assert.That(sut.RefetchCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task FetchingToStreaming_DoesNotRaiseCanExecuteChanged()
    {
        using var sut = CreateSut(QueryState<string>.CreateFetching());
        var raised = 0;
        sut.CancelCommand.CanExecuteChanged += (_, _) => raised++;

        _stateSubject.OnNext(QueryState<string>.CreateStreaming("a"));
        _uiContext.DrainAll();

        await Assert.That(raised).IsEqualTo(0);
    }

    [Test]
    public async Task StreamCompletion_RaisesCanExecuteChanged()
    {
        using var sut = CreateSut(QueryState<string>.CreateStreaming("a"));
        var raised = 0;
        sut.CancelCommand.CanExecuteChanged += (_, _) => raised++;

        _stateSubject.OnNext(QueryState<string>.CreateSuccess("a"));
        _uiContext.DrainAll();

        using var _ = Assert.Multiple();
        await Assert.That(raised).IsEqualTo(1);
        await Assert.That(sut.CancelCommand.CanExecute(null)).IsFalse();
    }
}
