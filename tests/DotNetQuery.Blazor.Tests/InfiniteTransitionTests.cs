namespace DotNetQuery.Blazor.Tests;

public class InfiniteTransitionTests
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

    private IRenderedComponent<InfiniteTransition<int, string, int>> Render(IInfiniteQuery<int, string, int> query) =>
        _context.Render<InfiniteTransition<int, string, int>>(p =>
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
    public void WhenFetching_WithoutPages_RendersLoadingFragment()
    {
        var query = CreateQuery(InfiniteQueryState<string, int>.CreateFetching([], [], false, false));

        var cut = Render(query);

        cut.MarkupMatches("<div>loading</div>");
    }

    [Test]
    public void WhenFetching_WithStalePages_KeepsRenderingContent()
    {
        var query = CreateQuery(InfiniteQueryState<string, int>.CreateFetching(["a"], [0], false, false));

        var cut = Render(query);

        cut.MarkupMatches("<span>a:False</span>");
    }

    [Test]
    public void WhenFetchingNextPage_RendersContentWithFlag()
    {
        var query = CreateQuery(InfiniteQueryState<string, int>.CreateFetchingNext(["a"], [0], true, false));

        var cut = Render(query);

        cut.MarkupMatches("<span>a:True</span>");
    }

    [Test]
    public void WhenFailure_WithStalePages_KeepsRenderingContent()
    {
        var query = CreateQuery(
            InfiniteQueryState<string, int>.CreateFailure(new Exception("boom"), ["a"], [0], false, false)
        );

        var cut = Render(query);

        cut.MarkupMatches("<span>a:False</span>");
    }

    [Test]
    public void WhenFailure_WithoutPages_RendersFailure()
    {
        var query = CreateQuery(
            InfiniteQueryState<string, int>.CreateFailure(new Exception("boom"), [], [], false, false)
        );

        var cut = Render(query);

        cut.MarkupMatches("<div>boom</div>");
    }

    [Test]
    public void StateChange_ToSuccess_RendersContent()
    {
        var query = CreateQuery(InfiniteQueryState<string, int>.CreateIdle());

        var cut = Render(query);

        _stateMock.OnNext(InfiniteQueryState<string, int>.CreateSuccess(["a", "b"], [0, 1], false, false));

        cut.WaitForAssertion(() => cut.MarkupMatches("<span>a|b:False</span>"));
    }

    [Test]
    public async Task OnParametersSet_SameQueryInstance_DoesNotResubscribe()
    {
        // Regression: previously, every OnParametersSet unconditionally disposed and recreated the
        // subscription even when the Query reference had not changed, causing subscription churn.
        var subscribeCount = 0;
        // Assign to _stateMock so the [After(Test)] teardown can call OnCompleted/Dispose safely.
        _ = CreateQuery(InfiniteQueryState<string, int>.CreateIdle());

        var observableWithCounter = Observable.Create<InfiniteQueryState<string, int>>(observer =>
        {
            subscribeCount++;
            return _stateMock.Subscribe(observer);
        });

        _queryMock.State.Returns(observableWithCounter);
        var query = _queryMock.Object;

        RenderFragment content(InfiniteQueryState<string, int> state) =>
            builder => builder.AddContent(0, $"<span>{string.Join("|", state.Pages)}</span>");

        var cut = _context.Render<InfiniteTransition<int, string, int>>(p =>
            p.Add(c => c.Query, query).Add(c => c.Content, content)
        );

        var countAfterFirstRender = subscribeCount;

        // Simulate a parent re-render passing the same Query reference — OnParametersSet fires again
        await cut.InvokeAsync(() =>
            cut.Instance.SetParametersAsync(
                ParameterView.FromDictionary(
                    new Dictionary<string, object?>
                    {
                        [nameof(InfiniteTransition<int, string, int>.Query)] = (object)query,
                        [nameof(InfiniteTransition<int, string, int>.Content)] = (object)
                            (RenderFragment<InfiniteQueryState<string, int>>)content,
                    }
                )
            )
        );

        await Assert.That(subscribeCount).IsEqualTo(countAfterFirstRender);
    }
}
