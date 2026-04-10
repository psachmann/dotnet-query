namespace DotNetQuery.Core;

/// <summary>
/// Factory for creating <see cref="IQueryClient"/> instances without a dependency injection container.
/// </summary>
public static class QueryClientFactory
{
    /// <summary>
    /// Creates a new <see cref="IQueryClient"/> using the given <paramref name="options"/>.
    /// Options are validated before the client is created.
    /// </summary>
    /// <param name="options">The configuration options for the client.</param>
    /// <param name="scheduler">
    /// An optional <see cref="IScheduler"/> used for timing operations such as stale-time,
    /// cache eviction, and refetch intervals. Defaults to <see cref="DefaultScheduler.Instance"/> when <c>null</c>.
    /// </param>
    /// <returns>A new <see cref="IQueryClient"/> instance.</returns>
    public static IQueryClient Create(QueryClientOptions options, IScheduler? scheduler = null)
    {
        options.Validate();

        return new QueryClient(options, scheduler);
    }
}
