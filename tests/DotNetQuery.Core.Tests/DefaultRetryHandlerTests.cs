namespace DotNetQuery.Core.Tests;

public class DefaultRetryHandlerTests
{
    private readonly DefaultRetryHandler _sut = new();

    [Test]
    public async Task ExecuteAsync_SucceedsOnFirstAttempt_ReturnsResult()
    {
        var result = await _sut.ExecuteAsync<int>(_ => Task.FromResult(42));

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task ExecuteAsync_OnException_ThrowsImmediately()
    {
        var exception = new InvalidOperationException("fail");

        var act = async () => await _sut.ExecuteAsync<int>(_ => Task.FromException<int>(exception));

        await Assert.That(act).ThrowsException().And.IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task ExecuteAsync_OnException_DoesNotRetry()
    {
        var callCount = 0;

        var act = async () =>
            await _sut.ExecuteAsync<int>(_ =>
            {
                callCount++;
                throw new InvalidOperationException("fail");
            });

        await Assert.That(act).ThrowsException();
        await Assert.That(callCount).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () =>
            await _sut.ExecuteAsync<int>(
                ct =>
                {
                    ct.ThrowIfCancellationRequested();

                    return Task.FromResult(0);
                },
                cts.Token
            );

        await Assert.That(act).ThrowsException().And.IsTypeOf<OperationCanceledException>();
    }
}
