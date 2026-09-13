namespace DotNetQuery.Mvvm.Tests;

public class MutationViewModelStateTests
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

    private static List<string?> CollectPropertyChanges(MutationViewModel<int, string> sut)
    {
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        return raised;
    }

    [Test]
    public async Task Ctor_WithRunningCurrentState_ExposesIsRunningSynchronously()
    {
        using var sut = CreateSut(MutationState<string>.CreateRunning());

        // No DrainAll — the initial state must be visible before any dispatcher hop.
        await Assert.That(sut.IsRunning).IsTrue();
        await Assert.That(sut.IsIdle).IsFalse();
    }

    [Test]
    public async Task StateEmission_BeforeContextDrains_DoesNotNotify()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());
        var raised = CollectPropertyChanges(sut);

        _stateSubject.OnNext(MutationState<string>.CreateRunning());

        await Assert.That(sut.IsRunning).IsFalse();
        await Assert.That(raised).IsEmpty();

        _uiContext.DrainAll();

        await Assert.That(sut.IsRunning).IsTrue();
        await Assert.That(raised).Contains(nameof(sut.IsRunning));
    }

    [Test]
    public async Task PropertyChanged_OnRunningToSuccess_RaisesOnlyChangedProperties()
    {
        using var sut = CreateSut(MutationState<string>.CreateRunning());
        var raised = CollectPropertyChanges(sut);

        _stateSubject.OnNext(MutationState<string>.CreateSuccess("result"));
        _uiContext.DrainAll();

        await Assert.That(raised).Contains(nameof(sut.CurrentState));
        await Assert.That(raised).Contains(nameof(sut.Status));
        await Assert.That(raised).Contains(nameof(sut.IsRunning));
        await Assert.That(raised).Contains(nameof(sut.IsSuccess));
        await Assert.That(raised).Contains(nameof(sut.Data));
        await Assert.That(raised).Contains(nameof(sut.HasData));

        // Unchanged between Running and Success — must not be raised.
        await Assert.That(raised).DoesNotContain(nameof(sut.IsIdle));
        await Assert.That(raised).DoesNotContain(nameof(sut.IsFailure));
        await Assert.That(raised).DoesNotContain(nameof(sut.Error));
        await Assert.That(raised).DoesNotContain(nameof(sut.HasError));
    }

    [Test]
    public async Task PropertyChanged_HandlerReadingSiblings_SeesConsistentSnapshot()
    {
        using var sut = CreateSut(MutationState<string>.CreateRunning());
        var observedPairs = new List<(MutationStatus Status, string? Data)>();
        sut.PropertyChanged += (_, _) => observedPairs.Add((sut.Status, sut.Data));

        _stateSubject.OnNext(MutationState<string>.CreateSuccess("result"));
        _uiContext.DrainAll();

        // Every notification must observe the fully applied new snapshot — never a torn mix.
        await Assert.That(observedPairs.Count).IsGreaterThan(0);
        await Assert.That(observedPairs.All(p => p is { Status: MutationStatus.Success, Data: "result" })).IsTrue();
    }

    [Test]
    public async Task BurstEmissions_BeforeDrain_CoalesceToSingleApplyOfLatestState()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());
        var raised = CollectPropertyChanges(sut);

        _stateSubject.OnNext(MutationState<string>.CreateRunning());
        _stateSubject.OnNext(MutationState<string>.CreateSuccess("result"));

        await Assert.That(_uiContext.PostCount).IsEqualTo(1);

        _uiContext.DrainAll();

        await Assert.That(sut.IsSuccess).IsTrue();
        await Assert.That(sut.Data).IsEqualTo("result");
        // The intermediate Running state was skipped: Idle -> Success never flips IsRunning.
        await Assert.That(raised).DoesNotContain(nameof(sut.IsRunning));
    }

    [Test]
    public async Task Failure_WithError_ExposesErrorAndFlags()
    {
        using var sut = CreateSut(MutationState<string>.CreateRunning());
        var error = new InvalidOperationException("boom");

        _stateSubject.OnNext(MutationState<string>.CreateFailure(error));
        _uiContext.DrainAll();

        await Assert.That(sut.IsFailure).IsTrue();
        await Assert.That(sut.HasError).IsTrue();
        await Assert.That(sut.Error).IsSameReferenceAs(error);
    }

    [Test]
    public async Task StateChanged_OnAppliedState_IsRaisedAfterEveryPropertyChanged()
    {
        using var sut = CreateSut(MutationState<string>.CreateRunning());
        var events = new List<string>();
        sut.PropertyChanged += (_, e) => events.Add(e.PropertyName!);
        sut.StateChanged += (_, _) => events.Add(nameof(sut.StateChanged));

        _stateSubject.OnNext(MutationState<string>.CreateSuccess("result"));
        _uiContext.DrainAll();

        await Assert.That(events).Contains(nameof(sut.StateChanged));
        await Assert.That(events[^1]).IsEqualTo(nameof(sut.StateChanged));
    }

    [Test]
    public async Task StateChanged_WhenStateDoesNotActuallyChange_IsNotRaised()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());
        var raisedStateChanged = false;
        sut.StateChanged += (_, _) => raisedStateChanged = true;

        _stateSubject.OnNext(_stateSubject.Value);
        _uiContext.DrainAll();

        await Assert.That(raisedStateChanged).IsFalse();
    }

    [Test]
    public async Task StateChanged_BurstEmissionsBeforeDrain_RaisesOnceForTheCoalescedState()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());
        var raiseCount = 0;
        sut.StateChanged += (_, _) => raiseCount++;

        _stateSubject.OnNext(MutationState<string>.CreateRunning());
        _stateSubject.OnNext(MutationState<string>.CreateSuccess("result"));
        _uiContext.DrainAll();

        await Assert.That(raiseCount).IsEqualTo(1);
    }
}
