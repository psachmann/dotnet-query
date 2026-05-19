namespace DotNetQuery.Core.Internals;

internal interface IQueryClientInspector
{
    public IObservable<IReadOnlyDictionary<QueryKey, IQuery>> CacheEntries { get; }
}
