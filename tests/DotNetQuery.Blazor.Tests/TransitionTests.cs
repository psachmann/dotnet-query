namespace DotNetQuery.Blazor.Tests;

public class TransitionTests
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

        var cut = _context.Render<Transition<int, string>>(p =>
            p.Add(c => c.Query, query)
                .Add(c => c.Content, data => $"<span>{data}</span>")
                .Add(c => c.Loading, "<div>loading</div>")
        );

        cut.MarkupMatches("<div>loading</div>");
    }

    [Test]
    public void WhenFetching_WithNoLastData_RendersLoadingFragment()
    {
        var query = CreateQuery(QueryState<string>.CreateFetching());

        var cut = _context.Render<Transition<int, string>>(p =>
            p.Add(c => c.Query, query)
                .Add(c => c.Content, data => $"<span>{data}</span>")
                .Add(c => c.Loading, "<div>loading</div>")
        );

        cut.MarkupMatches("<div>loading</div>");
    }

    [Test]
    public void WhenFetching_WithLastData_RendersContentWithLastData()
    {
        var query = CreateQuery(QueryState<string>.CreateFetching(lastData: "previous"));

        var cut = _context.Render<Transition<int, string>>(p =>
            p.Add(c => c.Query, query)
                .Add(c => c.Content, data => $"<span>{data}</span>")
                .Add(c => c.Loading, "<div>loading</div>")
        );

        cut.MarkupMatches("<span>previous</span>");
    }

    [Test]
    public void WhenSuccess_RendersContentWithCurrentData()
    {
        var query = CreateQuery(QueryState<string>.CreateSuccess("hello"));

        var cut = _context.Render<Transition<int, string>>(p =>
            p.Add(c => c.Query, query)
                .Add(c => c.Content, data => $"<span>{data}</span>")
                .Add(c => c.Loading, "<div>loading</div>")
        );

        cut.MarkupMatches("<span>hello</span>");
    }

    [Test]
    public void WhenFailure_WithNoLastData_RendersErrorTemplate()
    {
        var error = new Exception("boom");
        var query = CreateQuery(QueryState<string>.CreateFailure(error));

        var cut = _context.Render<Transition<int, string>>(p =>
            p.Add(c => c.Query, query)
                .Add(c => c.Content, data => $"<span>{data}</span>")
                .Add(c => c.Failure, ex => $"<div>{ex.Message}</div>")
        );

        cut.MarkupMatches("<div>boom</div>");
    }

    [Test]
    public void WhenFailure_WithLastData_RendersContentWithLastData()
    {
        // Transition keeps showing the last successful data even on failure,
        // unlike Suspense which would show the failure template.
        var error = new Exception("boom");
        var query = CreateQuery(QueryState<string>.CreateFailure(error, lastData: "stale"));

        var cut = _context.Render<Transition<int, string>>(p =>
            p.Add(c => c.Query, query)
                .Add(c => c.Content, data => $"<span>{data}</span>")
                .Add(c => c.Failure, ex => $"<div>{ex.Message}</div>")
        );

        cut.MarkupMatches("<span>stale</span>");
    }

    [Test]
    public void StateChange_FromSuccessToFetching_KeepsContentVisible()
    {
        // Transition keeps showing current content during a background refetch,
        // unlike Suspense which falls back to the loading template.
        var query = CreateQuery(QueryState<string>.CreateSuccess("current"));

        var cut = _context.Render<Transition<int, string>>(p =>
            p.Add(c => c.Query, query)
                .Add(c => c.Content, data => $"<span>{data}</span>")
                .Add(c => c.Loading, "<div>loading</div>")
        );

        _stateMock.OnNext(QueryState<string>.CreateFetching(lastData: "current"));

        cut.WaitForAssertion(() => cut.MarkupMatches("<span>current</span>"));
    }

    [Test]
    public void StateChange_ToSuccess_RendersUpdatedContent()
    {
        var query = CreateQuery(QueryState<string>.CreateFetching());

        var cut = _context.Render<Transition<int, string>>(p =>
            p.Add(c => c.Query, query)
                .Add(c => c.Content, data => $"<span>{data}</span>")
                .Add(c => c.Loading, "<div>loading</div>")
        );

        _stateMock.OnNext(QueryState<string>.CreateSuccess("done"));

        cut.WaitForAssertion(() => cut.MarkupMatches("<span>done</span>"));
    }

    [Test]
    public async Task OnParametersSet_SameQueryInstance_DoesNotResubscribe()
    {
        // Regression: previously, every OnParametersSet unconditionally disposed and recreated the
        // subscription even when the Query reference had not changed, causing subscription churn.
        var subscribeCount = 0;
        // Assign to _stateMock so the [After(Test)] teardown can call OnCompleted/Dispose safely.
        _stateMock = new BehaviorSubject<QueryState<string>>(QueryState<string>.CreateIdle());

        var observableWithCounter = Observable.Create<QueryState<string>>(observer =>
        {
            subscribeCount++;
            return _stateMock.Subscribe(observer);
        });

        _queryMock.State.Returns(observableWithCounter);
        var query = _queryMock.Object;

        Microsoft.AspNetCore.Components.RenderFragment<string> content =
            value => builder => builder.AddContent(0, $"<span>{value}</span>");

        var cut = _context.Render<Transition<int, string>>(p =>
            p.Add(c => c.Query, query).Add(c => c.Content, content)
        );

        var countAfterFirstRender = subscribeCount;

        // Simulate a parent re-render passing the same Query reference — OnParametersSet fires again
        await cut.InvokeAsync(() =>
            cut.Instance.SetParametersAsync(
                Microsoft.AspNetCore.Components.ParameterView.FromDictionary(
                    new Dictionary<string, object?>
                    {
                        [nameof(Transition<int, string>.Query)] = (object)query,
                        [nameof(Transition<int, string>.Content)] = (object)content,
                    }
                )
            )
        );

        await Assert.That(subscribeCount).IsEqualTo(countAfterFirstRender);
    }
}

