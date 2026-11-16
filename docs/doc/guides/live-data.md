# Live Data: Server-Sent Events and SignalR

> **Not released yet.** Streamed queries and `IQueryClient.SetData` ship in the upcoming 3.0. The current stable release, 2.0.0, does not have them.

There are three ways data a server pushes — over [server-sent events](https://developer.mozilla.org/docs/Web/API/Server-sent_events) (SSE) or [SignalR](https://learn.microsoft.com/aspnet/core/signalr/introduction) — can reach the query cache:

| The push… | Example | Use |
|---|---|---|
| [**is** the data](#the-stream-is-the-data) | a price ticker, a log tail, notifications | a [streamed query](streamed-queries.md) over the stream |
| [**signals** a change](#an-event-signals-a-change) | `TodoUpdated(id)` | `IQueryClient.Invalidate`, so the query refetches |
| [**carries** the data](#an-event-carries-the-data) | `TodoUpdated(todoDto)` | `IQueryClient.SetData`, with no refetch |

Fetchling takes no dependency on SignalR or an SSE package. Both expose an `IAsyncEnumerable<T>` or plain callbacks, so the few lines that connect them to the cache live in your app.

## The Stream Is the Data

When the server streams the data itself, open the stream in a [streamed query](streamed-queries.md). Its fetcher returns an `IAsyncEnumerable` of items, and the reducer folds each one into the data.

**SignalR:** `HubConnection.StreamAsync<T>` already returns an `IAsyncEnumerable<T>`:

```csharp
var ticker = queryClient.CreateStreamedQuery(
    new StreamedQueryOptions<string, ImmutableList<PriceDto>, PriceDto>
    {
        KeyFactory = symbol => QueryKey.From("prices", symbol),
        Fetcher = (symbol, ct) => hub.StreamAsync<PriceDto>("WatchPrices", symbol, ct),
        InitialValue = [],
        // Keep only the latest 100: a live stream has no end, so neither would the list.
        Reducer = (prices, price) => prices.Count < 100 ? prices.Add(price) : prices.RemoveAt(0).Add(price),
        // Close the hub stream when nobody has watched it for 2 s; the next observer reopens it.
        CancelWhenUnobservedAfter = TimeSpan.FromSeconds(2),
    });
```

**SSE:** read the response as a stream and parse it with `SseParser` from the [`System.Net.ServerSentEvents`](https://www.nuget.org/packages/System.Net.ServerSentEvents) package:

```csharp
static async IAsyncEnumerable<PriceDto> WatchPrices(
    HttpClient http, string symbol, [EnumeratorCancellation] CancellationToken ct)
{
    using var response = await http.GetAsync(
        $"prices/{symbol}/stream", HttpCompletionOption.ResponseHeadersRead, ct);
    response.EnsureSuccessStatusCode();
    await using var stream = await response.Content.ReadAsStreamAsync(ct);

    var parser = SseParser.Create(stream, (_, data) => JsonSerializer.Deserialize<PriceDto>(data)!);
    await foreach (var item in parser.EnumerateAsync(ct))
    {
        yield return item.Data;
    }
}

// Fetcher = (symbol, ct) => WatchPrices(http, symbol, ct),
```

`HttpCompletionOption.ResponseHeadersRead` is required: without it, `HttpClient` buffers the whole response, which for a live stream never ends. In Blazor WebAssembly on .NET 10, the browser streams response bodies by default. Don't opt out with `<WasmEnableStreamingResponse>false</WasmEnableStreamingResponse>` or `SetBrowserResponseStreamingEnabled(false)`, or the stream never yields.

### What's different about a stream that never ends

A streamed query was designed for streams that complete, such as an LLM answer. A live stream works too, with these differences:

- **It never settles.** After the first item, the state is `Success` with `IsStreaming` set, and it stays that way. `Success` and `Settled` never emit. Render from `State`, `<Suspense>`/`<Transition>` or `QueryViewModel`, which all show the data as it grows, not from `Success`.
- **It never becomes fresh.** Data counts as fresh only after a completed fetch, so `StaleTime` has no effect, and the entry always reports `IsStale` in DevTools.
- **Don't prefetch it.** `PrefetchAsync` waits for the fetch to settle, which never happens, so it only returns when its token is cancelled. In particular, don't prefetch a live stream during server prerendering.
- **Bound the data.** The reducer runs for every item the stream will ever send. Keep only what you show, as the ticker above does with the latest 100.
- **Close it when nobody watches.** All observers of one key share one stream. By default, it stays open after the last observer goes away, until the entry is evicted `CacheTime` later (5 minutes by default). For a hub stream, that means a server-side subscription kept alive with nobody watching. Set [`CancelWhenUnobservedAfter`](streamed-queries.md#unobserved-streams), as the ticker above does, to stop it once nobody has observed it for a moment. The next observer reopens it.
- **Cancellation reaches the transport.** `Cancel()`, `Refetch()`, `CancelWhenUnobservedAfter`, eviction and disposing the client cancel the token the fetcher received. `StreamAsync` and `HttpClient` close the connection when it fires.
- **Invalidation doesn't restart it.** As for any streamed query, `Invalidate` leaves a running stream alone, and `Refetch()` restarts it from `InitialValue`.

### Reconnecting

When the connection drops, the stream throws and the query goes to `Failure`, with the data received so far in `LastData`. Nothing reopens it on its own. `<QueryRefreshMonitor>` does, when the browser comes back online or the tab regains focus: its invalidation reaches the failed entry, which is not streaming any more.

A [retry handler](retries.md) also reopens it, but it is a poor fit for a live stream:

- Every attempt restarts the fold from `InitialValue`, so the ticker loses its history on each drop.
- The retry budget covers the whole life of the stream, so a connection that drops a few times a day eventually runs out of retries.

Reconnect **inside the fetcher** instead. The query sees one stream that never fails, and the fold, along with the data, survives the drop:

```csharp
static async IAsyncEnumerable<PriceDto> WatchPricesResilient(
    HubConnection hub, string symbol, [EnumeratorCancellation] CancellationToken ct)
{
    var delay = TimeSpan.FromSeconds(1);

    while (!ct.IsCancellationRequested)
    {
        await using var prices = hub.StreamAsync<PriceDto>("WatchPrices", symbol, ct).GetAsyncEnumerator(ct);

        while (true)
        {
            bool moved;
            try
            {
                moved = await prices.MoveNextAsync();
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                break; // dropped: reconnect below
            }

            if (!moved)
            {
                yield break; // the server ended the stream
            }

            delay = TimeSpan.FromSeconds(1);
            yield return prices.Current;
        }

        await Task.Delay(delay, ct);
        delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TimeSpan.FromSeconds(30).Ticks));
    }
}
```

Items sent while the connection was down are lost. If the server can resume (an SSE `Last-Event-ID`, a sequence number in your hub method), pass the last value you saw when you reconnect.

## An Event Signals a Change

When the push only names what changed, invalidate it. The affected queries refetch with their own fetchers:

```csharp
hub.On<int>("TodoUpdated", id => queryClient.Invalidate(QueryKey.From("todos", id)));
hub.On("TodosChanged", () => queryClient.Invalidate(key => key.Parts is ["todos", ..]));
```

- **`Invalidate(QueryKey)` matches the key exactly.** `QueryKey.From("todos")` does not invalidate `QueryKey.From("todos", 1)`. To reach a group of keys, pass a predicate, as in the second line.
- **Only watched queries refetch.** A query with an active subscriber refetches right away. One without is only marked stale and refetches when it is next used.
- **Fresh data is refetched too.** `Invalidate` ignores `StaleTime`, so a query is refetched however recently it was fetched. (`InvalidateStale` is the variant that leaves fresh data alone.)
- **Events missed during a disconnect are lost.** When the connection comes back, invalidate everything the events would have touched:

  ```csharp
  hub.Reconnected += _ =>
  {
      queryClient.Invalidate(key => key.Parts is ["todos", ..]);
      return Task.CompletedTask;
  };
  ```

## An Event Carries the Data

When the push carries the new data, write it straight into the cache with `IQueryClient.SetData`. Every component showing that key updates, and no request is made:

```csharp
hub.On<TodoDto>("TodoUpdated", todo => queryClient.SetData(QueryKey.From("todos", todo.Id), todo));
```

The same item is often cached in several places: its detail query and every list that contains it. Patch them with an updater. The predicate overload applies it to every matching entry of the given data type and skips the rest, so a broad predicate is safe:

```csharp
hub.On<TodoDto>("TodoUpdated", todo =>
{
    queryClient.SetData(QueryKey.From("todos", todo.Id), todo);
    queryClient.SetData<ImmutableList<TodoDto>>(
        key => key.Parts is ["todos", "list", ..],
        list => list.Select(t => t.Id == todo.Id ? todo : t).ToImmutableList());
});
```

How a write behaves:

| Case | Behaviour |
|---|---|
| **Key not cached** | Nothing is written and `SetData` returns `false`. No entry means no observer is interested, and the client has no fetcher to create one with. |
| **Updater, entry without data** | The updater isn't called and `SetData` returns `false`. During a refetch it receives the data still shown. |
| **Fetch in flight** | The write wins. The fetch is cancelled, so its older result can't overwrite the push. |
| **Freshness** | The entry is stamped fresh: a write counts as a successful fetch for `StaleTime`. |
| **Streamed query** | Written only while no stream is running, because the next item would overwrite it. Returns `false` mid-stream. |
| **Different data type, infinite query** | `SetData(key, …)` throws `InvalidOperationException`. The predicate overload skips such entries. |

`IQuery.SetData`, used for [optimistic updates](optimistic-updates.md), writes through a query observer and leaves a fetch in flight alone. `IQueryClient.SetData` writes by key and cancels the fetch. Use the client one for pushed data.

## Threading

Hub and SSE handlers run on a background thread. That is fine: `Invalidate` and `SetData` are thread-safe, `<Suspense>`, `<Transition>` and the other Blazor components re-render through `InvokeAsync`, and `QueryViewModel` marshals to the UI thread itself. Only your own subscriptions to `State` need to marshal before touching UI.

## Where to Register the Handlers

A hub handler lives as long as its connection, so register it once per connection, not once per component. Where that is depends on how the query client is registered:

**Client-side apps** (Blazor WebAssembly, MAUI, WPF and the other MVVM hosts) have one client for the app (`QueryExecutionMode.Csr`, a singleton). Register the handlers once at startup, next to the connection:

```csharp
public sealed class TodoHubBridge(HubConnection hub, IQueryClient queryClient) : IAsyncDisposable
{
    private readonly IDisposable _updated = hub.On<TodoDto>(
        "TodoUpdated", todo => queryClient.SetData(QueryKey.From("todos", todo.Id), todo));

    public Task StartAsync(CancellationToken ct) => hub.StartAsync(ct);

    public async ValueTask DisposeAsync()
    {
        _updated.Dispose();
        await hub.DisposeAsync();
    }
}
```

**Blazor Server and other `QueryExecutionMode.Ssr` apps** have one client per circuit (scoped). A singleton or hosted service can't reach it. Register the same bridge as a **scoped** service, start it once per circuit (for example from the root layout's `OnInitializedAsync`, or a `CircuitHandler`), and let the scope dispose it when the circuit ends.

## See Also

- [Streamed Queries](streamed-queries.md) — states, refetch modes and options of streamed queries
- [Caching](caching.md) — `StaleTime`, `CacheTime` and eviction
- [Optimistic Updates](optimistic-updates.md) — writing predicted data before a mutation completes
- [Blazor Components](blazor.md) — `<QueryRefreshMonitor>`
