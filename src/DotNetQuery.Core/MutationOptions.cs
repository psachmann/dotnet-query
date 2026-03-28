namespace DotNetQuery.Core;

public sealed record MutationOptions<TArgs, TData>
{
    public required Func<TArgs, CancellationToken, Task<TData>> Mutator { get; init; }

    public IRetryHandler RetryHandler { get; init; } = new DefaultRetryHandler();

    public bool IsEnabled { get; init; } = true;

    public IReadOnlyList<QueryKey>? InvalidateKeys { get; init; }

    public Action<TArgs, TData>? OnSuccess { get; init; }

    public Action<Exception>? OnFailure { get; init; }

    public Action? OnSettled { get; init; }
}
