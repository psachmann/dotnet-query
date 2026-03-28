namespace DotNetQuery.Blazor.Tests;

public class SuspenseTests
{
    private readonly BunitContext _context = new();
    private readonly Mock<IQuery<int, string>> _queryMock = Mock.Of<IQuery<int, string>>();
    private BehaviorSubject<QueryState<string>> _stateMock = default!;

    [After(Test)]
    public void Teardown()
    {
        _stateMock.OnCompleted();
        _stateMock.Dispose();
        _context.Dispose();
    }

    private IQuery<int, string> CreateQuery(QueryState<string> state)
    {
        _stateMock = new(state);
        _queryMock.CurrentState.Returns(_stateMock.Value);
        _queryMock.State.Returns(_stateMock.AsObservable());

        return _queryMock.Object;
    }

    [Test]
    public void WhenIdle_RendersLoadingFragment()
    {
        var query = CreateQuery(QueryState<string>.CreateIdle());

        var cut = _context.Render<Suspense<int, string>>(p =>
            p.Add(c => c.Query, query)
                .Add(c => c.Content, data => $"<span>{data}</span>")
                .Add(c => c.Loading, "<div>loading</div>")
        );

        cut.MarkupMatches("<div>loading</div>");
    }

    [Test]
    public void WhenFetching_RendersLoadingFragment()
    {
        var query = CreateQuery(QueryState<string>.CreateFetching());

        var cut = _context.Render<Suspense<int, string>>(p =>
            p.Add(c => c.Query, query)
                .Add(c => c.Content, data => $"<span>{data}</span>")
                .Add(c => c.Loading, "<div>loading</div>")
        );

        cut.MarkupMatches("<div>loading</div>");
    }

    [Test]
    public void WhenSuccess_RendersContentWithData()
    {
        var query = CreateQuery(QueryState<string>.CreateSuccess("hello"));

        var cut = _context.Render<Suspense<int, string>>(p =>
            p.Add(c => c.Query, query)
                .Add(c => c.Content, data => $"<span>{data}</span>")
                .Add(c => c.Loading, "<div>loading</div>")
        );

        cut.MarkupMatches("<span>hello</span>");
    }

    [Test]
    public void WhenFailure_WithFailureTemplate_RendersError()
    {
        var error = new Exception("boom");
        var query = CreateQuery(QueryState<string>.CreateFailure(error));

        var cut = _context.Render<Suspense<int, string>>(p =>
            p.Add(c => c.Query, query)
                .Add(c => c.Content, data => $"<span>{data}</span>")
                .Add(c => c.Failure, ex => $"<div>{ex.Message}</div>")
        );

        cut.MarkupMatches("<div>boom</div>");
    }

    [Test]
    public void WhenFailure_WithoutFailureTemplate_RendersEmpty()
    {
        var query = CreateQuery(QueryState<string>.CreateFailure(new Exception()));

        var cut = _context.Render<Suspense<int, string>>(p =>
            p.Add(c => c.Query, query).Add(c => c.Content, data => $"<span>{data}</span>")
        );

        cut.MarkupMatches(string.Empty);
    }

    [Test]
    public void StateChange_ToSuccess_RendersContent()
    {
        var query = CreateQuery(QueryState<string>.CreateFetching());

        var cut = _context.Render<Suspense<int, string>>(p =>
            p.Add(c => c.Query, query)
                .Add(c => c.Content, data => $"<span>{data}</span>")
                .Add(c => c.Loading, "<div>loading</div>")
        );

        _stateMock.OnNext(QueryState<string>.CreateSuccess("done"));

        cut.WaitForAssertion(() => cut.MarkupMatches("<span>done</span>"));
    }

    [Test]
    public void StateChange_FromSuccessToFetching_ShowsLoading()
    {
        var query = CreateQuery(QueryState<string>.CreateSuccess("old"));

        var cut = _context.Render<Suspense<int, string>>(p =>
            p.Add(c => c.Query, query)
                .Add(c => c.Content, data => $"<span>{data}</span>")
                .Add(c => c.Loading, "<div>loading</div>")
        );

        _stateMock.OnNext(QueryState<string>.CreateFetching(lastData: "old"));

        cut.WaitForAssertion(() => cut.MarkupMatches("<div>loading</div>"));
    }
}
