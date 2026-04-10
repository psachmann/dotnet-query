# Example: Fetching Data

This example shows a realistic user profile page that fetches a user and their posts, demonstrates background refresh, and handles error states.

## Setup

```csharp
// Program.cs
builder.Services.AddDotNetQuery(options =>
{
    options.StaleTime = TimeSpan.FromMinutes(2);
    options.CacheTime = TimeSpan.FromMinutes(10);
});

builder.Services.AddHttpClient<UserApiClient>(client =>
    client.BaseAddress = new Uri("https://api.example.com"));
```

## Service Layer

Define your queries in a service class so they are easy to share and test:

```csharp
public sealed class UserQueries(IQueryClient queryClient, UserApiClient api) : IDisposable
{
    public readonly IQuery<int, UserDto> UserQuery = queryClient.CreateQuery(
        new QueryOptions<int, UserDto>
        {
            KeyFactory = id => QueryKey.From("users", id),
            Fetcher    = (id, ct) => api.GetUserAsync(id, ct),
            StaleTime  = TimeSpan.FromMinutes(5),
        }
    );

    public readonly IQuery<int, List<PostDto>> PostsQuery = queryClient.CreateQuery(
        new QueryOptions<int, List<PostDto>>
        {
            KeyFactory      = id => QueryKey.From("users", id, "posts"),
            Fetcher         = (id, ct) => api.GetUserPostsAsync(id, ct),
            RefetchInterval = TimeSpan.FromSeconds(60),
        }
    );

    public void Dispose()
    {
        UserQuery.Dispose();
        PostsQuery.Dispose();
    }
}
```

## Blazor Component

```razor
@page "/users/{Id:int}"
@inject UserQueries Queries
@implements IDisposable

<h1>User Profile</h1>

<Transition Query="Queries.UserQuery">
    <Content Context="user">
        <div class="profile">
            <h2>@user.Name</h2>
            <p>@user.Email</p>
            <button @onclick="() => Queries.UserQuery.Refetch()">
                Refresh
            </button>
        </div>

        <h3>Recent Posts</h3>

        <Suspense Query="Queries.PostsQuery">
            <Content Context="posts">
                @if (posts.Count == 0)
                {
                    <p>No posts yet.</p>
                }
                else
                {
                    <ul>
                        @foreach (var post in posts)
                        {
                            <li>
                                <strong>@post.Title</strong>
                                <span>@post.CreatedAt.ToShortDateString()</span>
                            </li>
                        }
                    </ul>
                }
            </Content>
            <Loading>
                <p>Loading posts...</p>
            </Loading>
            <Failure Context="error">
                <p class="error">Could not load posts: @error.Message</p>
            </Failure>
        </Suspense>
    </Content>
    <Loading>
        <div class="skeleton">Loading user profile...</div>
    </Loading>
    <Failure Context="error">
        <div class="alert alert-danger">
            <p>Failed to load user: @error.Message</p>
            <button @onclick="() => Queries.UserQuery.Refetch()">Try Again</button>
        </div>
    </Failure>
</Transition>

@code {
    [Parameter] public int Id { get; set; }

    protected override void OnParametersSet()
    {
        Queries.UserQuery.Args.OnNext(Id);
        Queries.PostsQuery.Args.OnNext(Id);
    }

    public void Dispose() => Queries.Dispose();
}
```

## Without Blazor (Console / Worker Service)

```csharp
using var client = QueryClientFactory.Create(new QueryClientOptions
{
    StaleTime = TimeSpan.FromMinutes(1),
});

var query = client.CreateQuery(new QueryOptions<int, UserDto>
{
    KeyFactory = id => QueryKey.From("users", id),
    Fetcher    = (id, ct) => userApi.GetUserAsync(id, ct),
});

// Subscribe before pushing args to capture every state
using var subscription = query.State.Subscribe(state =>
{
    Console.WriteLine($"Status: {state.Status}");

    if (state.IsSuccess)
        Console.WriteLine($"User: {state.CurrentData!.Name}");

    if (state.IsFailure)
        Console.WriteLine($"Error: {state.Error!.Message}");
});

query.Args.OnNext(42);

// Wait for the result
var result = await query.Success.FirstAsync();
Console.WriteLine($"Done: {result.Name}");
```

## Conditional Query (Disabled by Default)

Sometimes you do not want to fetch until a condition is met — for example, a search box that only queries once the user has typed at least 3 characters:

```csharp
var searchQuery = queryClient.CreateQuery(new QueryOptions<string, List<UserDto>>
{
    KeyFactory = term => QueryKey.From("users", "search", term),
    Fetcher    = (term, ct) => userApi.SearchAsync(term, ct),
    IsEnabled  = false, // do not fetch until we have a valid term
});

// In your OnSearchTermChanged handler:
if (term.Length >= 3)
{
    searchQuery.SetEnabled(true);
    searchQuery.Args.OnNext(term);
}
else
{
    searchQuery.SetEnabled(false);
}
```
