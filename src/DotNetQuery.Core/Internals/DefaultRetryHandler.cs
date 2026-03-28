namespace DotNetQuery.Core.Internals;

internal sealed class DefaultRetryHandler(TimeSpan[]? retryDelays = default) : IRetryHandler
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
    ];

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default
    )
    {
        Exception? lastException = null;
        var delays = retryDelays ?? RetryDelays;

        for (var attempt = 0; attempt <= delays.Length; attempt++)
        {
            try
            {
                return await action(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < delays.Length)
            {
                lastException = ex;
                await Task.Delay(delays[attempt], cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        throw lastException!;
    }
}
