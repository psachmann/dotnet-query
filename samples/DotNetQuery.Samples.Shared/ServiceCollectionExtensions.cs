using Microsoft.Extensions.DependencyInjection;

namespace DotNetQuery.Samples.Shared;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDotNetQuerySamplesShared(this IServiceCollection services)
    {
        services.AddDbContext<TodosContext>();
        services.AddScoped<ITodosClient, TodosClientImpl>();
        services.AddScoped<TodosMutations>();
        services.AddScoped<TodosQueries>();

        return services;
    }
}
