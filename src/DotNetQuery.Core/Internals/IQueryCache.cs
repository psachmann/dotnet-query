namespace DotNetQuery.Core.Internals;

internal interface IQueryCache : IDisposable
{
    Query<TArgs, TData> GetOrCreate<TArgs, TData>(QueryKey key, Query<TArgs, TData> query);

    void Remove(QueryKey key);

    void Invalidate(QueryKey key);

    void Invalidate(Func<QueryKey, bool> predicate);
}
