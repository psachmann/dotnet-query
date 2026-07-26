namespace DotNetQuery.Core.Internals;

/// <summary>What caused a <see cref="Query{TArgs,TData}"/> to fetch. Used only for observability tagging.</summary>
internal enum FetchTrigger
{
    /// <summary>An explicit call to <see cref="Query{TArgs,TData}.Refetch"/>.</summary>
    Manual,

    /// <summary>An explicit call to <c>Query{TArgs,TData}.Invalidate()</c> with active subscribers.</summary>
    Invalidate,

    /// <summary>A configured <c>RefetchInterval</c> tick.</summary>
    Interval,

    /// <summary>A deferred stale-while-revalidate fetch, triggered when the first subscriber joins a stale query.</summary>
    Stale,

    /// <summary>An explicit call to <see cref="IQuery{TArgs,TData}.PrefetchAsync"/>.</summary>
    Prefetch,
}
