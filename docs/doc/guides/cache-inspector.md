# Building a Cache Inspector

> **Not released yet.** The public inspection API ships in the upcoming 3.0. In the current stable release, 2.0.0, it is internal and only `<QueryDevTools>` can use it.

`<QueryDevTools>` covers Blazor. For any other UI stack (Avalonia, MAUI, WPF, WinUI, UNO, or a console dump in a test harness), `DotNetQuery.Core` exposes the same live view of the cache that DevTools is built on, so you can build your own inspector.

## Getting the inspector

```csharp
using DotNetQuery.Core;
using DotNetQuery.Core.Inspection;

if (!queryClient.TryGetInspector(out var inspector))
{
    return; // a custom IQueryClient implementation (or a mock) that does not support inspection
}
```

Clients created by `QueryClientFactory.Create` or `AddDotNetQuery()` always support inspection. A custom `IQueryClient` supports it only if it also implements `IQueryCacheInspector`.

## Observing the cache

`IQueryCacheInspector.Entries` is an `IObservable<IReadOnlyList<IQueryCacheEntry>>`:

- It emits the **current** list as soon as you subscribe.
- It emits a fresh list whenever an entry is **added**, **evicted**, has a pending eviction **cancelled**, or **changes state** (starts fetching, succeeds, fails).
- It **completes** when the client is disposed.

Emissions are not coalesced. A single fetch produces several emissions in quick succession, so throttle or sample them if your inspector does expensive work per emission.

Each emitted list is a snapshot of *which* entries are in the cache. The entries themselves are **live views**: every property reads the entry's state at the moment you access it. Read what you need while handling the emission rather than holding on to an entry and reading it later.

## What an entry exposes

| Member | Description |
|--------|-------------|
| `Key` | The entry's `QueryKey`. |
| `Name` | The query's `Name` option, otherwise the first key part, otherwise `"unknown"`. The same value tags the entry's metrics. |
| `Status` | `Idle`, `Fetching`, `Success`, or `Failure`. |
| `CurrentData` | Untyped data. For a regular query, the latest successful data, and `null` while fetching or after a failure. For an infinite query, a `TData[]` of the loaded pages, kept across fetches. |
| `LastData` | For a regular query, the previous data carried across fetches and failures, which is the value to show while `CurrentData` is `null` during a refetch. Always `null` for infinite queries. |
| `Error` | The exception from the last failed fetch. |
| `LastUpdatedAt` | When data was last written (fetch or `SetData`), on the client's scheduler clock. |
| `ObserverCount` | Active subscribers to the entry's state. |
| `IsStale` | Never succeeded, older than `StaleTime`, or invalidated while unobserved. Evaluated on read: going stale by elapsed time alone does not trigger an emission. |
| `CacheTime` | How long the entry stays cached after its last subscriber leaves. |
| `Refetch()` | Forces a fetch, ignoring `StaleTime`. |
| `Invalidate()` | Marks the entry stale. Re-fetches now if it has subscribers, otherwise when the next one joins. |
| `Cancel()` | Cancels the in-flight fetch. |

There is deliberately no `Dispose()`: the cache owns its entries' lifetimes.

`CurrentData` and `LastData` hold your own types as `object?`, so rendering them is up to you. Reflection-based `JsonSerializer` calls are not trim- or AOT-safe. If your inspector ships in a trimmed or AOT-published app, render the types you know and fall back to `ToString()` for the rest.

## Threading

`Entries` emits on **whichever thread changed the cache**. That is usually a thread-pool thread finishing a fetch, or the scheduler's timer thread for evictions. Marshal onto the UI thread before touching UI state:

- **Blazor**: `InvokeAsync(StateHasChanged)`.
- **`DotNetQuery.Mvvm`**: `ObserveOnUi()`. It posts every emission, so nothing is dropped. Call it on the UI thread, or pass an `IUiDispatcher` explicitly.

## Example: an MVVM inspector

A minimal inspector view model for Avalonia, MAUI, WPF, WinUI or UNO. It copies each live entry into an immutable row on the UI thread, then reconciles a bound collection in place with `SyncFrom`, so a selected row survives a refresh.

```csharp
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DotNetQuery.Core;
using DotNetQuery.Core.Inspection;
using DotNetQuery.Mvvm;

public sealed record CacheRow(QueryKey Key, string Name, QueryStatus Status, int Observers, DateTimeOffset? UpdatedAt, bool IsStale);

public sealed class CacheInspectorViewModel : IDisposable
{
    private readonly IDisposable? _subscription;

    public ObservableCollection<CacheRow> Rows { get; } = [];

    // Construct on the UI thread: ObserveOnUi captures the dispatcher at call time.
    public CacheInspectorViewModel(IQueryClient client)
    {
        if (!client.TryGetInspector(out var inspector))
        {
            return;
        }

        _subscription = inspector.Entries
            .ObserveOnUi()
            .Subscribe(entries =>
            {
                var rows = entries
                    .Select(e => new CacheRow(e.Key, e.Name, e.Status, e.ObserverCount, e.LastUpdatedAt, e.IsStale))
                    .OrderBy(r => r.Key.ToString());

                Rows.SyncFrom(rows, row => row.Key);
            });
    }

    public void Dispose() => _subscription?.Dispose();
}
```

Holding the `Entries` subscription does **not** keep entries alive. Only subscribers to a query's own state count towards `ObserverCount` and eviction, so an open inspector never pins the cache.
