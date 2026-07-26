using System.Collections;
using System.Runtime.CompilerServices;

namespace DotNetQuery.Core;

/// <summary>
/// An immutable, equality-comparable key used to identify and share query cache entries.
/// Create instances via <see cref="From(object[])"/> or use <see cref="Default"/> as a sentinel for the uninitialized state.
/// Supports collection expression syntax: <c>QueryKey key = ["users", id];</c>
/// </summary>
[CollectionBuilder(typeof(QueryKey), nameof(From))]
public sealed record QueryKey : IEnumerable<object>
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
    /// Built from a private marker type, so no user-constructed <see cref="QueryKey"/> can ever equal it.
    /// </summary>
    public static QueryKey Default { get; } = new(new object[] { new UninitializedMarker() });

    private sealed record UninitializedMarker
    {
        public override string ToString() => "<uninitialized>";
    }

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

        if (Array.Exists(parts, part => part is null))
        {
            throw new ArgumentException("QueryKey parts must not contain null elements.", nameof(parts));
        }

        return new(parts);
    }

    /// <summary>
    /// Creates a <see cref="QueryKey"/> from a span of parts. Enables collection expression syntax.
    /// </summary>
    /// <param name="parts">The values that make up the key. Must not contain <c>null</c> elements.</param>
    /// <returns>A new <see cref="QueryKey"/> composed of the given parts.</returns>
    /// <exception cref="ArgumentException">Thrown when any element in <paramref name="parts"/> is <c>null</c>.</exception>
    public static QueryKey From(ReadOnlySpan<object> parts)
    {
        foreach (var part in parts)
        {
            if (part is null)
            {
                throw new ArgumentException("QueryKey parts must not contain null elements.", nameof(parts));
            }
        }

        return new(parts.ToArray());
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

    /// <summary>Enables the collection-expression element type and iteration over <see cref="Parts"/>.</summary>
    public IEnumerator<object> GetEnumerator() => Parts.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Parts.GetEnumerator();
}
