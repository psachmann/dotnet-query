namespace DotNetQuery.Blazor.DevTools;

/// <summary>
/// A Blazor component that renders a live view of the DotNetQuery cache,
/// showing each cached query's key, status, observer count, and current data.
/// Add it once in <c>MainLayout.razor</c> and guard it with <c>IHostEnvironment.IsDevelopment()</c>
/// to limit it to development environments.
/// </summary>
/// <example>
/// <code>
/// @inject IHostEnvironment Env
///
/// @if (Env.IsDevelopment())
/// {
///     &lt;QueryDevTools /&gt;
/// }
/// </code>
/// </example>
public sealed partial class QueryDevTools : IAsyncDisposable
{
    [Inject]
    private IQueryClient QueryClient { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    private bool _isOpen;
    private bool _isDarkMode = true;
    private string _filter = "";
    private int _panelHeight = 350;
    private int _detailWidth = 340;
    private IReadOnlyDictionary<QueryKey, IQuery> _cacheEntries = new Dictionary<QueryKey, IQuery>();
    private QueryKey? _selectedKey;
    private IDisposable? _cacheSubscription;
    private IJSObjectReference? _module;
    private DotNetObjectReference<QueryDevTools>? _dotnetRef;
    private ElementReference _panelRef;
    private ElementReference _detailRef;

    /// <inheritdoc/>
    protected override void OnInitialized()
    {
        if (QueryClient is not IQueryClientInspector inspector)
        {
            return;
        }

        _cacheSubscription = inspector.CacheEntries.Subscribe(entries =>
        {
            if (_selectedKey is not null && !entries.ContainsKey(_selectedKey))
            {
                _selectedKey = null;
            }

            _cacheEntries = entries;
            InvokeAsync(StateHasChanged);
        });
    }

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
            "./_content/DotNetQuery.Blazor.DevTools/QueryDevTools.js"
        );
        await _module.InvokeVoidAsync("init", _dotnetRef);

        var theme = await _module.InvokeAsync<string>("getTheme");
        _isDarkMode = theme != "light";
        StateHasChanged();
    }

    private async Task ToggleTheme()
    {
        _isDarkMode = !_isDarkMode;
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("setTheme", _isDarkMode ? "dark" : "light");
        }
    }

    private async Task OnResizeHandleMouseDown(MouseEventArgs e)
    {
        if (_module is null)
        {
            return;
        }

        await _module.InvokeVoidAsync("startResize", _panelRef, (int)e.ClientY, _panelHeight);
    }

    /// <summary>Called from JS when the user finishes dragging the panel height resize handle.</summary>
    [JSInvokable]
    public void OnResizeEnd(int height)
    {
        _panelHeight = Math.Max(150, height);
    }

    private async Task OnDetailResizeHandleMouseDown(MouseEventArgs e)
    {
        if (_module is null)
        {
            return;
        }

        await _module.InvokeVoidAsync("startDetailResize", _detailRef, (int)e.ClientX, _detailWidth);
    }

    /// <summary>Called from JS when the user finishes dragging the detail panel width resize handle.</summary>
    [JSInvokable]
    public void OnDetailResizeEnd(int width)
    {
        _detailWidth = Math.Max(200, width);
    }

    private IReadOnlyList<KeyValuePair<QueryKey, IQuery>> FilteredEntries
    {
        get
        {
            var all = _cacheEntries;

            if (string.IsNullOrWhiteSpace(_filter))
            {
                return [.. all];
            }

            var lower = _filter.Trim();

            return all.Where(e => e.Key.ToString().Contains(lower, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    private IQuery? SelectedQuery =>
        _selectedKey is not null && _cacheEntries.TryGetValue(_selectedKey, out var q) ? q : null;

    private int FetchingCount => CountByStatus(QueryStatus.Fetching);
    private int SuccessCount => CountByStatus(QueryStatus.Success);
    private int IdleCount => CountByStatus(QueryStatus.Idle);
    private int FailureCount => CountByStatus(QueryStatus.Failure);

    private int CountByStatus(QueryStatus status) => _cacheEntries.Values.Count(q => GetStatus(q) == status);

    private void Open() => _isOpen = true;

    private void Close()
    {
        _isOpen = false;
        _selectedKey = null;
    }

    private void ToggleSelected(QueryKey key) => _selectedKey = _selectedKey == key ? null : key;

    private bool IsSelected(QueryKey key) => _selectedKey == key;

    private void InvalidateAll() => QueryClient.Invalidate(_ => true);

    private static QueryStatus GetStatus(IQuery query) =>
        query is IQueryInspector insp ? insp.Status : QueryStatus.Idle;

    private static object? GetData(IQuery query) => query is IQueryInspector insp ? insp.CurrentData : null;

    private static DateTimeOffset? GetLastUpdated(IQuery query) =>
        query is IQueryInspector insp ? insp.LastUpdatedAt : null;

    private static int GetObserverCount(IQuery query) => query is IQueryInspector insp ? insp.ObserverCount : 0;

    private static string StatusCssClass(QueryStatus status) =>
        status switch
        {
            QueryStatus.Fetching => "dnq-status-fetching",
            QueryStatus.Success => "dnq-status-success",
            QueryStatus.Failure => "dnq-status-failure",
            _ => "dnq-status-idle",
        };

    private static string StatusLabel(QueryStatus status) =>
        status switch
        {
            QueryStatus.Fetching => "Fetching",
            QueryStatus.Success => "Success",
            QueryStatus.Failure => "Failure",
            _ => "Idle",
        };

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts == Timeout.InfiniteTimeSpan || ts >= TimeSpan.FromDays(365))
        {
            return "∞";
        }

        if (ts.TotalSeconds < 60)
        {
            return $"{ts.TotalSeconds:0.#}s";
        }

        if (ts.TotalMinutes < 60)
        {
            return $"{ts.TotalMinutes:0.#}m";
        }

        return $"{ts.TotalHours:0.#}h";
    }

    private static string SerializeData(object? data)
    {
        if (data is null)
        {
            return "null";
        }

        try
        {
            return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return data.ToString() ?? "null";
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _cacheSubscription?.Dispose();
        _dotnetRef?.Dispose();

        return _module?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
