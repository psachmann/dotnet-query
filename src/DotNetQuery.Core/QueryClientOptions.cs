namespace DotNetQuery.Core;

public sealed record QueryClientOptions
{
    public TimeSpan StaleTime { get; set; } = TimeSpan.Zero;

    public TimeSpan CacheTime { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan? RefetchInterval { get; set; }

    public IRetryHandler RetryHandler { get; set; } = new DefaultRetryHandler();

    public QueryRenderMode RenderMode { get; set; } = QueryRenderMode.Csr;
}
