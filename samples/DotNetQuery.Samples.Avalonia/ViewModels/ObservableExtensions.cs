using System;
using Avalonia.Threading;

namespace DotNetQuery.Samples.Avalonia.ViewModels;

internal static class ObservableExtensions
{
    /// <summary>
    /// Subscribes to <paramref name="source"/> and dispatches every notification onto the UI thread.
    /// Query state is pushed from whichever thread the fetch completed on, so it has to be
    /// marshalled before it touches a bound property.
    /// </summary>
    public static IDisposable SubscribeOnUiThread<T>(this IObservable<T> source, Action<T> onNext) =>
        source.Subscribe(value => Dispatcher.UIThread.Post(() => onNext(value)));
}
