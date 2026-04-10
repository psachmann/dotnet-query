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

await builder.Build().RunAsync();
```

## User List Page

```razor
@page "/users"
@inject IQueryClient QueryClient
@inject HttpClient Http
@inject NavigationManager Nav
@implements IDisposable

<h1>Users</h1>

<button @onclick="HandleCreate" class="btn btn-primary">New User</button>

<Transition Query="_usersQuery">
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
                            <DeleteButton UserId="user.Id"
                                          OnDeleted="() => _usersQuery.Refetch()" />
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
    private IQuery<Unit, List<UserDto>> _usersQuery = default!;

    protected override void OnInitialized()
    {
        _usersQuery = QueryClient.CreateQuery(new QueryOptions<Unit, List<UserDto>>
        {
            KeyFactory = _ => QueryKey.From("users"),
            Fetcher    = (_, ct) => Http.GetFromJsonAsync<List<UserDto>>("/api/users", ct)!,
        });

        _usersQuery.Args.OnNext(Unit.Default);
    }

    private void HandleCreate() => Nav.NavigateTo("/users/new");

    public void Dispose() => _usersQuery.Dispose();
}
```

## User Detail Page

```razor
@page "/users/{Id:int}"
@inject IQueryClient QueryClient
@inject HttpClient Http
@inject NavigationManager Nav
@implements IDisposable

<Transition Query="_userQuery">
    <Content Context="user">
        <h1>@user.Name</h1>
        <dl>
            <dt>Email</dt>  <dd>@user.Email</dd>
            <dt>Role</dt>   <dd>@user.Role</dd>
            <dt>Joined</dt> <dd>@user.CreatedAt.ToShortDateString()</dd>
        </dl>

        <div class="actions">
            <button @onclick="() => Nav.NavigateTo($"/users/{Id}/edit")">Edit</button>
            <button @onclick="() => _userQuery.Refetch()">Refresh</button>
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

    private IQuery<int, UserDto> _userQuery = default!;

    protected override void OnInitialized()
    {
        _userQuery = QueryClient.CreateQuery(new QueryOptions<int, UserDto>
        {
            KeyFactory = id => QueryKey.From("users", id),
            Fetcher    = (id, ct) => Http.GetFromJsonAsync<UserDto>($"/api/users/{id}", ct)!,
            StaleTime  = TimeSpan.FromMinutes(5),
        });
    }

    protected override void OnParametersSet() => _userQuery.Args.OnNext(Id);

    public void Dispose() => _userQuery.Dispose();
}
```

## Create User Form

```razor
@page "/users/new"
@inject IQueryClient QueryClient
@inject HttpClient Http
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

    private IMutation<CreateUserRequest, UserDto> _mutation = default!;
    private IDisposable? _subscription;
    private bool _isBusy;
    private string? _errorMessage;

    protected override void OnInitialized()
    {
        _mutation = QueryClient.CreateMutation(new MutationOptions<CreateUserRequest, UserDto>
        {
            Mutator        = (req, ct) => Http.PostAsJsonAsync<UserDto>("/api/users", req, ct),
            InvalidateKeys = [QueryKey.From("users")],
        });

        _subscription = _mutation.State.Subscribe(async state =>
        {
            _isBusy       = state.IsRunning;
            _errorMessage = state.IsFailure ? state.Error!.Message : null;

            if (state.IsSuccess)
                Nav.NavigateTo($"/users/{state.CurrentData!.Id}");

            await InvokeAsync(StateHasChanged);
        });
    }

    private void HandleSubmit() => _mutation.Execute(_model);

    public void Dispose()
    {
        _subscription?.Dispose();
        _mutation.Dispose();
    }
}
```

## Delete Button Component

A reusable component that wraps a delete mutation:

```razor
@* Components/DeleteButton.razor *@
@inject IQueryClient QueryClient
@inject HttpClient Http
@implements IDisposable

<button @onclick="HandleClick"
        disabled="@_isDeleting"
        class="btn btn-danger btn-sm">
    @(_isDeleting ? "Deleting..." : "Delete")
</button>

@code {
    [Parameter, EditorRequired] public int UserId { get; set; }
    [Parameter] public EventCallback OnDeleted { get; set; }

    private IMutation<int, Unit> _mutation = default!;
    private IDisposable? _subscription;
    private bool _isDeleting;

    protected override void OnInitialized()
    {
        _mutation = QueryClient.CreateMutation(new MutationOptions<int, Unit>
        {
            Mutator = (id, ct) => Http.DeleteAsync($"/api/users/{id}", ct)
                                      .ContinueWith(_ => Unit.Default, ct),
        });

        _subscription = _mutation.State.Subscribe(async state =>
        {
            _isDeleting = state.IsRunning;

            if (state.IsSuccess)
                await OnDeleted.InvokeAsync();

            await InvokeAsync(StateHasChanged);
        });
    }

    private void HandleClick() => _mutation.Execute(UserId);

    public void Dispose()
    {
        _subscription?.Dispose();
        _mutation.Dispose();
    }
}
```
