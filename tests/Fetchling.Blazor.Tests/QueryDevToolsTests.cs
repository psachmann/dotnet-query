namespace Fetchling.Blazor.Tests;

using System.Reflection;
using Fetchling.Blazor.DevTools;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

public class QueryDevToolsTests
{
    private readonly BunitContext _context = new();
    private readonly List<IDisposable> _subscriptions = [];
    private IQueryClient _client = default!;
    private BunitJSModuleInterop _jsModule = default!;

    [Before(Test)]
    public void Setup()
    {
        _client = QueryClientFactory.Create(new QueryClientOptions());
        _context.Services.AddSingleton(_client);

        _jsModule = _context.JSInterop.SetupModule("./_content/Fetchling.Blazor.DevTools/QueryDevTools.js");
        _jsModule.SetupVoid("init", _ => true).SetVoidResult();
        _jsModule.Setup<string>("getTheme", _ => true).SetResult("dark");
        _jsModule.SetupVoid("setTheme", _ => true).SetVoidResult();
        _jsModule.SetupVoid("startResize", _ => true).SetVoidResult();
        _jsModule.SetupVoid("startDetailResize", _ => true).SetVoidResult();
    }

    [After(Test)]
    public void Teardown()
    {
        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }

        _client.Dispose();
        _context.Dispose();
    }

    private void AddCacheEntry(string key, TimeSpan? cacheTime = null)
    {
        var query = _client.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = _ => QueryKey.From(key),
                Fetcher = (_, _) => Task.FromResult("data"),
                CacheTime = cacheTime,
            }
        );
        _subscriptions.Add(query.State.Subscribe());
        query.SetArgs(0);
    }

    private IRenderedComponent<QueryDevTools> OpenPanel()
    {
        var cut = _context.Render<QueryDevTools>();
        cut.Find("button.dnq-fab").Click();
        return cut;
    }

    [Test]
    public void InitialRender_ShowsOpenButton()
    {
        var cut = _context.Render<QueryDevTools>();

        cut.Find("button.dnq-fab");
    }

    [Test]
    public async Task InitialRender_PanelIsHidden()
    {
        var cut = _context.Render<QueryDevTools>();

        await Assert.That(cut.FindAll(".dnq-panel").Count).IsEqualTo(0);
    }

    [Test]
    public void ClickOpenButton_ShowsPanel()
    {
        var cut = _context.Render<QueryDevTools>();

        cut.Find("button.dnq-fab").Click();

        cut.Find(".dnq-panel");
    }

    [Test]
    public async Task ClickCloseButton_HidesPanel()
    {
        var cut = _context.Render<QueryDevTools>();
        cut.Find("button.dnq-fab").Click();

        cut.Find(".dnq-close-btn").Click();

        await Assert.That(cut.FindAll(".dnq-panel").Count).IsEqualTo(0);
    }

    [Test]
    public void EmptyCache_ShowsEmptyMessage()
    {
        var cut = OpenPanel();

        cut.Find(".dnq-empty");
    }

    [Test]
    public void CacheWithEntry_ShowsQueryRow()
    {
        AddCacheEntry("test-key");

        var cut = OpenPanel();

        cut.WaitForAssertion(() => cut.Find(".dnq-query-row"));
    }

    [Test]
    public async Task CacheWithEntry_RowDisplaysQueryKey()
    {
        AddCacheEntry("my-key");

        var cut = OpenPanel();

        cut.WaitForAssertion(() => cut.Find(".dnq-key-text"));

        await Assert.That(cut.Find(".dnq-key-text").TextContent).Contains("my-key");
    }

    [Test]
    public async Task FilterInput_HidesNonMatchingRows()
    {
        AddCacheEntry("alpha");
        AddCacheEntry("beta");

        var cut = OpenPanel();

        cut.WaitForAssertion(() =>
        {
            var count = cut.FindAll(".dnq-query-row").Count;
            if (count < 2)
            {
                throw new InvalidOperationException($"Expected 2 rows, got {count}");
            }
        });

        cut.Find(".dnq-search").Input("alpha");

        await Assert.That(cut.FindAll(".dnq-query-row").Count).IsEqualTo(1);
    }

    [Test]
    public void FilterInput_NoMatchesShowsEmptyMessage()
    {
        AddCacheEntry("alpha");

        var cut = OpenPanel();

        cut.WaitForAssertion(() => cut.Find(".dnq-query-row"));

        cut.Find(".dnq-search").Input("xyz-nonexistent");

        cut.Find(".dnq-empty");
    }

    [Test]
    public void InvalidateAllButton_Click_CompletesWithoutError()
    {
        AddCacheEntry("key");

        var cut = OpenPanel();

        cut.WaitForAssertion(() => cut.Find(".dnq-query-row"));
        cut.Find("[title='Invalidate all queries']").Click();
    }

    // ── Row selection / detail panel ──────────────────────────────────────────

    [Test]
    public void ClickQueryRow_ShowsDetailPanel()
    {
        AddCacheEntry("my-key");

        var cut = OpenPanel();

        cut.WaitForAssertion(() => cut.Find(".dnq-query-row"));
        cut.Find(".dnq-query-row").Click();

        cut.Find(".dnq-detail");
    }

    [Test]
    public async Task ClickSelectedRow_TogglesDetailOff()
    {
        AddCacheEntry("my-key");

        var cut = OpenPanel();

        cut.WaitForAssertion(() => cut.Find(".dnq-query-row"));
        cut.Find(".dnq-query-row").Click();
        cut.Find(".dnq-detail");

        cut.Find(".dnq-query-row").Click();

        await Assert.That(cut.FindAll(".dnq-detail").Count).IsEqualTo(0);
    }

    [Test]
    public void DetailPanel_InvalidateButton_Click_CompletesWithoutError()
    {
        AddCacheEntry("my-key");

        var cut = OpenPanel();

        cut.WaitForAssertion(() => cut.Find(".dnq-query-row"));
        cut.Find(".dnq-query-row").Click();
        cut.Find(".dnq-detail");

        cut.FindAll(".dnq-btn").First(b => b.TextContent.Trim() == "Invalidate").Click();
    }

    [Test]
    public void DetailPanel_RefetchButton_Click_CompletesWithoutError()
    {
        AddCacheEntry("my-key");

        var cut = OpenPanel();

        cut.WaitForAssertion(() => cut.Find(".dnq-query-row"));
        cut.Find(".dnq-query-row").Click();
        cut.Find(".dnq-detail");

        cut.FindAll(".dnq-btn").First(b => b.TextContent.Trim() == "Refetch").Click();
    }

    [Test]
    public async Task OnResizeEnd_UpdatesPanelHeight()
    {
        var cut = OpenPanel();

        await cut.InvokeAsync(() => cut.Instance.OnResizeEnd(250));
        cut.Render();

        await Assert.That(cut.Find(".dnq-panel").GetAttribute("style")).Contains("250px");
    }

    [Test]
    public async Task OnResizeEnd_BelowMinimum_ClampsTo150()
    {
        var cut = OpenPanel();

        await cut.InvokeAsync(() => cut.Instance.OnResizeEnd(50));
        cut.Render();

        await Assert.That(cut.Find(".dnq-panel").GetAttribute("style")).Contains("150px");
    }

    [Test]
    public void ResizeHandle_MouseDown_InvokesStartResize()
    {
        var cut = OpenPanel();

        cut.Find(".dnq-resize-handle").TriggerEvent("onmousedown", new MouseEventArgs { ClientY = 200 });

        _jsModule.VerifyInvoke("startResize", calledTimes: 1);
    }

    [Test]
    public async Task DetailPanel_CacheTime_DisplaysSeconds()
    {
        AddCacheEntry("key", cacheTime: TimeSpan.FromSeconds(30));

        var cut = OpenPanel();

        cut.WaitForAssertion(() => cut.Find(".dnq-query-row"));
        cut.Find(".dnq-query-row").Click();

        await Assert.That(cut.Find(".dnq-detail-rows").TextContent).Contains("30s");
    }

    [Test]
    public async Task DetailPanel_CacheTime_DisplaysHours()
    {
        AddCacheEntry("key", cacheTime: TimeSpan.FromHours(2));

        var cut = OpenPanel();

        cut.WaitForAssertion(() => cut.Find(".dnq-query-row"));
        cut.Find(".dnq-query-row").Click();

        await Assert.That(cut.Find(".dnq-detail-rows").TextContent).Contains("2h");
    }

    [Test]
    public async Task DetailPanel_CacheTime_DisplaysInfinity_WhenVeryLarge()
    {
        AddCacheEntry("key", cacheTime: TimeSpan.FromDays(400));

        var cut = OpenPanel();

        cut.WaitForAssertion(() => cut.Find(".dnq-query-row"));
        cut.Find(".dnq-query-row").Click();

        await Assert.That(cut.Find(".dnq-detail-rows").TextContent).Contains("∞");
    }

    [Test]
    public void QueryClient_WithoutInspector_RendersEmptyCache()
    {
        // Mock<IQueryClient> does not implement IQueryCacheInspector,
        // so TryGetInspector fails and OnInitialized returns early.
        using var ctx = new BunitContext();
        var jsm = ctx.JSInterop.SetupModule("./_content/Fetchling.Blazor.DevTools/QueryDevTools.js");
        jsm.SetupVoid("init", _ => true);
        jsm.Setup<string>("getTheme", _ => true).SetResult("dark");
        var mockClient = Mock.Of<IQueryClient>();
        ctx.Services.AddSingleton(mockClient.Object);

        var cut = ctx.Render<QueryDevTools>();
        cut.Find("button.dnq-fab").Click();

        cut.Find(".dnq-empty");
    }

    [Test]
    public async Task ResizeHandle_MouseDown_WhenModuleNull_DoesNotInvokeStartResize()
    {
        var cut = OpenPanel();

        typeof(QueryDevTools)
            .GetField("_module", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(cut.Instance, null);

        cut.Find(".dnq-resize-handle").TriggerEvent("onmousedown", new MouseEventArgs { ClientY = 200 });

        await Assert.That(_jsModule.Invocations.Any(i => i.Identifier == "startResize")).IsFalse();
    }

    [Test]
    public void CacheEntry_WithFetchingStatus_ShowsFetchingStatusPill()
    {
        var query = _client.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = _ => QueryKey.From("slow-key"),
                Fetcher = async (_, ct) =>
                {
                    await Task.Delay(Timeout.Infinite, ct);
                    return "";
                },
            }
        );
        _subscriptions.Add(query.State.Subscribe());
        query.SetArgs(0);

        var cut = OpenPanel();

        cut.WaitForAssertion(() => cut.Find(".dnq-status-fetching"));
    }

    [Test]
    public void CacheEntry_WithFailureStatus_ShowsFailureStatusPill()
    {
        var query = _client.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = _ => QueryKey.From("fail-key"),
                Fetcher = (_, _) => Task.FromException<string>(new Exception("boom")),
            }
        );
        _subscriptions.Add(query.State.Subscribe());
        query.SetArgs(0);

        var cut = OpenPanel();

        cut.WaitForAssertion(() => cut.Find(".dnq-status-failure"));
    }

    [Test]
    public async Task DetailPanel_NullData_DisplaysNullLiteral()
    {
        var query = _client.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = _ => QueryKey.From("null-data"),
                Fetcher = (_, _) => Task.FromResult<string>(null!),
            }
        );
        _subscriptions.Add(query.State.Subscribe());
        query.SetArgs(0);

        var cut = OpenPanel();

        cut.WaitForAssertion(() => cut.Find(".dnq-query-row"));
        cut.Find(".dnq-query-row").Click();

        await Assert.That(cut.Find(".dnq-detail-data").TextContent.Trim()).IsEqualTo("null");
    }

    [Test]
    public void DetailPanel_WhileRefetching_KeepsShowingPreviousData()
    {
        var calls = 0;
        var secondFetch = new TaskCompletionSource<string>();
        var query = _client.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = _ => QueryKey.From("refetching"),
                Fetcher = (_, _) => ++calls == 1 ? Task.FromResult("first-value") : secondFetch.Task,
            }
        );
        _subscriptions.Add(query.State.Subscribe());
        query.SetArgs(0);

        var cut = OpenPanel();

        cut.WaitForAssertion(() => cut.Find(".dnq-query-row"));
        cut.Find(".dnq-query-row").Click();
        cut.WaitForState(() => cut.Find(".dnq-detail-data").TextContent.Contains("first-value"));

        query.Refetch();

        cut.WaitForAssertion(() => cut.Find(".dnq-status-fetching"));
        cut.WaitForState(() => cut.Find(".dnq-detail-data").TextContent.Contains("first-value"));

        secondFetch.SetResult("second-value");
    }

    private sealed class CircularNode
    {
        public CircularNode? Next { get; set; }
    }

    [Test]
    public void DetailPanel_NonSerializableData_DisplaysFallbackString()
    {
        var node = new CircularNode();
        node.Next = node;

        var query = _client.CreateQuery(
            new QueryOptions<int, CircularNode>
            {
                KeyFactory = _ => QueryKey.From("circular"),
                Fetcher = (_, _) => Task.FromResult(node),
            }
        );
        _subscriptions.Add(query.State.Subscribe());
        query.SetArgs(0);

        var cut = OpenPanel();

        cut.WaitForAssertion(() => cut.Find(".dnq-query-row"));
        cut.Find(".dnq-query-row").Click();

        cut.WaitForAssertion(() => cut.Find(".dnq-detail-data"));
    }

    // ── Theme ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task ToggleTheme_Click_SwitchesToLightAndPersistsViaJs()
    {
        var cut = OpenPanel();

        cut.Find("[aria-label='Toggle theme']").Click();

        using var _ = Assert.Multiple();
        await Assert.That(cut.Find(".dnq-panel").ClassList).Contains("dnq-light");
        await Assert.That(_jsModule.Invocations["setTheme"][0].Arguments[0]).IsEqualTo("light");
    }

    [Test]
    public async Task ToggleTheme_ClickedTwice_SwitchesBackToDark()
    {
        var cut = OpenPanel();

        cut.Find("[aria-label='Toggle theme']").Click();
        cut.Find("[aria-label='Toggle theme']").Click();

        using var _ = Assert.Multiple();
        await Assert.That(cut.Find(".dnq-panel").ClassList).DoesNotContain("dnq-light");
        await Assert.That(_jsModule.Invocations["setTheme"][1].Arguments[0]).IsEqualTo("dark");
    }

    [Test]
    public async Task InitialRender_WhenStoredThemeIsLight_RendersLightPanel()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(_client);
        var module = ctx.JSInterop.SetupModule("./_content/Fetchling.Blazor.DevTools/QueryDevTools.js");
        module.SetupVoid("init", _ => true).SetVoidResult();
        module.Setup<string>("getTheme", _ => true).SetResult("light");

        var cut = ctx.Render<QueryDevTools>();
        cut.Find("button.dnq-fab").Click();

        await Assert.That(cut.Find(".dnq-panel").ClassList).Contains("dnq-light");
    }

    // ── Detail panel resizing ─────────────────────────────────────────────────

    private IRenderedComponent<QueryDevTools> OpenPanelWithDetail()
    {
        AddCacheEntry("my-key");

        var cut = OpenPanel();
        cut.WaitForAssertion(() => cut.Find(".dnq-query-row"));
        cut.Find(".dnq-query-row").Click();
        cut.Find(".dnq-detail");

        return cut;
    }

    [Test]
    public void DetailResizeHandle_MouseDown_InvokesStartDetailResize()
    {
        var cut = OpenPanelWithDetail();

        cut.Find(".dnq-detail-resize-handle").TriggerEvent("onmousedown", new MouseEventArgs { ClientX = 300 });

        _jsModule.VerifyInvoke("startDetailResize", calledTimes: 1);
    }

    [Test]
    public async Task OnDetailResizeEnd_UpdatesDetailWidth()
    {
        var cut = OpenPanelWithDetail();

        await cut.InvokeAsync(() => cut.Instance.OnDetailResizeEnd(500));
        cut.Render();

        await Assert.That(cut.Find(".dnq-detail").GetAttribute("style")).Contains("500px");
    }

    [Test]
    public async Task OnDetailResizeEnd_BelowMinimum_ClampsTo200()
    {
        var cut = OpenPanelWithDetail();

        await cut.InvokeAsync(() => cut.Instance.OnDetailResizeEnd(10));
        cut.Render();

        await Assert.That(cut.Find(".dnq-detail").GetAttribute("style")).Contains("200px");
    }

    [Test]
    public async Task DetailResizeHandle_MouseDown_WhenModuleNull_DoesNotInvokeStartDetailResize()
    {
        var cut = OpenPanelWithDetail();

        typeof(QueryDevTools)
            .GetField("_module", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(cut.Instance, null);

        cut.Find(".dnq-detail-resize-handle").TriggerEvent("onmousedown", new MouseEventArgs { ClientX = 300 });

        await Assert.That(_jsModule.Invocations.Any(i => i.Identifier == "startDetailResize")).IsFalse();
    }

    // ── Cache changes ─────────────────────────────────────────────────────────

    [Test]
    public async Task SelectedEntry_RemovedFromCache_ClosesDetailPanel()
    {
        var query = _client.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = _ => QueryKey.From("short-lived"),
                Fetcher = (_, _) => Task.FromResult("data"),
                CacheTime = TimeSpan.FromMilliseconds(50),
            }
        );
        var observer = query.State.Subscribe();
        query.SetArgs(0);

        var cut = OpenPanel();
        cut.WaitForAssertion(() => cut.Find(".dnq-query-row"));
        cut.Find(".dnq-query-row").Click();
        cut.Find(".dnq-detail");

        // Dropping the last observer lets the entry expire and be evicted from the cache.
        observer.Dispose();

        cut.WaitForAssertion(() => cut.Find(".dnq-empty"));
        await Assert.That(cut.FindAll(".dnq-detail").Count).IsEqualTo(0);
    }

    [Test]
    public async Task CloseButton_ClearsSelection()
    {
        var cut = OpenPanelWithDetail();

        cut.Find(".dnq-close-btn").Click();
        cut.Find("button.dnq-fab").Click();

        await Assert.That(cut.FindAll(".dnq-detail").Count).IsEqualTo(0);
    }

    [Test]
    public async Task CacheEntry_WithIdleStatus_ShowsIdleStatusPill()
    {
        var query = _client.CreateQuery(
            new QueryOptions<int, string>
            {
                KeyFactory = _ => QueryKey.From("idle-key"),
                Fetcher = (_, _) => Task.FromResult("data"),
                IsEnabled = false,
            }
        );
        _subscriptions.Add(query.State.Subscribe());
        query.SetArgs(0);

        var cut = OpenPanel();

        cut.WaitForAssertion(() => cut.Find(".dnq-status-idle"));
        await Assert.That(cut.Find(".dnq-status-pill").TextContent).IsEqualTo("Idle");
    }

    [Test]
    public async Task Header_ShowsCountsPerStatus()
    {
        AddCacheEntry("a");
        AddCacheEntry("b");

        var cut = OpenPanel();

        cut.WaitForAssertion(() => cut.Find(".dnq-badge-default").TextContent.Contains("Total 2"));
        await Assert.That(cut.Find(".dnq-badge-green").TextContent).Contains("Success 2");
    }

    // ── JS disconnected (Blazor Server circuit torn down) ─────────────────────

    private IRenderedComponent<QueryDevTools> RenderWithDisconnectedJs(BunitContext ctx, params string[] failingCalls)
    {
        ctx.Services.AddSingleton(_client);
        ctx.Services.AddSingleton<IJSRuntime>(new DisconnectingJSRuntime(failingCalls));

        return ctx.Render<QueryDevTools>();
    }

    [Test]
    [Arguments("import")]
    [Arguments("init")]
    [Arguments("getTheme")]
    public async Task FirstRender_WhenJSDisconnected_StillRendersClosedPanel(string failingCall)
    {
        using var ctx = new BunitContext();

        var cut = RenderWithDisconnectedJs(ctx, failingCall);

        await Assert.That(cut.FindAll("button.dnq-fab").Count).IsEqualTo(1);
    }

    [Test]
    public async Task ToggleTheme_WhenJSDisconnected_DoesNotThrowAndStillSwitchesTheme()
    {
        using var ctx = new BunitContext();
        var cut = RenderWithDisconnectedJs(ctx, "setTheme");
        cut.Find("button.dnq-fab").Click();

        cut.Find("[aria-label='Toggle theme']").Click();

        await Assert.That(cut.Find(".dnq-panel").ClassList).Contains("dnq-light");
    }

    [Test]
    public void ResizeHandle_MouseDown_WhenJSDisconnected_DoesNotThrow()
    {
        using var ctx = new BunitContext();
        var cut = RenderWithDisconnectedJs(ctx, "startResize");
        cut.Find("button.dnq-fab").Click();

        cut.Find(".dnq-resize-handle").TriggerEvent("onmousedown", new MouseEventArgs { ClientY = 200 });
    }

    [Test]
    public void DetailResizeHandle_MouseDown_WhenJSDisconnected_DoesNotThrow()
    {
        AddCacheEntry("my-key");
        using var ctx = new BunitContext();
        var cut = RenderWithDisconnectedJs(ctx, "startDetailResize");
        cut.Find("button.dnq-fab").Click();
        cut.WaitForAssertion(() => cut.Find(".dnq-query-row"));
        cut.Find(".dnq-query-row").Click();

        cut.Find(".dnq-detail-resize-handle").TriggerEvent("onmousedown", new MouseEventArgs { ClientX = 300 });
    }

    [Test]
    public async Task DisposeAsync_WhenJSDisconnected_DoesNotThrow()
    {
        using var ctx = new BunitContext();
        var cut = RenderWithDisconnectedJs(ctx, "dispose");

        await Assert.That(async () => await cut.Instance.DisposeAsync()).ThrowsNothing();
    }

    [Test]
    public async Task DisposeAsync_WithoutRender_DoesNotThrow()
    {
        var devTools = new QueryDevTools();

        await Assert.That(async () => await devTools.DisposeAsync()).ThrowsNothing();
    }

    [Test]
    public async Task ToggleTheme_WhenModuleNull_StillSwitchesThemeWithoutCallingJs()
    {
        var cut = OpenPanel();
        typeof(QueryDevTools)
            .GetField("_module", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(cut.Instance, null);

        cut.Find("[aria-label='Toggle theme']").Click();

        using var _ = Assert.Multiple();
        await Assert.That(cut.Find(".dnq-panel").ClassList).Contains("dnq-light");
        await Assert.That(_jsModule.Invocations.Any(i => i.Identifier == "setTheme")).IsFalse();
    }
}
