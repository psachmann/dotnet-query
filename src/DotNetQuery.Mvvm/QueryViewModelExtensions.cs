namespace DotNetQuery.Mvvm;

/// <summary>
/// Extension methods that wrap <c>DotNetQuery.Core</c> types in view models, and marshal arbitrary
/// observables onto the UI thread.
/// </summary>
public static class QueryViewModelExtensions
{
    /// <summary>
    /// Wraps <paramref name="query"/> in a <see cref="QueryViewModel{TArgs, TData}"/>. The returned view
    /// model does <b>not</b> take ownership: disposing it releases only its own state subscription, and
    /// the caller remains responsible for disposing <paramref name="query"/>.
    /// </summary>
    /// <param name="query">The query to wrap.</param>
    /// <param name="dispatcher">
    /// The UI-thread dispatcher. When <c>null</c>, <see cref="SynchronizationContext.Current"/> is
    /// captured; call this on the UI thread or pass a dispatcher explicitly.
    /// </param>
    public static QueryViewModel<TArgs, TData> ToViewModel<TArgs, TData>(
        this IQuery<TArgs, TData> query,
        IUiDispatcher? dispatcher = null
    )
        where TData : class
    {
        ArgumentNullException.ThrowIfNull(query);

        return new QueryViewModel<TArgs, TData>(query, dispatcher);
    }

    /// <summary>
    /// Wraps <paramref name="query"/> in an <see cref="InfiniteQueryViewModel{TArgs, TData, TPageParam}"/>.
    /// The returned view model does <b>not</b> take ownership: disposing it releases only its own state
    /// subscription, and the caller remains responsible for disposing <paramref name="query"/>.
    /// </summary>
    /// <param name="query">The infinite query to wrap.</param>
    /// <param name="dispatcher">
    /// The UI-thread dispatcher. When <c>null</c>, <see cref="SynchronizationContext.Current"/> is
    /// captured; call this on the UI thread or pass a dispatcher explicitly.
    /// </param>
    public static InfiniteQueryViewModel<TArgs, TData, TPageParam> ToViewModel<TArgs, TData, TPageParam>(
        this IInfiniteQuery<TArgs, TData, TPageParam> query,
        IUiDispatcher? dispatcher = null
    )
        where TData : class
    {
        ArgumentNullException.ThrowIfNull(query);

        return new InfiniteQueryViewModel<TArgs, TData, TPageParam>(query, dispatcher);
    }

    /// <summary>
    /// Wraps <paramref name="mutation"/> in a <see cref="MutationViewModel{TArgs, TData}"/>. The returned
    /// view model does <b>not</b> take ownership: disposing it releases only its own state subscription,
    /// and the caller remains responsible for disposing <paramref name="mutation"/>.
    /// </summary>
    /// <param name="mutation">The mutation to wrap.</param>
    /// <param name="dispatcher">
    /// The UI-thread dispatcher. When <c>null</c>, <see cref="SynchronizationContext.Current"/> is
    /// captured; call this on the UI thread or pass a dispatcher explicitly.
    /// </param>
    public static MutationViewModel<TArgs, TData> ToViewModel<TArgs, TData>(
        this IMutation<TArgs, TData> mutation,
        IUiDispatcher? dispatcher = null
    )
    {
        ArgumentNullException.ThrowIfNull(mutation);

        return new MutationViewModel<TArgs, TData>(mutation, dispatcher);
    }

    /// <summary>
    /// Marshals every emission of <paramref name="source"/> onto the UI thread via
    /// <paramref name="dispatcher"/> — one dispatcher post per emission. Unlike the coalescing state
    /// binding the view models use internally, nothing here is dropped or collapsed: use this for
    /// <b>event</b> streams — <see cref="IQuery{TArgs, TData}.Success"/>,
    /// <see cref="IQuery{TArgs, TData}.Failure"/>, <see cref="IMutation{TArgs, TData}.Success"/>,
    /// <see cref="IMutation{TArgs, TData}.Settled"/>, and the like — where missing an emission would be
    /// a bug, unlike a snapshot of the latest state.
    /// <para>
    /// <paramref name="dispatcher"/> is captured when this method is <b>called</b>, not when the
    /// returned observable is subscribed. Call it on the UI thread, or pass a dispatcher explicitly, no
    /// matter where the eventual subscription happens.
    /// </para>
    /// <para>
    /// A callback already posted when the subscription is disposed, but not yet run, becomes a no-op
    /// instead of calling into the observer.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The source observable.</param>
    /// <param name="dispatcher">
    /// The UI-thread dispatcher. When <c>null</c>, <see cref="SynchronizationContext.Current"/> is
    /// captured at the moment this method is called.
    /// </param>
    public static IObservable<T> ObserveOnUi<T>(this IObservable<T> source, IUiDispatcher? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var effectiveDispatcher = dispatcher ?? SynchronizationContextUiDispatcher.CaptureCurrent();

        return Observable.Create<T>(observer =>
        {
            var isDisposed = false;

            var subscription = source.Subscribe(
                value =>
                    effectiveDispatcher.Post(() =>
                    {
                        if (!Volatile.Read(ref isDisposed))
                        {
                            observer.OnNext(value);
                        }
                    }),
                error =>
                    effectiveDispatcher.Post(() =>
                    {
                        if (!Volatile.Read(ref isDisposed))
                        {
                            observer.OnError(error);
                        }
                    }),
                () =>
                    effectiveDispatcher.Post(() =>
                    {
                        if (!Volatile.Read(ref isDisposed))
                        {
                            observer.OnCompleted();
                        }
                    })
            );

            return Disposable.Create(() =>
            {
                Volatile.Write(ref isDisposed, true);
                subscription.Dispose();
            });
        });
    }
}
