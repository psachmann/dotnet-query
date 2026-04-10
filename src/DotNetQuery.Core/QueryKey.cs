namespace DotNetQuery.Core;

/// <summary>
/// An immutable, equality-comparable key used to identify and share query cache entries.
/// Create instances via <see cref="From"/> or use <see cref="Default"/> as a sentinel for the uninitialized state.
/// </summary>
public sealed record QueryKey
{
    private QueryKey(IReadOnlyList<object> parts)
    {
        Parts = parts;
    }

    /// <summary>The ordered list of values that make up this key.</summary>
    public IReadOnlyList<object> Parts { get; private set; }

    /// <summary>
    /// A sentinel key representing the uninitialized state.
    /// Returned by <see cref="IQuery.Key"/> before args have been pushed for the first time.
    /// </summary>
    public static QueryKey Default { get; } = new(["\0"]);

    /// <summary>
    /// Creates a <see cref="QueryKey"/> from the given parts.
    /// </summary>
    /// <param name="parts">The values that make up the key. Must not be <c>null</c> and must not contain <c>null</c> elements.</param>
    /// <returns>A new <see cref="QueryKey"/> composed of the given parts.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="parts"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Thrown when any element in <paramref name="parts"/> is <c>null</c>.</exception>
    public static QueryKey From(params object[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts, nameof(parts));

        if (Array.Exists(parts, p => p is null))
        {
            throw new ArgumentException("QueryKey parts must not contain null elements.", nameof(parts));
        }

        return new(parts);
    }

    /// <inheritdoc/>
    public override string ToString() => string.Join(":", Parts.Select(p => p?.ToString() ?? "null"));

    /// <inheritdoc/>
    public bool Equals(QueryKey? other)
    {
        if (other is null)
        {
            return false;
        }

        return Parts.SequenceEqual(other.Parts);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var part in Parts)
        {
            hash.Add(part);
        }

        return hash.ToHashCode();
    }
}
