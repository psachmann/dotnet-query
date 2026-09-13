namespace DotNetQuery.Mvvm.Tests;

public class InfiniteQueryViewModelLifecycleTests
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
    public async Task Ctor_SubscribesToState()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateIdle());

        await Assert.That(_stateSubject.HasObservers).IsTrue();
    }

    [Test]
    public async Task Dispose_WithWrappedQuery_ReleasesSubscriptionButNotQuery()
    {
        var sut = CreateSut(InfiniteQueryState<string, int>.CreateIdle());

        sut.Dispose();

        await Assert.That(_stateSubject.HasObservers).IsFalse();
        await Assert.That(Mock.Invocations(_queryMock).Any(i => i.MemberName == nameof(IDisposable.Dispose))).IsFalse();
    }

    private InfiniteQueryViewModel<int, string, int> CreateClientOwnedSut()
    {
        _stateSubject = new(InfiniteQueryState<string, int>.CreateIdle());
        _queryMock.CurrentState.Returns(_stateSubject.Value);
        _queryMock.State.Returns(_stateSubject.AsObservable());

        var clientMock = Mock.Of<IQueryClient>();
        clientMock.CreateInfiniteQuery(Any<InfiniteQueryOptions<int, string, int>>()).Returns(_queryMock.Object);

        var options = new InfiniteQueryOptions<int, string, int>
        {
            KeyFactory = args => QueryKey.From("test", args),
            Fetcher = (_, page, _) => Task.FromResult($"page{page}"),
            InitialPageParam = 0,
            GetNextPageParam = info => info.PageParam + 1,
        };

        return new InfiniteQueryViewModel<int, string, int>(
            clientMock.Object,
            options,
            new SynchronizationContextUiDispatcher(_uiContext)
        );
    }

    [Test]
    public async Task Dispose_WithClientCreatedQuery_DisposesQuery()
    {
        var sut = CreateClientOwnedSut();

        sut.Dispose();

        await Assert.That(_stateSubject.HasObservers).IsFalse();
        await Assert.That(Mock.Invocations(_queryMock).Any(i => i.MemberName == nameof(IDisposable.Dispose))).IsTrue();
    }

    [Test]
    public async Task Dispose_CalledTwice_DoesNotThrow()
    {
        var sut = CreateSut(InfiniteQueryState<string, int>.CreateIdle());

        sut.Dispose();
        sut.Dispose();

        await Assert.That(_stateSubject.HasObservers).IsFalse();
    }

    [Test]
    public async Task PendingPost_DrainedAfterDispose_DoesNotRaisePropertyChanged()
    {
        var sut = CreateSut(InfiniteQueryState<string, int>.CreateIdle());
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _stateSubject.OnNext(InfiniteQueryState<string, int>.CreateFetching([], [], false, false));
        sut.Dispose();
        _uiContext.DrainAll();

        await Assert.That(raised).IsEmpty();
        await Assert.That(sut.IsFetching).IsFalse();
    }

    [Test]
    public async Task SetArgs_DelegatesToQuery()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateIdle());

        sut.SetArgs(42);

        var invocation = Mock.Invocations(_queryMock)
            .Single(i => i.MemberName == nameof(IInfiniteQuery<int, string, int>.SetArgs));
        await Assert.That(invocation.Arguments[0]).IsEqualTo(42);
    }

    [Test]
    public async Task SetEnabled_DelegatesToQuery()
    {
        using var sut = CreateSut(InfiniteQueryState<string, int>.CreateIdle());

        sut.SetEnabled(false);

        var invocation = Mock.Invocations(_queryMock)
            .Single(i => i.MemberName == nameof(IInfiniteQuery<int, string, int>.SetEnabled));
        await Assert.That((bool)invocation.Arguments[0]!).IsFalse();
    }
}
