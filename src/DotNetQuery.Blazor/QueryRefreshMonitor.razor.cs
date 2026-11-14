namespace DotNetQuery.Blazor;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

/// <summary>
/// A side-effect component that automatically invalidates stale queries when the browser
/// comes back online or the user returns to the tab.
/// Place it once near the root of your app (e.g. <c>App.razor</c>).
/// </summary>
/// <remarks>
/// Invalidation respects each query's <c>StaleTime</c> — fresh data is never re-fetched.
/// Requires an interactive render mode (Blazor WASM or interactive Blazor Server); has no effect under static SSR.
/// </remarks>
public sealed partial class QueryRefreshMonitor : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private IJSObjectReference? _registration;
    private DotNetObjectReference<QueryRefreshMonitor>? _dotnetRef;

    /// <summary>When <c>true</c> (default), stale queries are invalidated when the browser tab regains focus.</summary>
    [Parameter]
    public bool RefetchOnFocus { get; set; } = true;

    /// <summary>When <c>true</c> (default), stale queries are invalidated when network connectivity is restored.</summary>
    [Parameter]
    public bool RefetchOnReconnect { get; set; } = true;

    /// <inheritdoc/>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _dotnetRef = DotNetObjectReference.Create(this);
        _module = await JS.InvokeAsync<IJSObjectReference>(
            "import",
            "./_content/DotNetQuery.Blazor/QueryRefreshMonitor.js"
        );

        _registration = await _module.InvokeAsync<IJSObjectReference>(
            "register",
            _dotnetRef,
            RefetchOnFocus,
            RefetchOnReconnect
        );
    }

    /// <summary>Called by the browser when the tab regains focus. Not intended for direct use.</summary>
    [JSInvokable]
    public void OnFocus() => QueryClient.Invalidate(_ => true);

    /// <summary>Called by the browser when network connectivity is restored. Not intended for direct use.</summary>
    [JSInvokable]
    public void OnReconnect() => QueryClient.Invalidate(_ => true);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_registration is not null)
            {
                await _registration.InvokeVoidAsync("unregister");
                await _registration.DisposeAsync();
            }

            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The circuit is already gone during normal Blazor Server teardown — nothing left to clean up.
        }

        _dotnetRef?.Dispose();
    }
}
