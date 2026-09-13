namespace DotNetQuery.Mvvm.Tests;

public class QueryViewModelExtensionsTests
{
    private readonly RecordingSynchronizationContext _uiContext = new();

    [Test]
    public async Task ToViewModel_OnQuery_WrapsWithoutTakingOwnership()
    {
        var queryMock = Mock.Of<IQuery<int, string>>();
        var stateSubject = new BehaviorSubject<QueryState<string>>(QueryState<string>.CreateIdle());
        queryMock.CurrentState.Returns(stateSubject.Value);
        queryMock.State.Returns(stateSubject.AsObservable());

        var sut = queryMock.Object.ToViewModel(new SynchronizationContextUiDispatcher(_uiContext));

        await Assert.That(sut.Query).IsSameReferenceAs(queryMock.Object);

        sut.Dispose();

        await Assert.That(Mock.Invocations(queryMock).Any(i => i.MemberName == nameof(IDisposable.Dispose))).IsFalse();

        stateSubject.OnCompleted();
        stateSubject.Dispose();
    }

    [Test]
    public async Task ToViewModel_OnInfiniteQuery_WrapsWithoutTakingOwnership()
    {
        var queryMock = Mock.Of<IInfiniteQuery<int, string, int>>();
        var stateSubject = new BehaviorSubject<InfiniteQueryState<string, int>>(
            InfiniteQueryState<string, int>.CreateIdle()
        );
        queryMock.CurrentState.Returns(stateSubject.Value);
        queryMock.State.Returns(stateSubject.AsObservable());

        var sut = queryMock.Object.ToViewModel(new SynchronizationContextUiDispatcher(_uiContext));

        await Assert.That(sut.Query).IsSameReferenceAs(queryMock.Object);

        sut.Dispose();

        await Assert.That(Mock.Invocations(queryMock).Any(i => i.MemberName == nameof(IDisposable.Dispose))).IsFalse();

        stateSubject.OnCompleted();
        stateSubject.Dispose();
    }

    [Test]
    public async Task ToViewModel_OnMutation_WrapsWithoutTakingOwnership()
    {
        var mutationMock = Mock.Of<IMutation<int, string>>();
        var stateSubject = new BehaviorSubject<MutationState<string>>(MutationState<string>.CreateIdle());
        mutationMock.CurrentState.Returns(stateSubject.Value);
        mutationMock.State.Returns(stateSubject.AsObservable());

        var sut = mutationMock.Object.ToViewModel(new SynchronizationContextUiDispatcher(_uiContext));

        await Assert.That(sut.Mutation).IsSameReferenceAs(mutationMock.Object);

        sut.Dispose();

        await Assert
            .That(Mock.Invocations(mutationMock).Any(i => i.MemberName == nameof(IDisposable.Dispose)))
            .IsFalse();

        stateSubject.OnCompleted();
        stateSubject.Dispose();
    }

    [Test]
    public async Task ObserveOnUi_EachEmission_PostsSeparately_NoCoalescing()
    {
        var source = new Subject<int>();
        var received = new List<int>();
        using var subscription = source
            .ObserveOnUi(new SynchronizationContextUiDispatcher(_uiContext))
            .Subscribe(received.Add);

        source.OnNext(1);
        source.OnNext(2);
        source.OnNext(3);

        // Contrast with UiStateBinding: every emission gets its own post, none are collapsed.
        await Assert.That(_uiContext.PostCount).IsEqualTo(3);
        await Assert.That(received).IsEmpty();

        _uiContext.DrainAll();

        await Assert.That(received).IsEquivalentTo(new[] { 1, 2, 3 });
    }

    [Test]
    public async Task ObserveOnUi_OnError_ForwardsErrorAfterDrain()
    {
        var source = new Subject<int>();
        Exception? received = null;
        using var subscription = source
            .ObserveOnUi(new SynchronizationContextUiDispatcher(_uiContext))
            .Subscribe(_ => { }, error => received = error);
        var exception = new InvalidOperationException("boom");

        source.OnError(exception);

        await Assert.That(received).IsNull();

        _uiContext.DrainAll();

        await Assert.That(received).IsSameReferenceAs(exception);
    }

    [Test]
    public async Task ObserveOnUi_OnCompleted_ForwardsAfterDrain()
    {
        var source = new Subject<int>();
        var completed = false;
        using var subscription = source
            .ObserveOnUi(new SynchronizationContextUiDispatcher(_uiContext))
            .Subscribe(_ => { }, () => completed = true);

        source.OnCompleted();

        await Assert.That(completed).IsFalse();

        _uiContext.DrainAll();

        await Assert.That(completed).IsTrue();
    }

    [Test]
    public async Task ObserveOnUi_DisposedBeforeDrain_PendingPostBecomesNoOp()
    {
        var source = new Subject<int>();
        var received = new List<int>();
        var subscription = source
            .ObserveOnUi(new SynchronizationContextUiDispatcher(_uiContext))
            .Subscribe(received.Add);

        source.OnNext(1);
        subscription.Dispose();
        _uiContext.DrainAll();

        await Assert.That(received).IsEmpty();
    }

    [Test]
    public async Task ObserveOnUi_DispatcherCapturedAtCallTime_NotAtSubscribeTime()
    {
        var callTimeContext = new RecordingSynchronizationContext();
        var subscribeTimeContext = new RecordingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        var source = new Subject<int>();

        SynchronizationContext.SetSynchronizationContext(callTimeContext);
        var observable = source.ObserveOnUi<int>();

        try
        {
            SynchronizationContext.SetSynchronizationContext(subscribeTimeContext);
            using var subscription = observable.Subscribe(_ => { });

            source.OnNext(1);

            await Assert.That(callTimeContext.PostCount).IsEqualTo(1);
            await Assert.That(subscribeTimeContext.PostCount).IsEqualTo(0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
