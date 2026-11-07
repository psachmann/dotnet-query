# Mutations

Mutations handle async write operations — creating, updating, or deleting data. They have their own state machine, support retry logic, and can automatically invalidate related queries on success.

## Creating a Mutation

Use `IQueryClient.CreateMutation` to create a mutation:

```csharp
IMutation<TArgs, TData> mutation = queryClient.CreateMutation(new MutationOptions<TArgs, TData>
{
    Mutator = (args, ct) => ...,
});
```

Only `Mutator` is required. Everything else is optional.

### Mutator

The mutator is the async function that performs the operation. It receives your args and a `CancellationToken` that is cancelled when the mutation is cancelled or disposed:

```csharp
Mutator = (request, ct) => httpClient.PostAsJsonAsync<UserDto>("/api/users", request, ct)
```

Always pass the `CancellationToken` to your async operations.

### MutationOptions Reference

```csharp
new MutationOptions<CreateUserRequest, UserDto>
{
    // Required: the async operation
    Mutator = (request, ct) => httpClient.PostAsJsonAsync<UserDto>("/api/users", request, ct),

    // Override the global retry handler for this mutation
    RetryHandler = new NoRetryHandler(),

    // Name used to tag metrics, traces, and log messages — defaults to typeof(TArgs).Name
    Name = "createUser",

    // Start disabled — Execute() calls are silently ignored until enabled
    IsEnabled = false,

    // Invalidate these query keys automatically on success
    InvalidateKeys = [QueryKey.From("users")],

    // Called synchronously before the mutator runs — use for optimistic updates
    OnMutate = request => { /* snapshot + SetData */ },

    // Called after a successful execution, before OnSettled
    OnSuccess = (request, user) => logger.LogInformation("Created user {Id}", user.Id),

    // Called after a failed execution (after all retries), before OnSettled
    OnFailure = error => logger.LogError(error, "Failed to create user"),

    // Called after every terminal state: success, failure, or cancellation
    OnSettled = () => isBusy = false,
}
```

## Executing a Mutation

Call `Execute` with the args to trigger the mutation:

```csharp
mutation.Execute(new CreateUserRequest { Name = "Alice" });
```

If you call `Execute` while a previous execution is still running, the previous one is cancelled and the new one starts immediately.

If the mutation is disabled (`IsEnabled = false` or `SetEnabled(false)` was called), `Execute` is silently ignored.

## Mutation Lifecycle

The mutation moves through four states:

```
             Execute(args)
   Idle ──────────────────► Running ◄───────────────┐
    ▲                          │                    │
    └────────── Cancel() ──────┤                    │
                          ┌────┴────┐               │
                          ▼         ▼               │
                       Success   Failure            │
                          │         │               │
                          └────┬────┘               │
                               └── Execute(args) ───┘
```

- **Idle** — the initial state, and the state after `Cancel()`.
- **Running** — the mutator is executing, including any retries.
- **Success** — the last execution succeeded. `CurrentData` holds the result.
- **Failure** — all retry attempts failed. `Error` holds the exception.

`Success` and `Failure` are terminal: the mutation stays there until the next `Execute` call, which goes straight to `Running`. It returns to `Idle` only when a run is cancelled.

> **Note:** Like queries, mutations replay their current state to new subscribers — a subscriber joining mid-run sees `Running` immediately. The shortcut streams below are derived from `State`, so `Success` and `Failure` also replay a result that is still current.

## Subscribing to Mutation State

### Full State Stream

```csharp
mutation.State.Subscribe(state =>
{
    switch (state.Status)
    {
        case MutationStatus.Idle:
            break;
        case MutationStatus.Running:
            ShowSpinner();
            break;
        case MutationStatus.Success:
            NavigateTo("/users");
            break;
        case MutationStatus.Failure:
            ShowError(state.Error!.Message);
            break;
    }
});
```

### Shortcut Streams

```csharp
mutation.Success.Subscribe(data => NavigateTo($"/users/{data.Id}"));
mutation.Failure.Subscribe(error => ShowError(error.Message));
mutation.Settled.Subscribe(_ => HideSpinner());
```

## Reporting Progress

For long-running writes — uploads, exports, imports, background jobs — pass a third type argument for the progress value. The mutator then receives an `IProgress<TProgress>`, and every state carries the latest reported value as `Progress`:

```csharp
IMutation<UploadRequest, DocumentDto, long> upload = queryClient.CreateMutation(
    new MutationOptions<UploadRequest, DocumentDto, long>
    {
        Mutator = async (request, progress, ct) =>
        {
            long sent = 0;

            await foreach (var chunk in request.File.ReadChunksAsync(ct))
            {
                await uploadClient.SendAsync(chunk, ct);
                sent += chunk.Length;
                progress.Report(sent);
            }

            return await uploadClient.CompleteAsync(ct);
        },
        InvalidateKeys = [QueryKey.From("inbox")],
    });

upload.State.Subscribe(state =>
{
    if (state.IsRunning)
    {
        ShowProgress(state.Progress, request.File.Length);
    }
});
```

Everything else — `InvalidateKeys`, the lifecycle callbacks, `Cancel()`, retries, `SetEnabled` — works exactly as for a mutation without progress.

How `Progress` behaves:

