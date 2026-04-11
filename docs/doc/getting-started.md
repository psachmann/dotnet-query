# Getting Started

This guide walks you through installing DotNet Query and writing your first query and mutation. By the end you will have a working setup and a solid foundation to build on.

## Prerequisites

- .NET 10.0 or later
- A project that uses `Microsoft.Extensions.DependencyInjection` (ASP.NET Core, Blazor, or any generic host) — or just the factory if you are wiring things up manually.

## Installation

Install the packages you need from NuGet:

```bash
# Core library — always required
dotnet add package DotNetQuery.Core

# DI integration — if you use Microsoft.Extensions.DependencyInjection
dotnet add package DotNetQuery.Extensions.DependencyInjection

# Blazor components — if you use Blazor
dotnet add package DotNetQuery.Blazor
```

## Setting Up the Client

### With Dependency Injection (recommended)

In your `Program.cs` (or wherever you configure services), call `AddDotNetQuery`:

```csharp
builder.Services.AddDotNetQuery();
```

That is the minimal setup. You can also configure the global defaults:

```csharp
builder.Services.AddDotNetQuery(options =>
{
    options.StaleTime       = TimeSpan.FromMinutes(1);   // data stays fresh for 1 minute
    options.CacheTime       = TimeSpan.FromMinutes(10);  // cache entries live 10 minutes after last subscriber
    options.RefetchInterval = TimeSpan.FromSeconds(30);  // automatically refetch every 30 seconds
});
```

The registered `IQueryClient` has a **Singleton** lifetime by default (correct for WebAssembly / CSR apps). See the [Server-Side Rendering guide](guides/ssr.md) if you are building a Blazor Server or SSR app.

### Without Dependency Injection

If you are not using a DI container, use `QueryClientFactory` directly:

```csharp
IQueryClient client = QueryClientFactory.Create(new QueryClientOptions
{
    StaleTime = TimeSpan.FromMinutes(1),
});
```

Remember to call `client.Dispose()` when you are done with it.

## Your First Query

A query needs two things: a **key factory** and a **fetcher**.

```csharp
public sealed class UserQueries(IQueryClient queryClient, HttpClient httpClient) : IDisposable
{
    public readonly IQuery<int, UserDto> UserQuery = queryClient.CreateQuery(
        new QueryOptions<int, UserDto>
        {
            KeyFactory = id => QueryKey.From("users", id),
            Fetcher    = (id, ct) => httpClient.GetFromJsonAsync<UserDto>($"/api/users/{id}", ct)
                                     ?? throw new InvalidOperationException("User not found."),
        }
    );

    public void Dispose() => UserQuery.Dispose();
}
```

Queries do not fetch anything on their own — they wait for you to push args:

```csharp
// Trigger a fetch for user 42
userQueries.UserQuery.SetArgs(42);
```

Then subscribe to the state stream to react to changes:

```csharp
userQueries.UserQuery.State.Subscribe(state =>
{
    if (state.IsFetching)
        ShowSpinner();

    if (state.IsSuccess)
        Render(state.CurrentData!);

    if (state.IsFailure)
        ShowError(state.Error!.Message);
});
```

Or use the shortcut streams if you only care about success or failure:

```csharp
userQueries.UserQuery.Success.Subscribe(user => Console.WriteLine($"Got user: {user.Name}"));
userQueries.UserQuery.Failure.Subscribe(error => Console.WriteLine($"Failed: {error.Message}"));
```

You can also read the current state synchronously at any time — useful for rendering without subscribing:

```csharp
var state = userQueries.UserQuery.CurrentState;
```

## Your First Mutation

A mutation needs a **mutator** function:

```csharp
public sealed class UserMutations(IQueryClient queryClient, HttpClient httpClient) : IDisposable
{
    public readonly IMutation<CreateUserRequest, UserDto> CreateUser =
        queryClient.CreateMutation(new MutationOptions<CreateUserRequest, UserDto>
        {
            Mutator = (request, ct) => httpClient.PostAsJsonAsync<UserDto>("/api/users", request, ct),

            // Automatically invalidate the "users" list query after a successful creation
            InvalidateKeys = [QueryKey.From("users")],

            OnSuccess = (request, user) => Console.WriteLine($"Created user {user.Id}"),
            OnFailure = error => Console.WriteLine($"Failed: {error.Message}"),
            OnSettled = () => Console.WriteLine("Mutation finished"),
        });

    public void Dispose() => CreateUser.Dispose();
}
```

Trigger the mutation by calling `Execute`:

```csharp
userMutations.CreateUser.Execute(new CreateUserRequest { Name = "Alice" });
```

Subscribe to the mutation state the same way you would a query:

```csharp
userMutations.CreateUser.State.Subscribe(state =>
{
    if (state.IsRunning) ShowSpinner();
    if (state.IsSuccess) NavigateTo("/users");
    if (state.IsFailure) ShowError(state.Error!.Message);
});
```

## Putting It Together in Blazor

If you are using Blazor, the `<Suspense>` and `<Transition>` components handle all the state-switching for you. Register the service classes and inject them into your components — components stay focused on rendering:

```csharp
// Program.cs
builder.Services.AddScoped<UserQueries>();
builder.Services.AddScoped<UserMutations>();
```

Render query state declaratively with `<Suspense>`:

```razor
@inject UserQueries Queries

<Suspense Query="Queries.UserQuery">
    <Content Context="user">
        <p>Hello, @user.Name!</p>
    </Content>
    <Loading>
        <p>Loading...</p>
    </Loading>
    <Failure Context="error">
        <p>Something went wrong: @error.Message</p>
    </Failure>
</Suspense>

@code {
    protected override void OnInitialized()
    {
        Queries.UserQuery.SetArgs(42);
    }
}
```

Subscribe to mutation state directly for form handling. The component only disposes its own subscription — the mutation itself is owned by the injected service:

```razor
@inject UserMutations Mutations
@inject NavigationManager Nav
@implements IDisposable

<button @onclick="HandleCreate">Create Alice</button>

@code {
    private IDisposable? _subscription;

    protected override void OnInitialized()
    {
        _subscription = Mutations.CreateUser.State.Subscribe(state =>
        {
            if (state.IsSuccess)
                Nav.NavigateTo($"/users/{state.CurrentData!.Id}");

            InvokeAsync(StateHasChanged);
        });
    }

    private void HandleCreate() =>
        Mutations.CreateUser.Execute(new CreateUserRequest { Name = "Alice" });

    public void Dispose() => _subscription?.Dispose();
}
```

See the [Blazor Components guide](guides/blazor.md) for more details, including the `<Transition>` component for stale-while-revalidate rendering.

## Next Steps

- [Queries guide](guides/queries.md) — everything about query configuration, lifecycle, and control.
- [Mutations guide](guides/mutations.md) — deep dive into mutations, callbacks, and invalidation.
- [Caching guide](guides/caching.md) — understand stale time, cache time, and deduplication.
- [Blazor Components guide](guides/blazor.md) — `<Suspense>` and `<Transition>` in detail.
- [Observability guide](guides/observability.md) — distributed tracing, metrics, and structured logging with OpenTelemetry.
- [Examples](examples/queries.md) — complete real-world examples.
