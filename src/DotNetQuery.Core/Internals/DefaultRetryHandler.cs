namespace DotNetQuery.Core.Internals;

internal sealed class DefaultRetryHandler : IRetryHandler
{
    public Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default
    ) => action(cancellationToken);
}
