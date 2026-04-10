namespace DotNetQuery.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDotNetQuery(
        this IServiceCollection services,
        Action<QueryClientOptions>? configure = default
    )
    {
        var options = new QueryClientOptions();

        if (configure is not null)
        {
            configure(options);
        }

        options.Validate();

        services.Add(
            new ServiceDescriptor(
                typeof(IQueryClient),
                serviceProvider => QueryClientFactory.Create(options, serviceProvider.GetService<IScheduler>()),
                options.ExecutionMode.ToServiceLifetime()
            )
        );

        return services;
    }

    private static ServiceLifetime ToServiceLifetime(this QueryExecutionMode executionMode) =>
        executionMode switch
        {
            QueryExecutionMode.Csr => ServiceLifetime.Singleton,
            QueryExecutionMode.Ssr => ServiceLifetime.Scoped,
            _ => throw new ArgumentOutOfRangeException(nameof(executionMode), executionMode, null),
        };
}
