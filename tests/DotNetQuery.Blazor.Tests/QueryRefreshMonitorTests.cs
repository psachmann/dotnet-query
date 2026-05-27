namespace DotNetQuery.Blazor.Tests;

using Microsoft.Extensions.DependencyInjection;

public class QueryRefreshMonitorTests
{
    private readonly BunitContext _context = new();

    [After(Test)]
    public void Teardown() => _context.Dispose();

    private Mock<IQueryClient> SetupQueryClient()
    {
        var client = Mock.Of<IQueryClient>();

        _context.Services.AddSingleton(client.Object);

        return client;
    }

    private BunitJSModuleInterop SetupJsModule()
    {
        var module = _context.JSInterop.SetupModule("./_content/DotNetQuery.Blazor/QueryRefreshMonitor.js");

        module.SetupVoid("register", _ => true);

        return module;
    }

    [Test]
    public void OnFirstRender_ImportsModuleAndCallsRegister()
    {
        SetupQueryClient();
        var jsModule = SetupJsModule();

        _context.Render<QueryRefreshMonitor>();

        jsModule.VerifyInvoke("register", calledTimes: 1);
    }

    [Test]
    public async Task OnFirstRender_DefaultParameters_RegistersBothEnabled()
    {
        SetupQueryClient();
        var jsModule = SetupJsModule();

        _context.Render<QueryRefreshMonitor>();

        var args = jsModule.Invocations["register"][0].Arguments;
        using var _ = Assert.Multiple();
        await Assert.That((bool)args[1]!).IsTrue(); // onFocus
        await Assert.That((bool)args[2]!).IsTrue(); // onReconnect
    }

    [Test]
    public async Task OnFirstRender_RefetchOnFocusFalse_PassesFalseToJs()
    {
        SetupQueryClient();
        var jsModule = SetupJsModule();

        _context.Render<QueryRefreshMonitor>(p => p.Add(c => c.RefetchOnFocus, false));

        var args = jsModule.Invocations["register"][0].Arguments;
        using var _ = Assert.Multiple();
        await Assert.That((bool)args[1]!).IsFalse();
        await Assert.That((bool)args[2]!).IsTrue();
    }

    [Test]
    public async Task OnFirstRender_RefetchOnReconnectFalse_PassesFalseToJs()
    {
        SetupQueryClient();
        var jsModule = SetupJsModule();

        _context.Render<QueryRefreshMonitor>(p => p.Add(c => c.RefetchOnReconnect, false));

        var args = jsModule.Invocations["register"][0].Arguments;
        using var _ = Assert.Multiple();
        await Assert.That((bool)args[1]!).IsTrue();
        await Assert.That((bool)args[2]!).IsFalse();
    }

    [Test]
    public void OnFocus_InvalidatesAllQueries()
    {
        var client = SetupQueryClient();
        SetupJsModule();

        var cut = _context.Render<QueryRefreshMonitor>();
        cut.Instance.OnFocus();

        client.Invalidate(Any<Func<QueryKey, bool>>()).WasCalled();
    }

    [Test]
    public void OnReconnect_InvalidatesAllQueries()
    {
        var client = SetupQueryClient();
        SetupJsModule();

        var cut = _context.Render<QueryRefreshMonitor>();
        cut.Instance.OnReconnect();

        client.Invalidate(Any<Func<QueryKey, bool>>()).WasCalled();
    }

    [Test]
    public async Task InvalidatePredicate_ReturnsTrueForAnyKey()
    {
        var client = SetupQueryClient();
        SetupJsModule();

        var cut = _context.Render<QueryRefreshMonitor>();
        cut.Instance.OnFocus();

        var invocation = Mock.Invocations(client)
            .Single(c => c.MemberName == nameof(IQueryClient.Invalidate) && c.Arguments[0] is Func<QueryKey, bool>);
        var predicate = (Func<QueryKey, bool>)invocation.Arguments[0]!;

        await Assert.That(predicate!(QueryKey.From("any-key"))).IsTrue();
    }

    [Test]
    public void OnSubsequentRender_DoesNotCallRegisterAgain()
    {
        SetupQueryClient();
        var jsModule = SetupJsModule();

        var cut = _context.Render<QueryRefreshMonitor>();
        cut.Render();

        jsModule.VerifyInvoke("register", calledTimes: 1);
    }
}
