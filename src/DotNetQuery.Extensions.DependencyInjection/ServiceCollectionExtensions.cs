namespace DotNetQuery.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering DotNetQuery services with an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IQueryClient"/> with the DI container.
    /// The service lifetime is determined by <see cref="QueryClientOptions.ExecutionMode"/>:
    /// <see cref="QueryExecutionMode.Csr"/> registers as a singleton, <see cref="QueryExecutionMode.Ssr"/> as scoped.
    /// </summary>
    /// <param name="services">The service collection to add the client to.</param>
    /// <param name="configure">An optional delegate to configure <see cref="QueryClientOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
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
                serviceProvider =>
                    QueryClientFactory.Create(
                        options,
                        serviceProvider.GetService<IScheduler>(),
                        serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(QueryTelemetry.SourceName)
                    ),
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
