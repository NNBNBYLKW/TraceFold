using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Serilog;
using TraceFold.Media.Application;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Desktop.Dialogs;
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
                services.AddMediaApplication();
                services.AddMediaInfrastructure(paths.RootDirectory);
                services.AddSingleton<WindowContext>();
                services.AddSingleton<IFolderPicker, WindowsFolderPicker>();
                services.AddSingleton<IDebounceScheduler, ProductionDebounceScheduler>();
                services.AddSingleton<IMetadataEditorRegistry, MetadataEditorRegistry>();
                services.AddSingleton<IConflictDialogService, WindowsConflictDialogService>();
                services.AddSingleton<IBatchMetadataDialogService,
                    WindowsBatchMetadataDialogService>();
                services.AddSingleton<ILibraryBatchEditDialogService,
                    WindowsLibraryBatchEditDialogService>();
                services.AddSingleton<IBatchIngestConfirmationService,
                    WindowsBatchIngestConfirmationService>();
                services.AddSingleton<IUiDispatcher>(_ => new DispatcherQueueAdapter(
                    DispatcherQueue.GetForCurrentThread()
                    ?? throw new InvalidOperationException(
                        "The desktop UI dispatcher is unavailable.")));
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddTransient<MetadataEditorViewModel>();
                services.AddSingleton<InboxInspectorViewModel>();
                services.AddSingleton<InboxSelectionViewModel>();
                services.AddSingleton<InboxBatchSummaryViewModel>();
                services.AddSingleton<InboxViewModel>();
                services.AddSingleton<LibraryInspectorViewModel>();
                services.AddSingleton<LibrarySelectionViewModel>();
                services.AddSingleton<LibraryFilterViewModel>();
                services.AddSingleton<LibraryViewModel>();
                services.AddSingleton<TaxonomyViewModel>();
                services.AddSingleton<IProcessStarter, WindowsProcessStarter>();
                services.AddSingleton<IAssetRevealService, WindowsAssetRevealService>();
                services.AddSingleton<IRecoveryRevealService,
                    WindowsRecoveryRevealService>();
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
