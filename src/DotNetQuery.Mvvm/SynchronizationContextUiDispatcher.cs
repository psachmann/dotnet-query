namespace DotNetQuery.Mvvm;

/// <summary>
/// An <see cref="IUiDispatcher"/> backed by a <see cref="SynchronizationContext"/>.
/// When the context is <c>null</c>, actions are invoked inline on the calling thread —
/// useful in unit tests and console applications that have no UI thread.
/// </summary>
/// <param name="context">The context to post to, or <c>null</c> to invoke actions inline.</param>
public sealed class SynchronizationContextUiDispatcher(SynchronizationContext? context) : IUiDispatcher
{
    /// <summary>
    /// Creates a dispatcher targeting <see cref="SynchronizationContext.Current"/>.
    /// Call this on the UI thread; when called where no context is installed
    /// (unit tests, console apps), the resulting dispatcher invokes actions inline.
    /// </summary>
    public static SynchronizationContextUiDispatcher CaptureCurrent() => new(SynchronizationContext.Current);

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (context is null)
        {
            action();
        }
        else
        {
            context.Post(static state => ((Action)state!)(), action);
        }
    }
}
