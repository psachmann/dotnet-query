namespace DotNetQuery.Mvvm.Tests;

public class MutationViewModelIntegrationTests
{
    private readonly RecordingSynchronizationContext _uiContext = new();

    [Test]
    public async Task RealClient_ExecuteCommand_RunsAndNotifiesThroughDispatcher()
    {
        using var client = QueryClientFactory.Create(new QueryClientOptions());
        using var sut = new MutationViewModel<int, string>(
            client,
            new MutationOptions<int, string> { Mutator = (args, _) => Task.FromResult($"value-{args}") },
            new SynchronizationContextUiDispatcher(_uiContext)
        );
        var raised = new List<string?>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        sut.ExecuteCommand.Execute(1);

        // The VM subscribed to State before this subscriber, so once this await completes,
        // the success emission has already been queued on the recording context.
        _ = await sut.Mutation.State.Where(s => s.IsSuccess).FirstAsync();
        _uiContext.DrainAll();

        await Assert.That(sut.IsSuccess).IsTrue();
        await Assert.That(sut.Data).IsEqualTo("value-1");
        await Assert.That(raised).Contains(nameof(sut.Data));
    }

    [Test]
    public async Task RealClient_ExecuteWhileRunning_SecondCallIsIgnored()
    {
        var tcs = new TaskCompletionSource<string>();
        var executionCount = 0;
        using var client = QueryClientFactory.Create(new QueryClientOptions());
        using var sut = new MutationViewModel<int, string>(
            client,
            new MutationOptions<int, string>
            {
                Mutator = (_, _) =>
                {
                    executionCount++;

                    return tcs.Task;
                },
            },
            new SynchronizationContextUiDispatcher(_uiContext)
        );

        sut.Execute(1);
        // CurrentState flips to Running synchronously (Phase 0), so this second call is a no-op
        // even though nothing has drained through the dispatcher yet.
        sut.Execute(2);

        await Assert.That(executionCount).IsEqualTo(1);

        tcs.SetResult("done");
    }
}
