using System.Runtime.CompilerServices;

namespace DotNetQuery.Mvvm;

/// <summary>
/// A minimal <see cref="INotifyPropertyChanged"/> base class for view models.
/// Deliberately not named <c>ObservableObject</c> to avoid ambiguity with MVVM toolkits
/// that may coexist in consuming applications.
/// </summary>
public abstract class BindableBase : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises <see cref="PropertyChanged"/> for <paramref name="propertyName"/>.
    /// Call on the UI thread — bindings expect notifications there.
    /// </summary>
    /// <param name="propertyName">The property name; defaults to the calling member.</param>
    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Sets <paramref name="field"/> to <paramref name="value"/> and raises <see cref="PropertyChanged"/>
    /// when the value changed according to <see cref="EqualityComparer{T}.Default"/>.
    /// </summary>
    /// <typeparam name="T">The property type.</typeparam>
    /// <param name="field">The backing field.</param>
    /// <param name="value">The new value.</param>
    /// <param name="propertyName">The property name; defaults to the calling member.</param>
    /// <returns><c>true</c> when the value changed and a notification was raised.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        RaisePropertyChanged(propertyName);

        return true;
    }
}
