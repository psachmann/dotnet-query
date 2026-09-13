namespace DotNetQuery.Mvvm.Tests;

public class MutationViewModelCommandTests
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
    public async Task ExecuteCommand_WhileIdle_CanExecute()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());

        await Assert.That(sut.ExecuteCommand.CanExecute(null)).IsTrue();
        await Assert.That(sut.CancelCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task ExecuteCommand_WhileRunning_CannotExecute()
    {
        using var sut = CreateSut(MutationState<string>.CreateRunning());

        await Assert.That(sut.ExecuteCommand.CanExecute(null)).IsFalse();
        await Assert.That(sut.CancelCommand.CanExecute(null)).IsTrue();
    }

    [Test]
    public async Task ExecuteCommand_Execute_CallsMutationExecuteWithCastArgs()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());

        sut.ExecuteCommand.Execute(5);

        var invocation = Mock.Invocations(_mutationMock)
            .Single(i => i.MemberName == nameof(IMutation<int, string>.Execute));
        await Assert.That(invocation.Arguments[0]).IsEqualTo(5);
    }

    [Test]
    public async Task ExecuteCommand_Execute_WithWrongParameterType_ThrowsArgumentExceptionNamingBothTypes()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());
        var act = () => sut.ExecuteCommand.Execute("not-an-int");

        var ex = await Assert.That(act).ThrowsException().And.IsTypeOf<ArgumentException>();

        await Assert.That(ex?.Message).Contains(typeof(int).ToString());
        await Assert.That(ex?.Message).Contains(typeof(string).ToString());
        await Assert.That(ex?.ParamName).IsEqualTo("parameter");
    }

    [Test]
    public async Task ExecuteCommand_Execute_WithNullParameterForValueTypeArgs_ThrowsArgumentException()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());
        var act = () => sut.ExecuteCommand.Execute(null);

        await Assert.That(act).ThrowsException().And.IsTypeOf<ArgumentException>();
    }

    [Test]
    public async Task ExecuteCommand_ExecutedTwiceWithoutDrain_RunsMutationOnce()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());

        sut.ExecuteCommand.Execute(5);

        // A bare mock has no real state machine: simulate what the real Mutation does synchronously
        // in Execute (Phase 0 -- ExecuteAsync sets Running before its first await) so the view
        // model's own double-submit guard has something to observe on the second call.
        _mutationMock.CurrentState.Returns(MutationState<string>.CreateRunning());
        sut.ExecuteCommand.Execute(5);

        await Assert
            .That(Mock.Invocations(_mutationMock).Count(i => i.MemberName == nameof(IMutation<int, string>.Execute)))
            .IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteCommand_CanExecute_FalseImmediatelyAfterExecute_BeforeDrain()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());

        sut.ExecuteCommand.Execute(5);
        _mutationMock.CurrentState.Returns(MutationState<string>.CreateRunning());

        // No DrainAll: the dispatcher-applied IsRunning must still be false here, while CanExecute
        // reads Mutation.CurrentState directly and already reports false -- that gap is exactly
        // what closes the double-submit race.
        await Assert.That(sut.IsRunning).IsFalse();
        await Assert.That(sut.ExecuteCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task ExecuteCommand_RunStartedDirectlyOnSharedMutation_CannotExecute()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());

        // A run started elsewhere on the shared mutation, never through this view model.
        _mutationMock.CurrentState.Returns(MutationState<string>.CreateRunning());

        await Assert.That(sut.ExecuteCommand.CanExecute(null)).IsFalse();
        await Assert
            .That(Mock.Invocations(_mutationMock).Any(i => i.MemberName == nameof(IMutation<int, string>.Execute)))
            .IsFalse();
    }

    [Test]
    public async Task Execute_WhileRunning_DoesNotCallMutationExecute()
    {
        using var sut = CreateSut(MutationState<string>.CreateRunning());

        sut.Execute(5);

        await Assert
            .That(Mock.Invocations(_mutationMock).Any(i => i.MemberName == nameof(IMutation<int, string>.Execute)))
            .IsFalse();
    }

    [Test]
    public async Task CancelCommand_Execute_CallsMutationCancel()
    {
        using var sut = CreateSut(MutationState<string>.CreateRunning());

        sut.CancelCommand.Execute(null);

        await Assert
            .That(Mock.Invocations(_mutationMock).Any(i => i.MemberName == nameof(IMutation<int, string>.Cancel)))
            .IsTrue();
    }

    [Test]
    public async Task CanExecuteChanged_OnIsRunningFlip_RaisedDuringDrainForBothCommands()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());
        var executeRaised = 0;
        var cancelRaised = 0;
        sut.ExecuteCommand.CanExecuteChanged += (_, _) => executeRaised++;
        sut.CancelCommand.CanExecuteChanged += (_, _) => cancelRaised++;

        _stateSubject.OnNext(MutationState<string>.CreateRunning());

        await Assert.That(executeRaised).IsEqualTo(0);

        _uiContext.DrainAll();

        await Assert.That(executeRaised).IsEqualTo(1);
        await Assert.That(cancelRaised).IsEqualTo(1);
    }

    [Test]
    public async Task ToCommand_Mapped_ExecuteMapsParameterAndCallsMutationExecute()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());
        var command = sut.ToCommand<string>(text => text.Length);

        command.Execute("hello");

        var invocation = Mock.Invocations(_mutationMock)
            .Single(i => i.MemberName == nameof(IMutation<int, string>.Execute));
        await Assert.That(invocation.Arguments[0]).IsEqualTo(5);
    }

    [Test]
    public async Task ToCommand_Mapped_CanExecute_ConsultsSuppliedPredicate()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());
        var command = sut.ToCommand<string>(text => text.Length, text => text.Length > 2);

        await Assert.That(command.CanExecute("ab")).IsFalse();
        await Assert.That(command.CanExecute("abc")).IsTrue();
    }

    [Test]
    public async Task ToCommand_Mapped_WhileRunning_CannotExecuteRegardlessOfPredicate()
    {
        using var sut = CreateSut(MutationState<string>.CreateRunning());
        var command = sut.ToCommand<string>(text => text.Length, _ => true);

        await Assert.That(command.CanExecute("abc")).IsFalse();
    }

    [Test]
    public async Task ToCommand_Mapped_CanExecute_WrongParameterTypeReturnsFalseRatherThanThrowing()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());
        var command = sut.ToCommand<string>(text => text.Length);

        await Assert.That(command.CanExecute(42)).IsFalse();
    }

    [Test]
    public async Task ToCommand_Mapped_Execute_WrongParameterTypeThrows()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());
        var command = sut.ToCommand<string>(text => text.Length);
        var act = () => command.Execute(42);

        await Assert.That(act).ThrowsException().And.IsTypeOf<ArgumentException>();
    }

    [Test]
    public async Task ToCommand_Mapped_RaiseCanExecuteChanged_RaisesEvent()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());
        var command = sut.ToCommand<string>(text => text.Length);
        var raised = 0;
        command.CanExecuteChanged += (_, _) => raised++;

        command.RaiseCanExecuteChanged();

        await Assert.That(raised).IsEqualTo(1);
    }

    [Test]
    public async Task ToCommand_Mapped_RefreshedOnIsRunningFlipLikeBuiltInCommands()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());
        var command = sut.ToCommand<string>(text => text.Length);
        var raised = 0;
        command.CanExecuteChanged += (_, _) => raised++;

        _stateSubject.OnNext(MutationState<string>.CreateRunning());
        _uiContext.DrainAll();

        await Assert.That(raised).IsEqualTo(1);
    }

    [Test]
    public async Task ToCommand_Factory_ExecuteUsesFactoryArgs()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());
        var command = sut.ToCommand(() => 7);

        command.Execute(null);

        var invocation = Mock.Invocations(_mutationMock)
            .Single(i => i.MemberName == nameof(IMutation<int, string>.Execute));
        await Assert.That(invocation.Arguments[0]).IsEqualTo(7);
    }

    [Test]
    public async Task ToCommand_Factory_CanExecute_ConsultsSuppliedPredicate()
    {
        using var sut = CreateSut(MutationState<string>.CreateIdle());
        var canRun = false;
        var command = sut.ToCommand(() => 7, () => canRun);

        await Assert.That(command.CanExecute(null)).IsFalse();

        canRun = true;

        await Assert.That(command.CanExecute(null)).IsTrue();
    }

    [Test]
    public async Task ToCommand_Factory_WhileRunning_CannotExecuteRegardlessOfPredicate()
    {
        using var sut = CreateSut(MutationState<string>.CreateRunning());
        var command = sut.ToCommand(() => 7, () => true);

        await Assert.That(command.CanExecute(null)).IsFalse();
    }
}
