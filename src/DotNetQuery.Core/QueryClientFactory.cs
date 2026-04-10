namespace DotNetQuery.Core;

public static class QueryClientFactory
{
    public static IQueryClient Create(QueryClientOptions options, IScheduler? scheduler = null)
    {
        options.Validate();

        return new QueryClient(options, scheduler);
    }
}
