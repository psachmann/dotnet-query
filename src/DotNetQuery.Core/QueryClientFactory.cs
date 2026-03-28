namespace DotNetQuery.Core;

public static class QueryClientFactory
{
    public static IQueryClient Create(QueryClientOptions options, IScheduler? scheduler = null) =>
        new QueryClient(options, scheduler);
}
