namespace DotNetQuery.Core;

public interface IRetryHandler
{
    public Task<TData> ExecuteAsync<TData>(
        Func<CancellationToken, Task<TData>> action,
        CancellationToken cancellationToken = default
    );
}
