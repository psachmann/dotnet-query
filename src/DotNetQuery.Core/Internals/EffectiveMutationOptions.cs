namespace DotNetQuery.Core.Internals;

internal sealed record EffectiveMutationOptions<TArgs, TData>
{
    public required Func<TArgs, CancellationToken, Task<TData>> Mutator { get; init; }

    public required IRetryHandler RetryHandler { get; init; }

    public required bool IsEnabled { get; init; }

    public required IReadOnlyList<QueryKey> InvalidateKeys { get; init; }

    public required Action<TArgs, TData> OnSuccess { get; init; }

    public required Action<Exception> OnFailure { get; init; }

    public required Action OnSettled { get; init; }
}
