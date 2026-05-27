namespace DotNetQuery.Core.Internals;

internal interface IQueryClientInspector
{
    public IObservable<IReadOnlyList<IQueryInspector>> CacheEntries { get; }
}
