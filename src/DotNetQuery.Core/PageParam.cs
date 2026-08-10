namespace DotNetQuery.Core;

/// <summary>
/// Represents an optional page parameter used in infinite queries.
/// Return <see cref="None"/> from <c>GetNextPageParam</c> or <c>GetPreviousPageParam</c>
/// to signal that no further pages exist in that direction.
/// </summary>
/// <typeparam name="TValue">The type of the page parameter (e.g. <c>int</c>, <c>string</c>, <c>Guid</c>).</typeparam>
public readonly struct PageParam<TValue>
{
    private PageParam(TValue value, bool hasValue)
    {
        Value = value;
        HasValue = hasValue;
    }

    /// <summary>The page parameter value. Only meaningful when <see cref="HasValue"/> is <c>true</c>.</summary>
    public TValue Value { get; }

    /// <summary>
    /// <c>true</c> when this instance holds a page parameter; <c>false</c> when it represents <see cref="None"/>.
    /// </summary>
    public bool HasValue { get; }

    /// <summary>
    /// Signals that there are no further pages in this direction.
    /// Return this from <c>GetNextPageParam</c> or <c>GetPreviousPageParam</c> to end pagination.
    /// </summary>
    public static PageParam<TValue> None => default;

    /// <summary>Creates a <see cref="PageParam{TPageParam}"/> that holds <paramref name="value"/>.</summary>
    public static PageParam<TValue> Some(TValue value) => new(value, true);

    /// <summary>Implicitly converts <paramref name="value"/> to a <see cref="PageParam{TPageParam}"/> that holds it.</summary>
    public static implicit operator PageParam<TValue>(TValue value) => Some(value);
}
