namespace DotNetQuery.Core.Tests;

public class MutationStateTests
{
    [Test]
    public async Task CreateIdle_SetsIdleStatus()
    {
        var state = MutationState<Unit>.CreateIdle();

        await Assert.That(state.Status).IsEqualTo(MutationStatus.Idle);
    }

    [Test]
    public async Task CreateIdle_IsIdle_IsTrue()
    {
        var state = MutationState<Unit>.CreateIdle();

        await Assert.That(state.IsIdle).IsTrue();
    }

    [Test]
    public async Task CreateIdle_HasNoData_HasNoError()
    {
        var state = MutationState<Unit>.CreateIdle();

        using var _ = Assert.Multiple();
        await Assert.That(state.HasData).IsFalse();
        await Assert.That(state.HasError).IsFalse();
        await Assert.That(state.Error).IsNull();
    }

    [Test]
    public async Task CreateRunning_SetsRunningStatus()
    {
        var state = MutationState<Unit>.CreateRunning();

        await Assert.That(state.Status).IsEqualTo(MutationStatus.Running);
    }

    [Test]
    public async Task CreateRunning_IsRunning_IsTrue()
    {
        var state = MutationState<Unit>.CreateRunning();

        await Assert.That(state.IsRunning).IsTrue();
    }

    [Test]
    public async Task CreateRunning_HasNoData_HasNoError()
    {
        var state = MutationState<Unit>.CreateRunning();

        using var _ = Assert.Multiple();
        await Assert.That(state.HasData).IsFalse();
        await Assert.That(state.HasError).IsFalse();
        await Assert.That(state.Error).IsNull();
    }

    [Test]
    public async Task CreateSuccess_SetsSuccessStatus()
    {
        var state = MutationState<string>.CreateSuccess("result");

        await Assert.That(state.Status).IsEqualTo(MutationStatus.Success);
    }

    [Test]
    public async Task CreateSuccess_IsSuccess_IsTrue()
    {
        var state = MutationState<string>.CreateSuccess("result");

        await Assert.That(state.IsSuccess).IsTrue();
    }

    [Test]
    public async Task CreateSuccess_SetsData()
    {
        var state = MutationState<string>.CreateSuccess("result");

        using var _ = Assert.Multiple();
        await Assert.That(state.Data).IsEqualTo("result");
        await Assert.That(state.HasData).IsTrue();
        await Assert.That(state.HasError).IsFalse();
    }

    [Test]
    public async Task CreateFailure_SetsFailureStatus()
    {
        var state = MutationState<Unit>.CreateFailure(new Exception("oops"));

        await Assert.That(state.Status).IsEqualTo(MutationStatus.Failure);
    }

    [Test]
    public async Task CreateFailure_IsFailure_IsTrue()
    {
        var state = MutationState<Unit>.CreateFailure(new Exception("oops"));

        await Assert.That(state.IsFailure).IsTrue();
    }

    [Test]
    public async Task CreateFailure_SetsError()
    {
        var error = new Exception("oops");
        var state = MutationState<Unit>.CreateFailure(error);

        using var _ = Assert.Multiple();
        await Assert.That(state.Error).IsEqualTo(error);
        await Assert.That(state.HasError).IsTrue();
        await Assert.That(state.HasData).IsFalse();
    }

    [Test]
    public async Task BooleanFlags_OnlyMatchingStatus_IsTrue()
    {
        var idle = MutationState<Unit>.CreateIdle();
        var running = MutationState<Unit>.CreateRunning();
        var success = MutationState<Unit>.CreateSuccess(Unit.Default);
        var failure = MutationState<Unit>.CreateFailure(new Exception());

        using var _ = Assert.Multiple();
        await Assert.That(idle.IsIdle).IsTrue();
        await Assert.That(idle.IsRunning).IsFalse();
        await Assert.That(idle.IsSuccess).IsFalse();
        await Assert.That(idle.IsFailure).IsFalse();

        await Assert.That(running.IsRunning).IsTrue();
        await Assert.That(running.IsIdle).IsFalse();
        await Assert.That(running.IsSuccess).IsFalse();
        await Assert.That(running.IsFailure).IsFalse();

        await Assert.That(success.IsSuccess).IsTrue();
        await Assert.That(success.IsIdle).IsFalse();
        await Assert.That(success.IsRunning).IsFalse();
        await Assert.That(success.IsFailure).IsFalse();

        await Assert.That(failure.IsFailure).IsTrue();
        await Assert.That(failure.IsIdle).IsFalse();
        await Assert.That(failure.IsRunning).IsFalse();
        await Assert.That(failure.IsSuccess).IsFalse();
    }
}
