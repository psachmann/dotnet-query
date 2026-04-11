# Retry Handling

DotNet Query retries failed fetches and mutations automatically. Out of the box it uses an exponential backoff strategy, and you can swap in your own retry logic by implementing `IRetryHandler`.

## Default Behavior

By default, DotNet Query makes **one attempt** and does not retry. If the fetch or mutation throws, the exception is propagated immediately and the query transitions to `Failure` (or the mutation to `Failure`).

To add retry logic, configure a custom `IRetryHandler` — either globally or per query/mutation.

## Configuring the Global Retry Handler

You can replace the default handler globally when setting up the client:

```csharp
builder.Services.AddDotNetQuery(options =>
{
    options.RetryHandler = new MyRetryHandler();
});
```

## Overriding Per Query or Mutation

Override the retry handler for a specific query or mutation without touching the global config:

```csharp
// This query uses no retry — fail fast
new QueryOptions<int, UserDto>
{
    KeyFactory   = id => QueryKey.From("users", id),
    Fetcher      = (id, ct) => ...,
    RetryHandler = new NoRetryHandler(),
}

// This mutation uses a custom policy
new MutationOptions<PaymentRequest, Receipt>
{
    Mutator      = (req, ct) => ...,
    RetryHandler = new LinearRetryHandler(attempts: 3, delay: TimeSpan.FromSeconds(2)),
}
```

## Implementing a Custom Retry Handler

Implement `IRetryHandler` to define any retry policy you like:

```csharp
public sealed class LinearRetryHandler(int attempts, TimeSpan delay) : IRetryHandler
{
    public async Task<TData> ExecuteAsync<TData>(
        Func<CancellationToken, Task<TData>> action,
        CancellationToken cancellationToken = default)
    {
        Exception? last = null;

        for (var i = 0; i < attempts; i++)
        {
            try
            {
                return await action(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw; // never retry a cancellation
            }
            catch (Exception ex)
            {
                last = ex;

                if (i < attempts - 1)
                    await Task.Delay(delay, cancellationToken);
            }
        }

        ExceptionDispatchInfo.Throw(last!);
        throw null!; // unreachable
    }
}
```

A few things to keep in mind when writing a custom handler:

1. **Always rethrow `OperationCanceledException` immediately.** Never retry a cancellation.
2. **Use `ExceptionDispatchInfo.Throw(last)` to rethrow.** This preserves the original stack trace so the exception looks like it came from the original call site, not from inside your retry loop.
3. **Pass the `CancellationToken` to `Task.Delay`.** This ensures delays are interrupted cleanly on cancellation.

### A No-Retry Handler

If you want certain operations to fail fast without any retry:

```csharp
public sealed class NoRetryHandler : IRetryHandler
{
    public Task<TData> ExecuteAsync<TData>(
        Func<CancellationToken, Task<TData>> action,
        CancellationToken cancellationToken = default) => action(cancellationToken);
}
```

### Integrating with Polly

If your project already uses [Polly](https://www.thepollyproject.org/), you can wrap a Polly `ResiliencePipeline` in an `IRetryHandler`:

```csharp
public sealed class PollyRetryHandler(ResiliencePipeline pipeline) : IRetryHandler
{
    public Task<TData> ExecuteAsync<TData>(
        Func<CancellationToken, Task<TData>> action,
        CancellationToken cancellationToken = default) =>
        pipeline.ExecuteAsync(action, cancellationToken).AsTask();
}
```

Then register it:

```csharp
var pipeline = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 3 })
    .AddTimeout(TimeSpan.FromSeconds(10))
    .Build();

builder.Services.AddDotNetQuery(options =>
{
    options.RetryHandler = new PollyRetryHandler(pipeline);
});
```
