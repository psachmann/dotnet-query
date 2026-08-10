namespace DotNetQuery.Core;

/// <summary>
/// Configuration options for a single mutation created via <see cref="IQueryClient.CreateMutation{TArgs,TData}"/>.
/// </summary>
/// <typeparam name="TArgs">The type of the arguments passed to the mutator.</typeparam>
/// <typeparam name="TData">The type of the data returned on success.</typeparam>
public sealed record MutationOptions<TArgs, TData>
{
    /// <summary>
    /// The async function that performs the mutation for the given args.
    /// Receives a <see cref="CancellationToken"/> that is cancelled when the mutation is cancelled or disposed.
    /// </summary>
    public required Func<TArgs, CancellationToken, Task<TData>> Mutator { get; init; }

    /// <summary>Overrides the global <see cref="QueryClientOptions.RetryHandler"/>. <c>null</c> uses the global default.</summary>
    public IRetryHandler? RetryHandler { get; init; }

    /// <summary>
    /// A short name used to tag metrics, traces, and log messages for this mutation (e.g. <c>"createTodo"</c>).
    /// When <c>null</c>, <c>typeof(TArgs).Name</c> is used instead.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Whether the mutation is initially enabled. Defaults to <c>true</c>.
    /// When <c>false</c>, calls to <see cref="IMutation{TArgs,TData}.Execute"/> are silently ignored.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// A list of <see cref="QueryKey"/> values to invalidate automatically after a successful execution.
    /// <c>null</c> skips automatic invalidation.
    /// </summary>
    /// <remarks>
    /// Invalidation fires immediately when the <see cref="Mutator"/> task resolves.
    /// For eventually-consistent backends — read replicas, CQRS read models, async side-effects —
    /// the re-fetch may arrive before the write is visible and return stale data.
    /// In those cases use <see cref="OnSuccess"/> instead, where you control when to call
    /// <c>QueryClient.Invalidate(...)</c>.
    /// </remarks>
    public IReadOnlyList<QueryKey>? InvalidateKeys { get; init; }

    /// <summary>
    /// Invoked synchronously with the mutation args immediately before the mutator runs.
    /// Use this to snapshot the current cache state and apply an optimistic update via
    /// <see cref="IQuery{TArgs,TData}.SetData"/>. On failure, restore the snapshot in <see cref="OnFailure"/>.
    /// </summary>
    public Action<TArgs>? OnMutate { get; init; }

    /// <summary>
    /// Invoked after each successful execution with the original args and the returned data.
    /// Called before <see cref="OnSettled"/>.
    /// </summary>
    public Action<TArgs, TData>? OnSuccess { get; init; }

    /// <summary>
    /// Invoked after each failed execution with the thrown exception.
    /// Called before <see cref="OnSettled"/>.
    /// </summary>
    public Action<Exception>? OnFailure { get; init; }

    /// <summary>
    /// Invoked after every execution that reaches a terminal state: success, failure, <b>or cancellation</b>.
    /// Called after <see cref="OnSuccess"/> or <see cref="OnFailure"/> when applicable.
    /// </summary>
    public Action? OnSettled { get; init; }
}
