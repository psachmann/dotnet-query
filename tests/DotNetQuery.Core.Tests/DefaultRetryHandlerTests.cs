namespace DotNetQuery.Core.Tests;

public class DefaultRetryHandlerTests
{
    private static readonly TimeSpan[] NoDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];

    private readonly DefaultRetryHandler _sut = new(NoDelays);

    [Test]
    public async Task ExecuteAsync_SucceedsOnFirstAttempt_ReturnsResult()
    {
        var result = await _sut.ExecuteAsync<int>(_ => Task.FromResult(42));

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task ExecuteAsync_FailsThenSucceeds_ReturnsResult()
    {
        var attempts = 0;

        var result = await _sut.ExecuteAsync<int>(_ =>
        {
            attempts++;

            if (attempts < 2)
            {
                throw new InvalidOperationException("transient");
            }

            return Task.FromResult(99);
        });

        using var _ = Assert.Multiple();
        await Assert.That(result).IsEqualTo(99);
        await Assert.That(attempts).IsEqualTo(2);
    }

    [Test]
    public async Task ExecuteAsync_FailsAllAttempts_ThrowsLastException()
    {
        var callCount = 0;
        var exception = new InvalidOperationException("always fails");

        var act = async () =>
            await _sut.ExecuteAsync<int>(_ =>
            {
                callCount++;

                throw exception;
            });

        using var _ = Assert.Multiple();
        await Assert.That(act).ThrowsException().And.IsTypeOf<InvalidOperationException>();
        // 1 initial + 3 retries = 4 total attempts
        await Assert.That(callCount).IsEqualTo(4);
    }

    [Test]
    public async Task ExecuteAsync_SucceedsOnLastRetry_ReturnsResult()
    {
        var attempts = 0;

        var result = await _sut.ExecuteAsync<string>(_ =>
        {
            attempts++;

            if (attempts < 4)
            {
                throw new InvalidOperationException("transient");
            }

            return Task.FromResult("ok");
        });

        using var _ = Assert.Multiple();
        await Assert.That(result).IsEqualTo("ok");
        await Assert.That(attempts).IsEqualTo(4);
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

    [Test]
    public async Task ExecuteAsync_CancellationRequestedDuringRetry_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        var attempts = 0;
        var sut = new DefaultRetryHandler([TimeSpan.FromSeconds(60)]);

        var act = async () =>
            await sut.ExecuteAsync<int>(
                async ct =>
                {
                    attempts++;

                    if (attempts == 1)
                    {
                        await cts.CancelAsync();
                        throw new InvalidOperationException("first failure");
                    }

                    ct.ThrowIfCancellationRequested();

                    return 0;
                },
                cts.Token
            );

        await Assert.That(act).ThrowsException().And.IsTypeOf<OperationCanceledException>();
    }

    [Test]
    public async Task ExecuteAsync_FailsAllAttempts_PreservesOriginalStackTrace()
    {
        // Regression: before ExceptionDispatchInfo.Capture().Throw(), re-throwing with `throw ex`
        // replaced the original stack trace with the re-throw site. The fix uses
        // ExceptionDispatchInfo.Capture().Throw() which PREPENDS the original frames and adds new
        // frames after a "--- End of stack trace from previous location ---" separator.
        string originalFirstFrame = string.Empty;

        var act = async () =>
            await _sut.ExecuteAsync<int>(_ =>
            {
                try
                {
                    throw new InvalidOperationException("original");
                }
                catch (InvalidOperationException ex)
                {
                    // Capture the first frame at the actual throw site
                    originalFirstFrame = ex.StackTrace?.Split('\n')[0].Trim() ?? string.Empty;

                    throw;
                }
            });

        var thrown = await Assert.That(act).ThrowsException().And.IsTypeOf<InvalidOperationException>();

        // The re-thrown exception's trace must START with the original throw frame, proving
        // ExceptionDispatchInfo preserved it rather than replacing it with the re-throw site.
        await Assert.That(thrown?.StackTrace).Contains(originalFirstFrame);
    }
}
