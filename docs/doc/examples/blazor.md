# Example: Blazor Integration

This example builds a complete Blazor page with a list view, detail view, create form, and delete — wiring up queries and mutations end to end.

## Program.cs

```csharp
using DotNetQuery.Extensions.DependencyInjection;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddDotNetQuery(options =>
{
    options.StaleTime = TimeSpan.FromMinutes(2);
    options.CacheTime = TimeSpan.FromMinutes(15);
});

builder.Services.AddScoped<UserQueries>();
builder.Services.AddScoped<UserMutations>();

await builder.Build().RunAsync();
```

## Service Layer

Register queries and mutations in dedicated service classes and inject them into components. The services own the query and mutation instances and handle disposal — components stay focused on rendering.

```csharp
public sealed class UserQueries(IQueryClient queryClient, HttpClient http) : IDisposable
{
    public readonly IQuery<Unit, List<UserDto>> UsersQuery = queryClient.CreateQuery(
        new QueryOptions<Unit, List<UserDto>>
        {
            KeyFactory = _ => QueryKey.From("users"),
            Fetcher    = (_, ct) => http.GetFromJsonAsync<List<UserDto>>("/api/users", ct)!,
        });

    public readonly IQuery<int, UserDto> UserQuery = queryClient.CreateQuery(
        new QueryOptions<int, UserDto>
        {
            KeyFactory = id => QueryKey.From("users", id),
            Fetcher    = (id, ct) => http.GetFromJsonAsync<UserDto>($"/api/users/{id}", ct)!,
            StaleTime  = TimeSpan.FromMinutes(5),
        });

    public void Dispose()
    {
        UsersQuery.Dispose();
        UserQuery.Dispose();
    }
}

public sealed class UserMutations(IQueryClient queryClient, HttpClient http) : IDisposable
{
    public readonly IMutation<CreateUserRequest, UserDto> CreateUser = queryClient.CreateMutation(
        new MutationOptions<CreateUserRequest, UserDto>
        {
            Mutator        = (req, ct) => http.PostAsJsonAsync<UserDto>("/api/users", req, ct),
            InvalidateKeys = [QueryKey.From("users")],
        });

    public readonly IMutation<int, Unit> DeleteUser = queryClient.CreateMutation(
        new MutationOptions<int, Unit>
        {
            Mutator        = (id, ct) => http.DeleteAsync($"/api/users/{id}", ct)
                                             .ContinueWith(_ => Unit.Default, ct),
            InvalidateKeys = [QueryKey.From("users")],
        });

    public void Dispose()
    {
        CreateUser.Dispose();
        DeleteUser.Dispose();
    }
}
```

## User List Page

```razor
@page "/users"
@inject UserQueries Queries
@inject NavigationManager Nav

<h1>Users</h1>

<button @onclick="() => Nav.NavigateTo("/users/new")" class="btn btn-primary">New User</button>

<Transition Query="Queries.UsersQuery">
    <Content Context="users">
        <table class="table">
            <thead>
                <tr>
                    <th>Name</th>
                    <th>Email</th>
                    <th></th>
                </tr>
            </thead>
            <tbody>
                @foreach (var user in users)
                {
                    <tr>
                        <td>@user.Name</td>
                        <td>@user.Email</td>
                        <td>
                            <button @onclick="() => Nav.NavigateTo($"/users/{user.Id}")">
                                View
                            </button>
                            <DeleteButton UserId="user.Id" />
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    </Content>
    <Loading>
        <p>Loading users...</p>
    </Loading>
    <Failure Context="error">
        <p class="alert alert-danger">@error.Message</p>
    </Failure>
</Transition>

@code {
    protected override void OnInitialized()
    {
        Queries.UsersQuery.Args.OnNext(Unit.Default);
    }
}
```

## User Detail Page

```razor
@page "/users/{Id:int}"
@inject UserQueries Queries
@inject NavigationManager Nav

<Transition Query="Queries.UserQuery">
    <Content Context="user">
        <h1>@user.Name</h1>
        <dl>
            <dt>Email</dt>  <dd>@user.Email</dd>
            <dt>Role</dt>   <dd>@user.Role</dd>
            <dt>Joined</dt> <dd>@user.CreatedAt.ToShortDateString()</dd>
        </dl>

        <div class="actions">
            <button @onclick="() => Nav.NavigateTo($"/users/{Id}/edit")">Edit</button>
            <button @onclick="() => Queries.UserQuery.Refetch()">Refresh</button>
        </div>
    </Content>
    <Loading>
        <p>Loading...</p>
    </Loading>
    <Failure Context="error">
        <p>@error.Message</p>
        <button @onclick="() => Nav.NavigateTo("/users")">Back to list</button>
    </Failure>
</Transition>

@code {
    [Parameter] public int Id { get; set; }

    protected override void OnParametersSet() => Queries.UserQuery.Args.OnNext(Id);
}
```

## Create User Form

The component only disposes its own state subscription — the mutation itself is owned and disposed by the injected service.

```razor
@page "/users/new"
@inject UserMutations Mutations
@inject NavigationManager Nav
@implements IDisposable

<h1>New User</h1>

@if (_errorMessage is not null)
{
    <div class="alert alert-danger">@_errorMessage</div>
}

<EditForm Model="_model" OnValidSubmit="HandleSubmit">
    <DataAnnotationsValidator />
    <ValidationSummary />

    <div class="mb-3">
        <label class="form-label">Name</label>
        <InputText @bind-Value="_model.Name" class="form-control" />
    </div>

    <div class="mb-3">
        <label class="form-label">Email</label>
        <InputText @bind-Value="_model.Email" class="form-control" />
    </div>

    <button type="submit" disabled="@_isBusy" class="btn btn-primary">
        @(_isBusy ? "Creating..." : "Create")
    </button>
    <button type="button" @onclick="() => Nav.NavigateTo("/users")" class="btn btn-secondary">
        Cancel
    </button>
</EditForm>

@code {
    private readonly CreateUserRequest _model = new();

    private IDisposable? _subscription;
    private bool         _isBusy;
    private string?      _errorMessage;

    protected override void OnInitialized()
    {
        _subscription = Mutations.CreateUser.State.Subscribe(async state =>
        {
            _isBusy       = state.IsRunning;
            _errorMessage = state.IsFailure ? state.Error!.Message : null;

            if (state.IsSuccess)
                Nav.NavigateTo($"/users/{state.CurrentData!.Id}");

            await InvokeAsync(StateHasChanged);
        });
    }

    private void HandleSubmit() => Mutations.CreateUser.Execute(_model);

    public void Dispose() => _subscription?.Dispose();
}
```

## Delete Button Component

Uses `Settled.Take(1)` to track per-click state without holding a long-lived subscription. Because `DeleteUser` declares `InvalidateKeys`, the users list refetches automatically on success.

```razor
@* Components/DeleteButton.razor *@
@inject UserMutations Mutations

<button @onclick="HandleClick"
        disabled="@_isDeleting"
        class="btn btn-danger btn-sm">
    @(_isDeleting ? "Deleting..." : "Delete")
</button>

@code {
    [Parameter, EditorRequired] public int UserId { get; set; }

    private bool _isDeleting;

    private void HandleClick()
    {
        _isDeleting = true;

        Mutations.DeleteUser.Settled
            .Take(1)
            .Subscribe(async _ =>
            {
                _isDeleting = false;
                await InvokeAsync(StateHasChanged);
            });

        Mutations.DeleteUser.Execute(UserId);
    }
}
```
