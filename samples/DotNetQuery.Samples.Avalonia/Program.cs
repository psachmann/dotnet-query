using System;
using Avalonia;
using DotNetQuery.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetQuery.Samples.Avalonia;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // The service provider is built (and the in-memory database seeded) before Avalonia starts,
        // so the shell view model can be resolved synchronously once the framework is up.
        App.Services = BuildServiceProvider();
        TodosContextSeed.SeedAsync(App.Services).GetAwaiter().GetResult();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect()
#if DEBUG
        .WithDeveloperTools()
#endif
        .WithInterFont().LogToTrace();

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddDotNetQuery(options =>
        {
            // Csr keeps the client a singleton, which is what a desktop app wants: one cache
            // for the whole process lifetime.
            options.ExecutionMode = QueryExecutionMode.Csr;
            options.StaleTime = TimeSpan.Zero; // data is immediately stale
            options.CacheTime = TimeSpan.FromMinutes(10); // cache entries live 10 minutes after last subscriber
        });
        services.AddDotNetQuerySamplesShared();

        services.AddScoped<ViewModels.MainViewModel>();
        services.AddScoped<ViewModels.TodoDetailsViewModel>();

        return services.BuildServiceProvider();
    }
}
