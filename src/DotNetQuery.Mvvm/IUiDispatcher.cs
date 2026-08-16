namespace DotNetQuery.Mvvm;

/// <summary>
/// Marshals work onto the UI thread so that <see cref="System.ComponentModel.INotifyPropertyChanged"/>
/// notifications are raised where bindings expect them.
/// <para>
/// The default implementation is <see cref="SynchronizationContextUiDispatcher"/>. Supply a
/// platform-specific implementation to target the platform's dispatcher directly — e.g.
/// <c>MainThread.BeginInvokeOnMainThread</c> (MAUI), <c>DispatcherQueue.TryEnqueue</c> (WinUI/UNO),
/// or <c>Dispatcher.BeginInvoke</c> (WPF).
/// </para>
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Queues <paramref name="action"/> for asynchronous execution on the UI thread.
    /// </summary>
    /// <param name="action">The work to execute on the UI thread.</param>
    public void Post(Action action);
}
