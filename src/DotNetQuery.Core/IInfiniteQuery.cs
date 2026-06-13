namespace DotNetQuery.Core;

/// <summary>
/// An infinite (paginated) query that accumulates pages of <typeparamref name="TData"/> fetched
/// with page parameters of type <typeparamref name="TPageParam"/>.
/// </summary>
/// <typeparam name="TArgs">The type of the arguments that identify which resource to fetch.</typeparam>
/// <typeparam name="TData">The type of a single page of data.</typeparam>
/// <typeparam name="TPageParam">The type of the page parameter (cursor/offset).</typeparam>
public interface IInfiniteQuery<TArgs, TData, TPageParam> : IQuery
{
    /// <summary>The current state snapshot. Can be read synchronously without subscribing.</summary>
    InfiniteQueryState<TData, TPageParam> CurrentState { get; }

    /// <summary>
    /// Sets the args that drive the cache key and fetching. When args change, the query switches to
    /// the cache entry for the newly derived key and triggers a fetch if the query is enabled.
    /// </summary>
    void SetArgs(TArgs args);

    /// <summary>
    /// Enables or disables the query. When <c>false</c>, fetching is suspended even if the query
    /// is invalidated or new args are pushed. Pass <c>true</c> to resume.
    /// </summary>
    void SetEnabled(bool enabled);

    /// <summary>
    /// Fetches the next page using the cursor derived from the last loaded page via
    /// <c>GetNextPageParam</c>. No-op when no pages are loaded yet or
    /// <see cref="InfiniteQueryState{TData,TPageParam}.HasNextPage"/> is <c>false</c>.
    /// </summary>
    void FetchNextPage();

    /// <summary>
    /// Fetches the previous page using the cursor derived from the first loaded page via
    /// <c>GetPreviousPageParam</c>. No-op when no pages are loaded yet,
    /// <see cref="InfiniteQueryState{TData,TPageParam}.HasPreviousPage"/> is <c>false</c>,
    /// or no <c>GetPreviousPageParam</c> was configured.
    /// </summary>
    void FetchPreviousPage();

    /// <summary>
    /// Emits on every state transition. Replays the latest state to new subscribers.
    /// </summary>
    IObservable<InfiniteQueryState<TData, TPageParam>> State { get; }

    /// <summary>
    /// Emits the full accumulated page list after each fetch fully settles — not during
    /// <see cref="InfiniteQueryState{TData,TPageParam}.IsFetchingNextPage"/> or
    /// <see cref="InfiniteQueryState{TData,TPageParam}.IsFetchingPreviousPage"/>.
    /// </summary>
    IObservable<IReadOnlyList<TData>> Success { get; }

    /// <summary>Emits the <see cref="Exception"/> on each failed fetch.</summary>
    IObservable<Exception> Failure { get; }

    /// <summary>Emits the final state after each fetch completes, regardless of success or failure.</summary>
    IObservable<InfiniteQueryState<TData, TPageParam>> Settled { get; }

    /// <summary>
    /// Removes this query from the client cache while keeping active subscriptions alive.
    /// Use <see cref="IDisposable.Dispose"/> to also tear down all subscriptions and release resources.
    /// </summary>
    void Detach();
}
