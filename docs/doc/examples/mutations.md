# Example: Creating and Updating Data

This example demonstrates common mutation patterns: creating a resource, updating it, deleting it, and automatically refreshing related queries.

## Setup

```csharp
public sealed class UserMutations(IQueryClient queryClient, UserApiClient api) : IDisposable
{
    public readonly IMutation<CreateUserRequest, UserDto> CreateUser =
        queryClient.CreateMutation(new MutationOptions<CreateUserRequest, UserDto>
        {
            Mutator        = (req, ct) => api.CreateUserAsync(req, ct),
            InvalidateKeys = [QueryKey.From("users")],
            OnSuccess      = (req, user) => Console.WriteLine($"Created {user.Name}"),
        });

    public readonly IMutation<UpdateUserRequest, UserDto> UpdateUser =
        queryClient.CreateMutation(new MutationOptions<UpdateUserRequest, UserDto>
        {
            Mutator = (req, ct) => api.UpdateUserAsync(req, ct),
            // Invalidate both the list and the specific user entry
            InvalidateKeys = [
                QueryKey.From("users"),
                QueryKey.From("users", req.Id), // req is only available at execution time — see note below
            ],
        });

    public readonly IMutation<int, Unit> DeleteUser =
        queryClient.CreateMutation(new MutationOptions<int, Unit>
        {
            Mutator   = (id, ct) => api.DeleteUserAsync(id, ct),
            OnSuccess = (id, _) => Console.WriteLine($"Deleted user {id}"),
            OnFailure = error => Console.WriteLine($"Delete failed: {error.Message}"),
            OnSettled = () => Console.WriteLine("Delete operation finished"),
        });

    public void Dispose()
    {
        CreateUser.Dispose();
        UpdateUser.Dispose();
        DeleteUser.Dispose();
    }
}
```

> **Note on `InvalidateKeys`:** `InvalidateKeys` is evaluated when the mutation is *created*, not when it is *executed*. If you need to invalidate a key based on the args (e.g. `QueryKey.From("users", req.Id)`), do it in `OnSuccess` instead:
>
> ```csharp
> OnSuccess = (req, _) => queryClient.Invalidate(QueryKey.From("users", req.Id))
> ```

## Create User Form

```razor
@inject UserMutations Mutations
@implements IDisposable

<h2>Create User</h2>

@if (_errorMessage is not null)
{
    <div class="alert alert-danger">@_errorMessage</div>
}

<form @onsubmit="HandleSubmit">
    <div>
        <label>Name</label>
        <input @bind="_name" type="text" required />
    </div>
    <div>
        <label>Email</label>
        <input @bind="_email" type="email" required />
    </div>
    <button type="submit" disabled="@_isBusy">
        @(_isBusy ? "Saving..." : "Create User")
    </button>
</form>

@code {
    private string _name  = "";
    private string _email = "";
    private bool   _isBusy;
    private string? _errorMessage;

    private IDisposable? _subscription;

    protected override void OnInitialized()
    {
        _subscription = Mutations.CreateUser.State.Subscribe(state =>
        {
            _isBusy       = state.IsRunning;
            _errorMessage = state.IsFailure ? state.Error!.Message : null;
            InvokeAsync(StateHasChanged);
        });
    }

    private void HandleSubmit()
    {
        _errorMessage = null;
        Mutations.CreateUser.Execute(new CreateUserRequest
        {
            Name  = _name,
            Email = _email,
        });
    }

    public void Dispose() => _subscription?.Dispose();
}
```

## Delete with Optimistic Feedback

```razor
@inject UserMutations Mutations

<button @onclick="() => HandleDelete(user.Id)"
        disabled="@_isDeleting"
        class="btn btn-danger">
    @(_isDeleting ? "Deleting..." : "Delete")
</button>

@code {
    [Parameter] public UserDto User { get; set; } = default!;

    private bool _isDeleting;

    private void HandleDelete(int id)
    {
        _isDeleting = true;

        // Subscribe just for this execution
        Mutations.DeleteUser.Settled
            .Take(1)
            .Subscribe(_ =>
            {
                _isDeleting = false;
                InvokeAsync(StateHasChanged);
            });

        Mutations.DeleteUser.Execute(id);
    }
}
```

## Multiple Mutations in a Workflow

Here is a realistic scenario: uploading an avatar and then updating the user profile, keeping the UI in sync throughout:

```csharp
public sealed class ProfileWorkflow(IQueryClient queryClient, UserApiClient api) : IDisposable
{
    // Step 1: upload the avatar
    public readonly IMutation<Stream, string> UploadAvatar =
        queryClient.CreateMutation(new MutationOptions<Stream, string>
        {
            Mutator = (stream, ct) => api.UploadAvatarAsync(stream, ct),
        });

    // Step 2: save the profile (triggered after avatar upload succeeds)
    public readonly IMutation<UpdateProfileRequest, UserDto> SaveProfile =
        queryClient.CreateMutation(new MutationOptions<UpdateProfileRequest, UserDto>
        {
            Mutator        = (req, ct) => api.UpdateProfileAsync(req, ct),
            InvalidateKeys = [QueryKey.From("users", "me")],
        });

    public void Dispose()
    {
        UploadAvatar.Dispose();
        SaveProfile.Dispose();
    }
}
```

```razor
@inject ProfileWorkflow Workflow
@implements IDisposable

@code {
    private IDisposable? _avatarSuccessSub;

    protected override void OnInitialized()
    {
        // When avatar upload succeeds, chain into profile save
        _avatarSuccessSub = Workflow.UploadAvatar.Success.Subscribe(avatarUrl =>
        {
            Workflow.SaveProfile.Execute(new UpdateProfileRequest
            {
                AvatarUrl = avatarUrl,
                // ... other fields
            });
        });
    }

    private void HandleAvatarSelected(Stream avatarStream)
    {
        Workflow.UploadAvatar.Execute(avatarStream);
    }

    public void Dispose() => _avatarSuccessSub?.Dispose();
}
```
