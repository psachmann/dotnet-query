namespace DotNetQuery.Blazor.Tests;

using System.Reflection;
using DotNetQuery.Blazor.DevTools;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

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

        _jsModule = _context.JSInterop.SetupModule("./_content/DotNetQuery.Blazor.DevTools/dotnetquery-devtools.js");
        _jsModule.SetupVoid("init", _ => true);
        _jsModule.SetupVoid("startResize", _ => true);
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
        // Mock<IQueryClient> does not implement IQueryClientInspector,
        // so OnInitialized hits the early return at line 39.
        using var ctx = new BunitContext();
        var jsm = ctx.JSInterop.SetupModule("./_content/DotNetQuery.Blazor.DevTools/dotnetquery-devtools.js");
        jsm.SetupVoid("init", _ => true);
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
}
