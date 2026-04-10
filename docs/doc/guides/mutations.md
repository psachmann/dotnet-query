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

    // Start disabled — Execute() calls are silently ignored until enabled
    IsEnabled = false,

    // Invalidate these query keys automatically on success
    InvalidateKeys = [QueryKey.From("users")],

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
             │
          Idle ──────► Running
                         │
                    ┌────┴────┐
                    ▼         ▼
                 Success   Failure
                    │         │
                    └────┬────┘
                         ▼
                        Idle
```

- **Idle** — initial state, or after a completed execution.
- **Running** — the mutator is executing.
- **Success** — the last execution succeeded. `CurrentData` holds the result.
- **Failure** — all retry attempts failed. `Error` holds the exception.

After reaching `Success` or `Failure`, the state returns to `Idle` on the next `Execute` call.

> **Note:** Unlike queries, mutations do not replay their last state to new subscribers — they emit only new transitions going forward.

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

## Lifecycle Callbacks

Callbacks let you react to mutation outcomes without subscribing to an observable:

```csharp
OnSuccess  = (request, result) => toast.Show($"Created: {result.Name}"),
OnFailure  = error => toast.Show($"Error: {error.Message}"),
OnSettled  = () => form.Reset(),
```

The execution order is always:
1. `InvalidateKeys` invalidation (if applicable)
2. `OnSuccess` or `OnFailure`
3. `OnSettled`

`OnSettled` fires for success, failure, **and** cancellation. It is the right place for "always run" cleanup like hiding spinners or resetting form state.

## Controlling Mutations

### Cancel

Cancel the currently running execution:

```csharp
mutation.Cancel();
```

The mutation returns to `Idle`. `OnSettled` is called but `OnSuccess` / `OnFailure` are not.

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
