namespace DotNetQuery.Core;

public sealed record MutationOptions<TArgs, TData>
{
    public required Func<TArgs, CancellationToken, Task<TData>> Mutator { get; init; }

    /// <summary>Overrides the global <see cref="QueryClientOptions.RetryHandler"/>. <c>null</c> uses the global default.</summary>
    public IRetryHandler? RetryHandler { get; init; }

    public bool IsEnabled { get; init; } = true;

    public IReadOnlyList<QueryKey>? InvalidateKeys { get; init; }

    public Action<TArgs, TData>? OnSuccess { get; init; }

    public Action<Exception>? OnFailure { get; init; }

    public Action? OnSettled { get; init; }
}
