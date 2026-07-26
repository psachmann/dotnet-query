namespace DotNetQuery.Core;

/// <summary>
/// Wraps a value together with an explicit presence flag, distinguishing "not provided" from
/// "provided as <c>default(T)</c>" — a distinction a plain nullable can't express for non-nullable
/// value types (e.g. <c>0</c> for <c>int</c>).
/// </summary>
/// <typeparam name="T">The type of the wrapped value.</typeparam>
public readonly record struct Optional<T>
{
    private Optional(bool hasValue, T? value)
    {
        HasValue = hasValue;
        Value = value;
    }

    /// <summary><c>true</c> when a value was explicitly provided.</summary>
    public bool HasValue { get; }

    /// <summary>The wrapped value. <c>default</c> when <see cref="HasValue"/> is <c>false</c>.</summary>
    public T? Value { get; }

    /// <summary>Represents the absence of a value.</summary>
    public static Optional<T> None => default;

    /// <summary>Wraps <paramref name="value"/> as a present value.</summary>
    public static Optional<T> Some(T value) => new(true, value);

    /// <summary>
    /// Implicitly wraps <paramref name="value"/>. A <c>null</c> reference converts to <see cref="None"/>,
    /// matching the "null means absent" convention used elsewhere in this library for reference types.
    /// </summary>
    public static implicit operator Optional<T>(T? value) => value is null ? None : Some(value);
}
