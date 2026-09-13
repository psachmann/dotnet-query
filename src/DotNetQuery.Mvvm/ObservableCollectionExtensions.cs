using System.Collections.ObjectModel;

namespace DotNetQuery.Mvvm;

/// <summary>
/// Reconciles an <see cref="ObservableCollection{T}"/> in place against a new source sequence —
/// computing the minimal <see cref="System.Collections.Specialized.NotifyCollectionChangedAction.Move"/>,
/// <c>Insert</c>, <c>Remove</c>, and (same-type overload only) <c>Replace</c> operations needed to match
/// it, rather than clearing and re-adding. Clearing and re-adding drops whatever a bound control tracks
/// by collection position — the selected row in a WPF/Avalonia list, for instance.
/// </summary>
public static class ObservableCollectionExtensions
{
    /// <summary>
    /// Reconciles <paramref name="target"/> to match <paramref name="source"/> in place, keyed by
    /// <paramref name="keySelector"/>. An item whose key is no longer present is removed; a new key is
    /// inserted; an existing key is moved to its new position and, when <paramref name="itemComparer"/>
    /// reports it changed, replaced with the new value from <paramref name="source"/> — there is no way
    /// to update <typeparamref name="T"/> in place here, unlike the projection overload's <c>update</c>.
    /// When <paramref name="source"/> already matches <paramref name="target"/> exactly, nothing is raised.
    /// <para>
    /// Worst case is O(n²) moves — acceptable for UI-sized lists, not for large ones.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The element type, shared by both source and target.</typeparam>
    /// <typeparam name="TKey">The key type identifying an item's identity across syncs.</typeparam>
    /// <param name="target">The collection to reconcile in place.</param>
    /// <param name="source">The desired contents, in the desired order.</param>
    /// <param name="keySelector">Identifies an item's identity across syncs.</param>
    /// <param name="itemComparer">
    /// Decides whether an item at a matched key needs replacing. When <c>null</c>,
    /// <see cref="EqualityComparer{T}.Default"/> is used.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="source"/> contains a duplicate key.</exception>
    public static void SyncFrom<T, TKey>(
        this ObservableCollection<T> target,
        IEnumerable<T> source,
        Func<T, TKey> keySelector,
        IEqualityComparer<T>? itemComparer = null
    )
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keySelector);

        var comparer = itemComparer ?? EqualityComparer<T>.Default;
        var sourceList = MaterializeAndCheckDuplicateKeys(source, keySelector);

        RemoveKeysNotIn(target, keySelector, sourceList, keySelector);

        for (var i = 0; i < sourceList.Count; i++)
        {
            var desired = sourceList[i];
            var desiredKey = keySelector(desired);
            var currentIndex = IndexOfKeyFrom(target, keySelector, desiredKey, i);

            if (currentIndex < 0)
            {
                target.Insert(i, desired);

                continue;
            }

            if (currentIndex != i)
            {
                target.Move(currentIndex, i);
            }

            if (!comparer.Equals(target[i], desired))
            {
                target[i] = desired;
            }
        }
    }

    /// <summary>
    /// Reconciles <paramref name="target"/> to match <paramref name="source"/> in place, projecting each
    /// <typeparamref name="TSource"/> to a <typeparamref name="T"/> row (e.g. a view model built from a
    /// model). An item whose key is no longer present is removed; a new key is inserted via
    /// <paramref name="create"/>; an existing key is moved to its new position and, when
    /// <paramref name="update"/> is supplied, refreshed in place via <c>update(row, source)</c> — every
    /// sync, regardless of whether the source actually changed, so <paramref name="update"/> should
    /// assign through property setters that themselves no-op when the value is unchanged (as a
    /// <see cref="BindableBase"/>-style <c>SetProperty</c> does).
    /// <para>
    /// This overload never replaces a matched row — only <paramref name="create"/> ever produces a new
    /// <typeparamref name="T"/> instance. That is deliberate: some binding frameworks drop the selected
    /// row on a <c>Replace</c>, and a row's identity is exactly what a page view model wants to keep
    /// stable across a sync. When <paramref name="update"/> is omitted, a matched row is left untouched.
    /// </para>
    /// <para>
    /// Worst case is O(n²) moves — acceptable for UI-sized lists, not for large ones.
    /// </para>
    /// </summary>
    /// <typeparam name="TSource">The source element type (e.g. a model).</typeparam>
    /// <typeparam name="T">The target element type (e.g. a view model).</typeparam>
    /// <typeparam name="TKey">The key type identifying a row's identity across syncs.</typeparam>
    /// <param name="target">The collection to reconcile in place.</param>
    /// <param name="source">The desired contents, in the desired order.</param>
    /// <param name="sourceKey">Identifies a source item's identity across syncs.</param>
    /// <param name="targetKey">Identifies a row's identity across syncs.</param>
    /// <param name="create">Builds a new row for a source item with no matching existing row.</param>
    /// <param name="update">
    /// Refreshes an existing row from its matched source item. When <c>null</c>, matched rows are left
    /// untouched.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="source"/> contains a duplicate key.</exception>
    public static void SyncFrom<TSource, T, TKey>(
        this ObservableCollection<T> target,
        IEnumerable<TSource> source,
        Func<TSource, TKey> sourceKey,
        Func<T, TKey> targetKey,
        Func<TSource, T> create,
        Action<T, TSource>? update = null
    )
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceKey);
        ArgumentNullException.ThrowIfNull(targetKey);
        ArgumentNullException.ThrowIfNull(create);

        var sourceList = MaterializeAndCheckDuplicateKeys(source, sourceKey);

        RemoveKeysNotIn(target, targetKey, sourceList, sourceKey);

        for (var i = 0; i < sourceList.Count; i++)
        {
            var desired = sourceList[i];
            var desiredKey = sourceKey(desired);
            var currentIndex = IndexOfKeyFrom(target, targetKey, desiredKey, i);

            if (currentIndex < 0)
            {
                target.Insert(i, create(desired));

                continue;
            }

            if (currentIndex != i)
            {
                target.Move(currentIndex, i);
            }

            update?.Invoke(target[i], desired);
        }
    }

    private static List<TItem> MaterializeAndCheckDuplicateKeys<TItem, TKey>(
        IEnumerable<TItem> source,
        Func<TItem, TKey> keySelector
    )
        where TKey : notnull
    {
        var list = new List<TItem>();
        var seenKeys = new HashSet<TKey>();

        foreach (var item in source)
        {
            var key = keySelector(item);

            if (!seenKeys.Add(key))
            {
                throw new ArgumentException($"The source sequence contains duplicate key '{key}'.", nameof(source));
            }

            list.Add(item);
        }

        return list;
    }

    // Removes anything from target whose key is no longer wanted. Runs before the walk below so that
    // every subsequent IndexOfKeyFrom search only ever needs to skip forward past items already placed.
    private static void RemoveKeysNotIn<TItem, TSource, TKey>(
        ObservableCollection<TItem> target,
        Func<TItem, TKey> targetKey,
        List<TSource> sourceList,
        Func<TSource, TKey> sourceKey
    )
        where TKey : notnull
    {
        var wantedKeys = new HashSet<TKey>(sourceList.Select(sourceKey));

        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!wantedKeys.Contains(targetKey(target[i])))
            {
                target.RemoveAt(i);
            }
        }
    }

    private static int IndexOfKeyFrom<TItem, TKey>(
        IReadOnlyList<TItem> list,
        Func<TItem, TKey> keySelector,
        TKey key,
        int startIndex
    )
        where TKey : notnull
    {
        var comparer = EqualityComparer<TKey>.Default;

        for (var i = startIndex; i < list.Count; i++)
        {
            if (comparer.Equals(keySelector(list[i]), key))
            {
                return i;
            }
        }

        return -1;
    }
}
