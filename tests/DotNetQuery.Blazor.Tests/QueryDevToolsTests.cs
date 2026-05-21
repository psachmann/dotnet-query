namespace DotNetQuery.Blazor.Tests;

using DotNetQuery.Blazor.DevTools;
using Microsoft.Extensions.DependencyInjection;

public class QueryDevToolsTests
{
    private readonly BunitContext _context = new();
    private readonly List<IDisposable> _subscriptions = [];
    private IQueryClient _client = default!;

    [Before(Test)]
    public void Setup()
    {
        _client = QueryClientFactory.Create(new QueryClientOptions());
        _context.Services.AddSingleton(_client);

        var module = _context.JSInterop.SetupModule(
            "./_content/DotNetQuery.Blazor.DevTools/dotnetquery-devtools.js");
        module.SetupVoid("init", _ => true);
        module.SetupVoid("startResize", _ => true);
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

    private void AddCacheEntry(string key)
    {
        var query = _client.CreateQuery(new QueryOptions<int, string>
        {
            KeyFactory = _ => QueryKey.From(key),
            Fetcher = (_, _) => Task.FromResult("data"),
        });
        _subscriptions.Add(query.State.Subscribe());
        query.SetArgs(0);
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
        var cut = _context.Render<QueryDevTools>();
        cut.Find("button.dnq-fab").Click();

        cut.Find(".dnq-empty");
    }

    [Test]
    public void CacheWithEntry_ShowsQueryRow()
    {
        AddCacheEntry("test-key");

        var cut = _context.Render<QueryDevTools>();
        cut.Find("button.dnq-fab").Click();

        cut.WaitForAssertion(() => cut.Find(".dnq-query-row"));
    }

    [Test]
    public async Task CacheWithEntry_RowDisplaysQueryKey()
    {
        AddCacheEntry("my-key");

        var cut = _context.Render<QueryDevTools>();
        cut.Find("button.dnq-fab").Click();

        cut.WaitForAssertion(() => cut.Find(".dnq-key-text"));

        await Assert.That(cut.Find(".dnq-key-text").TextContent).Contains("my-key");
    }

    [Test]
    public async Task FilterInput_HidesNonMatchingRows()
    {
        AddCacheEntry("alpha");
        AddCacheEntry("beta");

        var cut = _context.Render<QueryDevTools>();
        cut.Find("button.dnq-fab").Click();

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
    public void ClickQueryRow_ShowsDetailPanel()
    {
        AddCacheEntry("my-key");

        var cut = _context.Render<QueryDevTools>();
        cut.Find("button.dnq-fab").Click();

        cut.WaitForAssertion(() => cut.Find(".dnq-query-row"));
        cut.Find(".dnq-query-row").Click();

        cut.Find(".dnq-detail");
    }

    [Test]
    public async Task ClickSelectedRow_TogglesDetailOff()
    {
        AddCacheEntry("my-key");

        var cut = _context.Render<QueryDevTools>();
        cut.Find("button.dnq-fab").Click();

        cut.WaitForAssertion(() => cut.Find(".dnq-query-row"));
        cut.Find(".dnq-query-row").Click();
        cut.Find(".dnq-detail");

        cut.Find(".dnq-query-row").Click();

        await Assert.That(cut.FindAll(".dnq-detail").Count).IsEqualTo(0);
    }
}
