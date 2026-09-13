namespace DotNetQuery.Mvvm;

/// <summary>
/// Marshals a stream of immutable state snapshots onto the UI thread with latest-wins coalescing —
/// a burst of emissions collapses into a single dispatcher hop applying only the newest state — and
/// swaps in the new snapshot before invoking <c>apply</c>, so the callback always sees a consistent
/// previous/next pair. Shared by <see cref="QueryViewModel{TArgs, TData}"/> and its sibling view
/// models so this mechanic is implemented once.
/// </summary>
/// <typeparam name="TState">The state snapshot type — an immutable record.</typeparam>
internal sealed class UiStateBinding<TState> : IDisposable
    where TState : class
{
    private readonly IUiDispatcher _dispatcher;
    private readonly Action<TState, TState> _apply;
    private readonly IDisposable _subscription;

    private TState _current;
    private TState? _pending;
    private bool _isDisposed;

    /// <summary>
    /// Subscribes to <paramref name="source"/> and applies every emission that differs (by reference)
    /// from the currently applied state through <paramref name="apply"/> on the UI thread.
    /// </summary>
    /// <param name="source">The state stream. Assumed to replay its latest value to a new subscriber.</param>
    /// <param name="initial">
    /// The state to expose from <see cref="Current"/> before the first dispatcher hop — read directly,
    /// without invoking <paramref name="apply"/>. The caller is expected to have already read this value
    /// synchronously (e.g. from the wrapped query's <c>CurrentState</c>) so bindings evaluated right
    /// after construction see it without waiting. The replay <paramref name="source"/> delivers on
    /// subscribe is deduped by the reference-equality check below, exactly like any other emission.
    /// </param>
    /// <param name="dispatcher">The UI-thread dispatcher.</param>
    /// <param name="apply">
    /// Invoked on the UI thread with the previous and next snapshots, in that order, once per applied
    /// state change. Never invoked after <see cref="Dispose"/>.
    /// </param>
    public UiStateBinding(
        IObservable<TState> source,
        TState initial,
        IUiDispatcher dispatcher,
        Action<TState, TState> apply
    )
    {
        _current = initial;
        _dispatcher = dispatcher;
        _apply = apply;
        _subscription = source.Subscribe(OnStateEmitted);
    }

    /// <summary>The most recently applied state.</summary>
    public TState Current => _current;

    /// <summary>
    /// Stops applying further emissions. A dispatcher hop already queued when this is called becomes
    /// a no-op instead of invoking <c>apply</c>.
    /// </summary>
    public void Dispose()
    {
        _isDisposed = true;
        _subscription.Dispose();
    }

    // Called on whatever thread the source pushed from. Latest-wins coalescing: a burst of
    // emissions collapses into a single dispatcher post applying only the newest state.
    private void OnStateEmitted(TState state)
    {
        if (Interlocked.Exchange(ref _pending, state) is null)
        {
            _dispatcher.Post(DrainPending);
        }
    }

    private void DrainPending()
    {
        if (Interlocked.Exchange(ref _pending, null) is { } state)
        {
            ApplyIfChanged(state);
        }
    }

    private void ApplyIfChanged(TState next)
    {
        if (_isDisposed || ReferenceEquals(_current, next))
        {
            return;
        }

        // Swap in the new snapshot before invoking apply so a callback reading Current mid-way
        // through — or a sibling property it raises PropertyChanged for — always sees the new state.
        var previous = _current;
        _current = next;
        _apply(previous, next);
    }
}
