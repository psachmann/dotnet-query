namespace DotNetQuery.Mvvm.Tests;

public class MutationViewModelProgressTests
{
    private readonly Mock<IMutation<int, string, long>> _mutationMock = Mock.Of<IMutation<int, string, long>>();
    private readonly RecordingSynchronizationContext _uiContext = new();
    private BehaviorSubject<MutationState<string, long>>? _stateSubject;

    [After(Test)]
    public void Teardown()
    {
        _stateSubject?.OnCompleted();
        _stateSubject?.Dispose();
    }

    private MutationViewModel<int, string, long> CreateSut(MutationState<string, long> initialState)
    {
        _stateSubject = new(initialState);
        _mutationMock.CurrentState.Returns(() => _stateSubject.Value);
        _mutationMock.State.Returns(_stateSubject.AsObservable());

        return new MutationViewModel<int, string, long>(
            _mutationMock.Object,
            new SynchronizationContextUiDispatcher(_uiContext)
        );
    }

    private static List<string?> CollectPropertyChanges(MutationViewModel<int, string, long> sut)
    {
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        return raised;
    }

    [Test]
    public async Task Ctor_WithRunningCurrentState_ExposesProgressSynchronously()
    {
        using var sut = CreateSut(MutationState<string, long>.CreateRunning(40));

        // No DrainAll — the initial state must be visible before any dispatcher hop.
        await Assert.That(sut.IsRunning).IsTrue();
        await Assert.That(sut.Progress).IsEqualTo(40L);
    }

    [Test]
    public async Task ProgressEmission_RaisesProgress_ButNotUnchangedStatusProperties()
    {
        using var sut = CreateSut(MutationState<string, long>.CreateRunning(0));
        var raised = CollectPropertyChanges(sut);

        _stateSubject!.OnNext(MutationState<string, long>.CreateRunning(25));
        _uiContext.DrainAll();

        await Assert.That(sut.Progress).IsEqualTo(25L);
        await Assert.That(raised).Contains(nameof(sut.CurrentState));
        await Assert.That(raised).Contains(nameof(sut.Progress));
        await Assert.That(raised).DoesNotContain(nameof(sut.Status));
        await Assert.That(raised).DoesNotContain(nameof(sut.IsRunning));
    }

    [Test]
    public async Task BurstProgressEmissions_BeforeDrain_CoalesceToLatestValue()
    {
        using var sut = CreateSut(MutationState<string, long>.CreateRunning(0));
        var raised = CollectPropertyChanges(sut);

        for (var i = 1; i <= 1_000; i++)
        {
            _stateSubject!.OnNext(MutationState<string, long>.CreateRunning(i));
        }

        _uiContext.DrainAll();

        await Assert.That(sut.Progress).IsEqualTo(1_000L);
        await Assert.That(raised.Count(name => name == nameof(sut.Progress))).IsEqualTo(1);
        await Assert.That(_uiContext.PostCount).IsEqualTo(1);
    }

    [Test]
    public async Task RunningToSuccess_WithSameProgress_DoesNotRaiseProgress()
    {
        using var sut = CreateSut(MutationState<string, long>.CreateRunning(100));
        var raised = CollectPropertyChanges(sut);

        _stateSubject!.OnNext(MutationState<string, long>.CreateSuccess("done", 100));
        _uiContext.DrainAll();

        await Assert.That(sut.IsSuccess).IsTrue();
        await Assert.That(sut.Progress).IsEqualTo(100L);
        await Assert.That(raised).Contains(nameof(sut.IsSuccess));
        await Assert.That(raised).DoesNotContain(nameof(sut.Progress));
    }

    [Test]
    public async Task ExecuteCommand_WhileRunning_CannotExecute()
    {
        using var sut = CreateSut(MutationState<string, long>.CreateRunning(10));

        await Assert.That(sut.ExecuteCommand.CanExecute(1)).IsFalse();
        await Assert.That(sut.CancelCommand.CanExecute(null)).IsTrue();
    }

    [Test]
    public async Task CancelCommand_CancelsWrappedMutation()
    {
        using var sut = CreateSut(MutationState<string, long>.CreateRunning(10));

        sut.CancelCommand.Execute(null);

        _mutationMock.Cancel().WasCalled(Times.Once);
        await Assert.That(sut.IsRunning).IsTrue();
    }

    [Test]
    public async Task RealClient_ReportedProgress_ReachesViewModelThroughDispatcher()
    {
        IProgress<long>? captured = null;
        var release = new TaskCompletionSource<string>();
        using var client = QueryClientFactory.Create(new QueryClientOptions());
        using var sut = new MutationViewModel<int, string, long>(
            client,
            new MutationOptions<int, string, long>
            {
                Mutator = (_, progress, _) =>
                {
                    captured = progress;
                    return release.Task;
                },
            },
            new SynchronizationContextUiDispatcher(_uiContext)
        );

        sut.Execute(1);
        captured!.Report(512);
        _uiContext.DrainAll();

        await Assert.That(sut.IsRunning).IsTrue();
        await Assert.That(sut.Progress).IsEqualTo(512L);

        release.SetResult("done");
        _ = await sut.Mutation.State.Where(s => s.IsSuccess).FirstAsync();
        _uiContext.DrainAll();

        await Assert.That(sut.IsSuccess).IsTrue();
        await Assert.That(sut.Progress).IsEqualTo(512L);
    }

    [Test]
    public async Task ToViewModel_WrapsWithoutTakingOwnership()
    {
        using var client = QueryClientFactory.Create(new QueryClientOptions());
        using var mutation = client.CreateMutation(
            new MutationOptions<int, string, long> { Mutator = (_, _, _) => Task.FromResult("ok") }
        );

        var sut = mutation.ToViewModel(dispatcher: new SynchronizationContextUiDispatcher(_uiContext));
        sut.Dispose();
        mutation.Execute(1);
        var success = await mutation.Success.FirstAsync();

        await Assert.That(sut.Mutation).IsSameReferenceAs(mutation);
        await Assert.That(success).IsEqualTo("ok");
    }
}
