namespace DotNetQuery.Core.Internals;

internal interface IQueryInspector
{
    public QueryStatus Status { get; }

    public object? CurrentData { get; }

    public DateTimeOffset? LastUpdatedAt { get; }

    public int ObserverCount { get; }
}
