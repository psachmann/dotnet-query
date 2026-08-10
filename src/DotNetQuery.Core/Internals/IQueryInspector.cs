namespace DotNetQuery.Core.Internals;

internal interface IQueryInspector : IQuery
{
    /// <summary>The low-cardinality name used to tag metrics for this query. See <see cref="QueryOptions{TArgs,TData}.Name"/>.</summary>
    public string MetricName { get; }

    public QueryStatus Status { get; }

    public object? CurrentData { get; }

    public DateTimeOffset? LastUpdatedAt { get; }

    public int ObserverCount { get; }

    public IObservable<Unit> StateChanged { get; }
}
