namespace DotNetQuery.Core.Tests.Utils;

internal sealed class NoRetryHandler : IRetryHandler
{
    public async Task<TData> ExecuteAsync<TData>(Func<CancellationToken, Task<TData>> action, CancellationToken ct)
    {
        return await action(ct);
    }
}
