# Optimistic Updates

An optimistic update immediately writes a predicted result into the cache before the mutation reaches the server. The UI reflects the change instantly. If the server confirms success, the optimistic data stays (or is replaced by the real response via invalidation). If the mutation fails, you roll back to the snapshot.

## How It Works

There are three moments to handle:

1. **Before the mutation runs** (`OnMutate`) — snapshot the current cache state and write the optimistic data.
2. **On success** — the optimistic data is already in the cache. Use `InvalidateKeys` to trigger a refetch with the real server data.
3. **On failure** — restore the snapshot using `SetData` in `OnFailure`.

## The Building Blocks

### `SetData` on `IQuery<TArgs, TData>`

`SetData` directly writes data into the active cache entry as a success state, bypassing any in-flight fetch:

```csharp
query.SetData(optimisticValue);
```

It also stamps the entry as fresh so stale-time is respected and an immediate re-fetch is not triggered. All subscribers — across all components — see the new value instantly.

### `OnMutate` on `MutationOptions`

`OnMutate` is called synchronously with the mutation args immediately before the mutator runs. This is where you snapshot the current state and apply the optimistic update:

```csharp
OnMutate = args =>
{
    _snapshot = query.CurrentState.CurrentData;  // snapshot
    query.Cancel();                              // stop any in-flight fetch
    query.SetData(args);                         // apply optimistic update
},
```

## Full Example

The facade is the natural home for this pattern — it owns both the query and the mutation, so it has access to both in the same closure:

```csharp
public class TodoFacade
{
    private readonly ITodoApi _api;
    private TodoDto? _snapshot;

    public IQuery<int, TodoDto> TodoQuery { get; }
    public IMutation<TodoDto, TodoDto> UpdateTodo { get; }

    public TodoFacade(IQueryClient client, ITodoApi api)
    {
        _api = api;

        TodoQuery = client.CreateQuery(new QueryOptions<int, TodoDto>
        {
            KeyFactory = id => QueryKey.From("todos", id),
            Fetcher    = (id, ct) => _api.GetTodoAsync(id, ct),
            StaleTime  = TimeSpan.FromMinutes(5),
        });

        UpdateTodo = client.CreateMutation(new MutationOptions<TodoDto, TodoDto>
        {
            Mutator = (todo, ct) => _api.UpdateTodoAsync(todo, ct),

            OnMutate = todo =>
            {
                _snapshot = TodoQuery.CurrentState.CurrentData;  // save for rollback
                TodoQuery.Cancel();                              // cancel any in-flight fetch
                TodoQuery.SetData(todo);                         // apply optimistic update
            },

            OnFailure = _ =>
            {
                if (_snapshot is not null)
                    TodoQuery.SetData(_snapshot);                // roll back
            },

            // Replace optimistic data with the real server response
            InvalidateKeys = [QueryKey.From("todos", ...)],
        });
    }
}
```

### What happens step by step

1. User triggers `UpdateTodo.Execute(updatedTodo)`.
2. `OnMutate` fires synchronously — current data is snapshotted, any in-flight fetch is cancelled, and the optimistic value is written to the cache. All subscribed components re-render immediately with the new data.
3. The mutator calls the API.
4. **On success** — `InvalidateKeys` marks the cache entry as stale. Active subscribers trigger a background refetch, replacing the optimistic data with the server-confirmed value.
5. **On failure** — `OnFailure` restores the snapshot via `SetData`. All components revert to the previous data.

## Snapshot and Rollback

The snapshot is captured in a field (`_snapshot`) on the facade because `OnMutate` and `OnFailure` run at different points in time. A simple closure variable works equally well if the facade constructs mutations inline:

```csharp
TodoDto? snapshot = null;

UpdateTodo = client.CreateMutation(new MutationOptions<TodoDto, TodoDto>
{
    Mutator   = (todo, ct) => _api.UpdateTodoAsync(todo, ct),
    OnMutate  = todo  => { snapshot = TodoQuery.CurrentState.CurrentData; TodoQuery.SetData(todo); },
    OnFailure = _     => { if (snapshot is not null) TodoQuery.SetData(snapshot); },
});
```

> **Note:** Since a mutation cancels any previous in-flight execution before starting a new one (`.Switch()` semantics), there is only ever one active mutation at a time. A single snapshot field is safe.

## Cancelling In-Flight Fetches

It is good practice to call `query.Cancel()` in `OnMutate` before applying the optimistic update. Without it, a fetch that completes after the optimistic write could overwrite your data with stale server results.

```csharp
OnMutate = todo =>
{
    _snapshot = TodoQuery.CurrentState.CurrentData;
    TodoQuery.Cancel();   // prevent a concurrent fetch from clobbering the optimistic value
    TodoQuery.SetData(todo);
},
```

## Invalidation vs. Keeping Optimistic Data

After a successful mutation you have two choices:

- **Invalidate** (`InvalidateKeys`) — triggers a refetch that replaces the optimistic data with the confirmed server response. Use this when the server may transform or enrich the data.
- **Keep the optimistic data** — omit `InvalidateKeys` and the optimistic value stays in the cache as-is. Use this when you are confident the server stores exactly what you sent.
