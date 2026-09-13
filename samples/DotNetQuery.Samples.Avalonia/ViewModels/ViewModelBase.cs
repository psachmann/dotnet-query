namespace DotNetQuery.Samples.Avalonia.ViewModels;

public abstract class ViewModelBase : ObservableObject, IDisposable
{
    protected CompositeDisposable Disposables { get; } = [];

    public void Dispose()
    {
        Disposables.Dispose();
        GC.SuppressFinalize(this);
    }
}
