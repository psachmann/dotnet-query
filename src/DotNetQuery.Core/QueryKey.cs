namespace DotNetQuery.Core;

public sealed record QueryKey
{
    private QueryKey(IReadOnlyList<object> parts)
    {
        Parts = parts;
    }

    public IReadOnlyList<object> Parts { get; private set; }

    public static QueryKey Default { get; } = new(["\0"]);

    public static QueryKey From(params object[] parts) => new(parts);

    public override string ToString() => string.Join(":", Parts.Select(p => p?.ToString() ?? "null"));

    public bool Equals(QueryKey? other)
    {
        if (other is null)
        {
            return false;
        }

        return Parts.SequenceEqual(other.Parts);
    }

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
