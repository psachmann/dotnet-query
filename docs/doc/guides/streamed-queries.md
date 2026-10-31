# Streamed Queries

> **Not released yet.** Streamed queries ship in the upcoming 3.0. The current stable release, 2.0.0, does not have them.

A streamed query is for a response that arrives in pieces and should be shown as it grows — a server-streaming gRPC call, LLM token output, a log tail. The fetcher returns an `IAsyncEnumerable` of chunks, and a reducer folds each chunk into the data as it arrives.

A streamed query is a regular `IQuery<TArgs, TData>`. Caching, deduplication, stale time, cancellation, retries, `<Suspense>`, `<Transition>` and `QueryViewModel` all work as they do for any query. The only addition is the `IsStreaming` flag on its state.

## Creating a Streamed Query

Use `IQueryClient.CreateStreamedQuery`:

```csharp
IQuery<string, string> answer = queryClient.CreateStreamedQuery(
    new StreamedQueryOptions<string, string, string>
    {
        KeyFactory = prompt => QueryKey.From("answer", prompt),
        Fetcher = (prompt, ct) => llm.StreamTokensAsync(prompt, ct), // IAsyncEnumerable<string>
        InitialValue = "",
        Reducer = (text, token) => text + token,
    });

answer.SetArgs("Explain stale-while-revalidate");
```

The three type arguments are `TArgs` (what identifies the stream), `TData` (the folded data, a reference type), and `TChunk` (one item the stream yields).

### Required Options

**`KeyFactory`** — same as regular queries. All observers with the same key share one cache entry **and one running stream**.

**`Fetcher`** — opens the stream for the given args. The `CancellationToken` is cancelled when the query is cancelled, disposed, or superseded by a refetch, and enumeration stops as soon as it is. It is called again, from the start, on every refetch and retry.

**`InitialValue`** — the seed every fetch starts folding from: `""` for text, `ImmutableList<T>.Empty` for a list. A stream that yields nothing completes with this value.

**`Reducer`** — folds one chunk into the data so far and returns the new data. Every intermediate value is published as a state, so **return a new value rather than mutating the old one**:

```csharp
// ✅ immutable — every state is its own snapshot
Reducer = (rows, row) => rows.Add(row), // ImmutableList<RowDto>

// ❌ mutates the list that earlier states already hold
Reducer = (rows, row) => { rows.Add(row); return rows; }, // List<RowDto>
```

### Optional Options

| Option | Default | Description |
|---|---|---|
| `RefetchMode` | `Reset` | What a refetch shows while it streams — see [Refetching](#refetching) |
| `StaleTime` | global | Same as for regular queries |
| `CacheTime` | global | Same as for regular queries |
| `RefetchInterval` | `null` | Polls on this interval. **Does not** fall back to the global `RefetchInterval` — see below |
| `RetryHandler` | global | Each attempt restarts the stream from `InitialValue` |
| `Name` | first key part | Low-cardinality name for metrics |
| `IsEnabled` | `true` | Same as for regular queries |
| `DataComparer` | `EqualityComparer<TData>.Default` | Compares the *completed* data against the previous completed data |

`RefetchInterval` is the one option that ignores the client-wide default. Re-opening a stream is rarely cheap (an LLM call, a long gRPC stream), so a global polling interval meant for ordinary queries should not quietly restart every stream in the app.

## State While Streaming

| Moment | `Status` | `IsStreaming` | `CurrentData` |
|---|---|---|---|
| Before the first chunk | `Fetching` | `false` | `null` |
| After each chunk | `Success` | `true` | the data folded so far |
| Stream completed | `Success` | `false` | the final data |
| Stream failed | `Failure` | `false` | `null` — the data received so far is in `LastData` |
| Cancelled | `Idle` | `false` | `null` — the data received so far is in `LastData` |

`State` emits once per chunk, and replays the current state to new subscribers, so a component that subscribes mid-stream renders the text received so far straight away.

`Success` and `Settled` emit **once per stream**, when it completes — not for every chunk. Use them for "the answer is done" work, and `State` (or the Blazor components) to render the growing data:

```csharp
answer.State.Subscribe(state => Render(state.CurrentData ?? state.LastData, isTyping: state.IsStreaming));
answer.Success.Subscribe(text => SaveToHistory(text));
```

> **Back-pressure:** a fast producer emits one state per chunk, with no throttling. If rendering every chunk is too much, sample the state stream where you render it — `answer.State.Sample(TimeSpan.FromMilliseconds(50))` — or yield coarser chunks. `QueryViewModel` already coalesces to one UI update per dispatcher hop.

## Failures and Retries

If the stream throws part-way through, the query goes to `Failure`, and `LastData` holds what had arrived so far — so a chat UI can keep showing the half-finished answer next to the error. (In `Replace` mode, where the partial data of a refetch was never shown, `LastData` keeps the previous completed data instead.)

A retry handler restarts the stream **from the beginning**: the fold starts again at `InitialValue`, and the state drops back to `Fetching` until the new attempt's first chunk.

## Refetching

What a refetch shows while the new stream runs depends on `RefetchMode`:

- **`Reset`** (default) — the previous data is dropped as soon as the refetch starts (`Fetching` with no `LastData`), and the new stream is shown as it grows. This is what a chat-style UI expects.
- **`Replace`** — the previous data stays visible (as `LastData` during `Fetching`, exactly like a regular query's background refetch) and is swapped for the new data when the stream completes. Chunks are folded out of sight in the meantime.

The very first fetch always shows chunks as they arrive, whichever mode is set — there is nothing to keep.

### Invalidation does not restart a running stream

`Invalidate()` — including the client-wide `Invalidate(...)` calls `<QueryRefreshMonitor>` makes on window focus and reconnect, and `RefetchInterval` ticks — **does not interrupt a stream that is still running**. It only marks the data stale, so the next subscriber to arrive refetches it. Restarting would throw away everything received so far, for an invalidation that usually has nothing to do with the stream.

Once the stream has completed, invalidation behaves exactly as for a regular query.

`Refetch()` always restarts the stream: the running one is cancelled (its token fires) and a new one starts.

## Cancellation

`Cancel()` and disposing the last owner cancel the token the fetcher received and stop enumeration. After `Cancel()`, the query is `Idle` and `LastData` holds what had arrived so far.

## Blazor Components

`<Suspense>` and `<Transition>` work unchanged. Both render `Content` as soon as the first chunk arrives and re-render on every chunk:

```razor
<Transition Query="Answer">
    <Content Context="text">
        <p>@text</p>
    </Content>
    <Loading><Spinner /></Loading>
</Transition>
```

The `Content` context is the data only. For a typing indicator, subscribe to `State` in the page and keep `IsStreaming` in a field, the same way you would track any other state flag.

## MVVM

`QueryViewModel<TArgs, TData>` exposes a bindable `IsStreaming`. `Data` grows with every chunk. `CancelCommand` stays enabled, and `RefetchCommand` disabled, until the stream completes.

## Not Covered: Raw Byte Streams

A download handed to the caller as a `Stream` is deliberately not a streamed query. A `Stream` can be read once by one reader, so two subscribers of one key could not share it. Caching it would mean buffering the whole payload, and a cache entry has no owner to dispose it. Model such a download as a [mutation](mutations.md) that writes to its destination instead.
