using Microsoft.Extensions.DependencyInjection;

namespace Fetchling.Samples.Shared;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFetchlingSamplesShared(this IServiceCollection services)
    {
        services.AddDbContext<TodosContext>();
        services.AddScoped<ITodosClient, TodosClientImpl>();
        services.AddScoped<TodosMutations>();
        services.AddScoped<TodosQueries>();

        return services;
    }
}
