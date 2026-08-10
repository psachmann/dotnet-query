namespace DotNetQuery.Blazor.Tests;

public class InfiniteSuspenseTests
{
    private readonly BunitContext _context = new();
    private readonly Mock<IInfiniteQuery<int, string, int>> _queryMock = Mock.Of<IInfiniteQuery<int, string, int>>();
    private BehaviorSubject<InfiniteQueryState<string, int>> _stateMock = default!;

    [After(Test)]
    public void Teardown()
    {
        _stateMock.OnCompleted();
        _stateMock.Dispose();
        _context.Dispose();
    }

    private IInfiniteQuery<int, string, int> CreateQuery(InfiniteQueryState<string, int> state)
    {
        _stateMock = new(state);
        _queryMock.CurrentState.Returns(_stateMock.Value);
        _queryMock.State.Returns(_stateMock.AsObservable());

        return _queryMock.Object;
    }

    private IRenderedComponent<InfiniteSuspense<int, string, int>> Render(IInfiniteQuery<int, string, int> query) =>
        _context.Render<InfiniteSuspense<int, string, int>>(p =>
            p.Add(c => c.Query, query)
                .Add(
                    c => c.Content,
                    state => $"<span>{string.Join("|", state.Pages)}:{state.IsFetchingNextPage}</span>"
                )
                .Add(c => c.Loading, "<div>loading</div>")
                .Add(c => c.Failure, ex => $"<div>{ex.Message}</div>")
        );

    [Test]
    public void WhenIdle_RendersLoadingFragment()
    {
        var query = CreateQuery(InfiniteQueryState<string, int>.CreateIdle());

        var cut = Render(query);

        cut.MarkupMatches("<div>loading</div>");
    }

    [Test]
    public void WhenFetching_WithStalePages_RendersLoadingFragment()
    {
        var query = CreateQuery(InfiniteQueryState<string, int>.CreateFetching(["a"], [0], false, false));

        var cut = Render(query);

        cut.MarkupMatches("<div>loading</div>");
    }

    [Test]
    public void WhenSuccess_RendersContentWithPages()
    {
        var query = CreateQuery(InfiniteQueryState<string, int>.CreateSuccess(["a", "b"], [0, 1], true, false));

        var cut = Render(query);

        cut.MarkupMatches("<span>a|b:False</span>");
    }

    [Test]
    public void WhenFetchingNextPage_RendersContentWithFlag()
    {
        var query = CreateQuery(InfiniteQueryState<string, int>.CreateFetchingNext(["a"], [0], true, false));

        var cut = Render(query);

        cut.MarkupMatches("<span>a:True</span>");
    }

    [Test]
    public void WhenFailure_WithStalePages_RendersFailure()
    {
        var query = CreateQuery(
            InfiniteQueryState<string, int>.CreateFailure(new Exception("boom"), ["a"], [0], false, false)
        );

        var cut = Render(query);

        cut.MarkupMatches("<div>boom</div>");
    }

    [Test]
    public void WhenFailure_WithoutFailureTemplate_RendersEmpty()
    {
        var query = CreateQuery(InfiniteQueryState<string, int>.CreateFailure(new Exception(), [], [], false, false));

        var cut = _context.Render<InfiniteSuspense<int, string, int>>(p =>
            p.Add(c => c.Query, query).Add(c => c.Content, state => $"<span>{string.Join("|", state.Pages)}</span>")
        );

        cut.MarkupMatches(string.Empty);
    }

    [Test]
    public void StateChange_ToSuccess_RendersContent()
    {
        var query = CreateQuery(InfiniteQueryState<string, int>.CreateIdle());

        var cut = Render(query);

        _stateMock.OnNext(InfiniteQueryState<string, int>.CreateSuccess(["a"], [0], false, false));

        cut.WaitForAssertion(() => cut.MarkupMatches("<span>a:False</span>"));
    }
}
