using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Startup;
using TraceFold.Media.Desktop.Pages;
using TraceFold.Media.Desktop.Services;
using TraceFold.Media.Desktop.Shell;
using TraceFold.Media.Infrastructure;
using TraceFold.Media.Infrastructure.Logging;

namespace TraceFold.Media.Desktop.Composition;

public static class DesktopCompositionRoot
{
    public static IHost BuildHost(AppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Log.Logger = LoggingBootstrapper.CreateLogger(paths);

        return Host.CreateDefaultBuilder()
            .UseSerilog(Log.Logger, dispose: true)
            .ConfigureServices(services =>
            {
                services.AddSingleton(paths);
                services.AddMediaInfrastructure(paths.RootDirectory);
                services.AddSingleton<DirectorySettingsService>();
                services.AddSingleton<IStartupCoordinator, StartupCoordinator>();
                services.AddSingleton<WindowContext>();
                services.AddSingleton<IFolderPicker, WindowsFolderPicker>();
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<InboxPage>();
                services.AddSingleton<LibraryPage>();
                services.AddSingleton<TaxonomyPage>();
                services.AddSingleton<SettingsPage>();
                services.AddSingleton<ShellPage>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }
}
