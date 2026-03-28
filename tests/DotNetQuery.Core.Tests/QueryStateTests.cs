namespace DotNetQuery.Core.Tests;

public class QueryStateTests
{
    [Test]
    public async Task CreateIdle_SetsIdleStatus()
    {
        var state = QueryState<string>.CreateIdle();

        await Assert.That(state.Status).IsEqualTo(QueryStatus.Idle);
    }

    [Test]
    public async Task CreateIdle_IsIdle_IsTrue()
    {
        var state = QueryState<string>.CreateIdle();

        await Assert.That(state.IsIdle).IsTrue();
    }

    [Test]
    public async Task CreateIdle_HasNoData()
    {
        var state = QueryState<string>.CreateIdle();

        using var _ = Assert.Multiple();
        await Assert.That(state.HasData).IsFalse();
        await Assert.That(state.CurrentData).IsNull();
        await Assert.That(state.LastData).IsNull();
    }

    [Test]
    public async Task CreateIdle_HasNoError()
    {
        var state = QueryState<string>.CreateIdle();

        using var _ = Assert.Multiple();
        await Assert.That(state.HasError).IsFalse();
        await Assert.That(state.Error).IsNull();
    }

    [Test]
    public async Task CreateFetching_SetsFetchingStatus()
    {
        var state = QueryState<string>.CreateFetching();

        await Assert.That(state.Status).IsEqualTo(QueryStatus.Fetching);
    }

    [Test]
    public async Task CreateFetching_IsFetching_IsTrue()
    {
        var state = QueryState<string>.CreateFetching();

        await Assert.That(state.IsFetching).IsTrue();
    }

    [Test]
    public async Task CreateFetching_WithLastData_SetsLastData()
    {
        var state = QueryState<string>.CreateFetching("previous");

        await Assert.That(state.LastData).IsEqualTo("previous");
    }

    [Test]
    public async Task CreateFetching_WithoutLastData_LastDataIsNull()
    {
        var state = QueryState<string>.CreateFetching();

        await Assert.That(state.LastData).IsNull();
    }

    [Test]
    public async Task CreateFetching_CurrentDataIsNull()
    {
        var state = QueryState<string>.CreateFetching("previous");

        using var _ = Assert.Multiple();
        await Assert.That(state.CurrentData).IsNull();
        await Assert.That(state.HasData).IsFalse();
    }

    [Test]
    public async Task CreateSuccess_SetsSuccessStatus()
    {
        var state = QueryState<string>.CreateSuccess("data");

        await Assert.That(state.Status).IsEqualTo(QueryStatus.Success);
    }

    [Test]
    public async Task CreateSuccess_IsSuccess_IsTrue()
    {
        var state = QueryState<string>.CreateSuccess("data");

        await Assert.That(state.IsSuccess).IsTrue();
    }

    [Test]
    public async Task CreateSuccess_SetsCurrentData()
    {
        var state = QueryState<string>.CreateSuccess("data");

        using var _ = Assert.Multiple();
        await Assert.That(state.CurrentData).IsEqualTo("data");
        await Assert.That(state.HasData).IsTrue();
    }

    [Test]
    public async Task CreateSuccess_WithLastData_SetsLastData()
    {
        var state = QueryState<string>.CreateSuccess("data", "previous");

        await Assert.That(state.LastData).IsEqualTo("previous");
    }

    [Test]
    public async Task CreateSuccess_WithoutLastData_LastDataIsNull()
    {
        var state = QueryState<string>.CreateSuccess("data");

        await Assert.That(state.LastData).IsNull();
    }

    [Test]
    public async Task CreateSuccess_HasNoError()
    {
        var state = QueryState<string>.CreateSuccess("data");

        using var _ = Assert.Multiple();
        await Assert.That(state.HasError).IsFalse();
        await Assert.That(state.Error).IsNull();
    }

    [Test]
    public async Task CreateFailure_SetsFailureStatus()
    {
        var error = new Exception("oops");
        var state = QueryState<string>.CreateFailure(error);

        await Assert.That(state.Status).IsEqualTo(QueryStatus.Failure);
    }

    [Test]
    public async Task CreateFailure_IsFailure_IsTrue()
    {
        var error = new Exception("oops");
        var state = QueryState<string>.CreateFailure(error);

        await Assert.That(state.IsFailure).IsTrue();
    }

    [Test]
    public async Task CreateFailure_SetsError()
    {
        var error = new Exception("oops");
        var state = QueryState<string>.CreateFailure(error);

        using var _ = Assert.Multiple();
        await Assert.That(state.Error).IsEqualTo(error);
        await Assert.That(state.HasError).IsTrue();
    }

    [Test]
    public async Task CreateFailure_WithLastData_SetsLastData()
    {
        var error = new Exception("oops");
        var state = QueryState<string>.CreateFailure(error, "previous");

        await Assert.That(state.LastData).IsEqualTo("previous");
    }

    [Test]
    public async Task CreateFailure_CurrentDataIsNull()
    {
        var error = new Exception("oops");
        var state = QueryState<string>.CreateFailure(error);

        using var _ = Assert.Multiple();
        await Assert.That(state.CurrentData).IsNull();
        await Assert.That(state.HasData).IsFalse();
    }

    [Test]
    public async Task BooleanFlags_OnlyMatchingStatus_IsTrue()
    {
        var idle = QueryState<string>.CreateIdle();
        var fetching = QueryState<string>.CreateFetching();
        var success = QueryState<string>.CreateSuccess("data");
        var failure = QueryState<string>.CreateFailure(new Exception());

        using var _ = Assert.Multiple();
        await Assert.That(idle.IsIdle).IsTrue();
        await Assert.That(idle.IsFetching).IsFalse();
        await Assert.That(idle.IsSuccess).IsFalse();
        await Assert.That(idle.IsFailure).IsFalse();

        await Assert.That(fetching.IsFetching).IsTrue();
        await Assert.That(fetching.IsIdle).IsFalse();
        await Assert.That(fetching.IsSuccess).IsFalse();
        await Assert.That(fetching.IsFailure).IsFalse();

        await Assert.That(success.IsSuccess).IsTrue();
        await Assert.That(success.IsIdle).IsFalse();
        await Assert.That(success.IsFetching).IsFalse();
        await Assert.That(success.IsFailure).IsFalse();

        await Assert.That(failure.IsFailure).IsTrue();
        await Assert.That(failure.IsIdle).IsFalse();
        await Assert.That(failure.IsFetching).IsFalse();
        await Assert.That(failure.IsSuccess).IsFalse();
    }
}
