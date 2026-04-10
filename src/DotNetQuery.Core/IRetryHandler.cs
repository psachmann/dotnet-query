namespace DotNetQuery.Core;

/// <summary>
/// Executes an async operation with retry logic applied according to the implementation's policy.
/// </summary>
public interface IRetryHandler
{
    /// <summary>
    /// Executes the given <paramref name="action"/> and retries it on failure according to the
    /// configured retry policy.
    /// </summary>
    /// <typeparam name="TData">The type of the value returned by the action.</typeparam>
    /// <param name="action">The async operation to execute, receiving a <see cref="CancellationToken"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result of the action on success.</returns>
    public Task<TData> ExecuteAsync<TData>(
        Func<CancellationToken, Task<TData>> action,
        CancellationToken cancellationToken = default
    );
}
