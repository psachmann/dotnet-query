namespace DotNetQuery.Mvvm.Tests;

public class MutationViewModelLifecycleTests
{
    private readonly Mock<IMutation<int, string>> _mutationMock = Mock.Of<IMutation<int, string>>();
    private readonly RecordingSynchronizationContext _uiContext = new();
    private BehaviorSubject<MutationState<string>> _stateSubject = default!;

    [After(Test)]
    public void Teardown()
    {
        _stateSubject.OnCompleted();
        _stateSubject.Dispose();
    }

    private MutationViewModel<int, string> CreateSut(MutationState<string> initialState)
    {
        _stateSubject = new(initialState);
        _mutationMock.CurrentState.Returns(_stateSubject.Value);
        _mutationMock.State.Returns(_stateSubject.AsObservable());

        return new MutationViewModel<int, string>(
            _mutationMock.Object,
            new SynchronizationContextUiDispatcher(_uiContext)
        );
    }

    [Test]
    public async Task Ctor_SubscribesToState()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());

        await Assert.That(_stateSubject.HasObservers).IsTrue();
    }

    [Test]
    public async Task Dispose_WithWrappedMutation_ReleasesSubscriptionButNotMutation()
    {
        var sut = CreateSut(MutationState<string>.CreateIdle());

        sut.Dispose();

        await Assert.That(_stateSubject.HasObservers).IsFalse();
        await Assert
            .That(Mock.Invocations(_mutationMock).Any(i => i.MemberName == nameof(IDisposable.Dispose)))
            .IsFalse();
    }

    private MutationViewModel<int, string> CreateClientOwnedSut()
    {
        _stateSubject = new(MutationState<string>.CreateIdle());
        _mutationMock.CurrentState.Returns(_stateSubject.Value);
        _mutationMock.State.Returns(_stateSubject.AsObservable());

        var clientMock = Mock.Of<IQueryClient>();
        clientMock.CreateMutation(Any<MutationOptions<int, string>>()).Returns(_mutationMock.Object);

        var options = new MutationOptions<int, string> { Mutator = (_, _) => Task.FromResult("ok") };

        return new MutationViewModel<int, string>(
            clientMock.Object,
            options,
            new SynchronizationContextUiDispatcher(_uiContext)
        );
    }

    [Test]
    public async Task Dispose_WithClientCreatedMutation_DisposesMutation()
    {
        var sut = CreateClientOwnedSut();

        sut.Dispose();

        await Assert.That(_stateSubject.HasObservers).IsFalse();
        await Assert
            .That(Mock.Invocations(_mutationMock).Any(i => i.MemberName == nameof(IDisposable.Dispose)))
            .IsTrue();
    }

    [Test]
    public async Task Dispose_CalledTwice_DoesNotThrow()
    {
        var sut = CreateSut(MutationState<string>.CreateIdle());

        sut.Dispose();
        sut.Dispose();

        await Assert.That(_stateSubject.HasObservers).IsFalse();
    }

    [Test]
    public async Task PendingPost_DrainedAfterDispose_DoesNotRaisePropertyChanged()
    {
        var sut = CreateSut(MutationState<string>.CreateIdle());
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _stateSubject.OnNext(MutationState<string>.CreateRunning());
        sut.Dispose();
        _uiContext.DrainAll();

        await Assert.That(raised).IsEmpty();
        await Assert.That(sut.IsRunning).IsFalse();
    }
}
