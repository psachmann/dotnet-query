using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DotNetQuery.Samples.Avalonia.ViewModels;
using DotNetQuery.Samples.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetQuery.Samples.Avalonia;

public partial class App : Application
{
    /// <summary>
    /// Set by <see cref="Program.Main"/> before Avalonia starts.
    /// </summary>
    public static IServiceProvider? Services { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services =
                Services ?? throw new InvalidOperationException($"{nameof(Services)} has not been initialized.");

            // The samples share EF Core services registered as scoped, so the app keeps a single
            // scope alive for its whole lifetime and resolves the shell view model from it.
            var scope = services.CreateScope();
            desktop.ShutdownRequested += (_, _) => scope.Dispose();
            desktop.MainWindow = new MainWindow
            {
                DataContext = scope.ServiceProvider.GetRequiredService<MainViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