| Moment | `Status` | `Progress` |
|--------|----------|------------|
| Before the first report | `Running` | `default` (e.g. `0`) |
| After `progress.Report(value)` | `Running` | `value` |
| At the start of a retry | `Running` | back to `default` — a retried mutator starts over |
| After success / failure | `Success` / `Failure` | the last value reported, so a UI can show "failed at 60 %" |
| After `Cancel()` | `Idle` | cleared |

Progress travels on `State` rather than a separate stream. Mutation state replays to new subscribers, so a component that subscribes in the middle of an upload renders the current progress straight away, and status and progress can never disagree.

`Report` may be called from any thread. Each call publishes synchronously on that thread, in order with the other transitions. Reports made after the attempt is over are ignored — after the mutator returned or threw, after `Cancel()`, or once a retry or a newer `Execute` has superseded it. This is also why the library hands you its own `IProgress<T>`: `System.Progress<T>` posts to the captured `SynchronizationContext`, so its reports could arrive after the mutation had already settled.

> **Reports are not throttled.** A mutator that reports on every 4 KB chunk of a large file emits that many states. Either report at a coarser granularity, or sample the stream where you render it:
>
> ```csharp
> _subscription = upload.State
>     .Sample(TimeSpan.FromMilliseconds(100))
>     .Subscribe(state => InvokeAsync(() => { _state = state; StateHasChanged(); }));
> ```
>
> `MutationViewModel<TArgs, TData, TProgress>` in the [MVVM package](mvvm.md) already coalesces to one UI update per dispatcher hop, so it needs no throttling.

> **Retries and one-shot inputs:** A retry calls the mutator again from the start. A mutator that consumes something it cannot re-read — a non-seekable `Stream`, a network body, an enumerator — will fail or send a truncated payload on the second attempt. Either re-open the input inside the mutator, or opt this mutation out of retries with `RetryHandler = new NoRetryHandler()` (see [Retries](retries.md)).

## Automatic Cache Invalidation

The most common pattern after a successful mutation is to invalidate related queries so they refetch fresh data. Use `InvalidateKeys`:

```csharp
var createMutation = queryClient.CreateMutation(new MutationOptions<CreateUserRequest, UserDto>
{
    Mutator        = (req, ct) => ...,
    InvalidateKeys = [QueryKey.From("users")],
});
```

After a successful execution, the client calls `Invalidate(QueryKey.From("users"))`, which marks all matching cache entries as stale and triggers a refetch if they have active subscribers.

Invalidation happens before `OnSuccess` is called, so your callback already sees up-to-date data if it reads from the cache.

You can invalidate multiple keys at once:

```csharp
InvalidateKeys = [
    QueryKey.From("users"),
    QueryKey.From("users", userId, "posts"),
],
```

> **Timing note:** `InvalidateKeys` invalidates immediately when the mutator's `Task` resolves. For eventually-consistent backends — read replicas, CQRS read models, async side-effects — the re-fetch may arrive before the write is visible and return stale data. In those cases use `OnSuccess` instead, where you control when to invalidate:
>
> ```csharp
> OnSuccess = (req, _) => queryClient.Invalidate(QueryKey.From("users"))
> ```
>
> If you need a delay before re-fetching, make `OnSuccess` an `async` lambda:
>
> ```csharp
> OnSuccess = async (req, _) =>
> {
>     await Task.Delay(500); // allow replica to catch up
>     queryClient.Invalidate(QueryKey.From("users"));
> }
> ```

## Lifecycle Callbacks

Callbacks let you react to mutation outcomes without subscribing to an observable:

```csharp
OnMutate   = args  => { /* snapshot + apply optimistic update */ },
OnSuccess  = (request, result) => toast.Show($"Created: {result.Name}"),
OnFailure  = error => toast.Show($"Error: {error.Message}"),
OnSettled  = () => form.Reset(),
```

The execution order is always:
1. `OnMutate` (before the mutator runs)
2. `InvalidateKeys` invalidation (if applicable, on success)
3. `OnSuccess` or `OnFailure`
4. `OnSettled`

### OnMutate

`OnMutate` is called synchronously with the mutation args immediately before the mutator starts. It is the entry point for optimistic updates: snapshot the current cache state, cancel any in-flight fetch, and write the predicted result via `query.SetData(...)`. If the mutation later fails, restore the snapshot in `OnFailure`.

```csharp
UserDto? _snapshot = null;

OnMutate = user =>
{
    _snapshot = UserQuery.CurrentState.CurrentData;
    UserQuery.Cancel();
    UserQuery.SetData(user);
},
OnFailure = _ =>
{
    if (_snapshot is not null)
    {
        UserQuery.SetData(_snapshot);
    }
},
```

See the [Optimistic Updates guide](optimistic-updates.md) for the full pattern.

`OnSettled` fires for success, failure, **and** cancellation. It is the right place for "always run" cleanup like hiding spinners or resetting form state.

## Controlling Mutations

### Cancel

Cancel the currently running execution:

```csharp
mutation.Cancel();
```

The mutation returns to `Idle`. `OnSettled` is called but `OnSuccess` / `OnFailure` are not.

Calling `Execute` while a run is in flight also cancels that run, in favour of the new one. The cancelled run's `OnSettled` still fires, but it never touches `State`: the mutation stays `Running` for the new execution.

### SetEnabled

Disable a mutation to prevent execution:

```csharp
mutation.SetEnabled(false); // Execute() calls are silently ignored
mutation.SetEnabled(true);  // re-enables
```

## Cleaning Up

Mutations implement `IDisposable`. Dispose when you no longer need the mutation:

```csharp
mutation.Dispose();
```
