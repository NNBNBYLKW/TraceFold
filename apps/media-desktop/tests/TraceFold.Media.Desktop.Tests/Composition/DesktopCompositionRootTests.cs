using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Desktop.Composition;
using TraceFold.Media.Desktop.Dialogs;
using TraceFold.Media.Desktop.Pages;
using TraceFold.Media.Desktop.Services;
using TraceFold.Media.Infrastructure.Workers;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Composition;

public sealed class DesktopCompositionRootTests
{
    [Fact]
    public void Composition_has_one_processor_and_one_processing_worker()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"tracefold-composition-{Guid.NewGuid():N}");
        var paths = new AppDataPaths(
            root,
            Path.Combine(root, "media-desktop.db"),
            Path.Combine(root, "logs"),
            Path.Combine(root, "work"));

        try
        {
            using IHost host = DesktopCompositionRoot.BuildHost(paths);

            IMediaProcessor processor = Assert.Single(
                host.Services.GetServices<IMediaProcessor>());
            Assert.IsType<Mp4M4aMergeProcessor>(processor);
            Assert.Single(
                host.Services.GetServices<IHostedService>(),
                service => service is ProcessingQueueWorker);
            Assert.IsType<WindowsConflictDialogService>(
                host.Services.GetRequiredService<IConflictDialogService>());
            Assert.IsType<WindowsBatchMetadataDialogService>(
                host.Services.GetRequiredService<IBatchMetadataDialogService>());
            Assert.IsType<WindowsLibraryBatchEditDialogService>(
                host.Services.GetRequiredService<ILibraryBatchEditDialogService>());
            Assert.IsType<WindowsBatchIngestConfirmationService>(
                host.Services.GetRequiredService<IBatchIngestConfirmationService>());
            ITaxonomyMaintenanceDialogService taxonomyDialogs = Assert.Single(
                host.Services.GetServices<ITaxonomyMaintenanceDialogService>());
            Assert.IsType<WindowsTaxonomyMaintenanceDialogService>(
                taxonomyDialogs);
            Assert.Same(
                host.Services.GetRequiredService<TaxonomyViewModel>(),
                host.Services.GetRequiredService<TaxonomyViewModel>());
            Assert.IsType<WindowsDiagnosticClipboardPlatform>(
                host.Services.GetRequiredService<IDiagnosticClipboardPlatform>());
            Assert.IsType<WindowsDiagnosticClipboardService>(
                host.Services.GetRequiredService<IDiagnosticClipboardService>());
            Assert.IsType<WindowsRegisteredLogFolderService>(
                host.Services.GetRequiredService<IRegisteredLogFolderService>());
            Assert.Same(
                host.Services.GetRequiredService<SystemDiagnosticsViewModel>(),
                host.Services.GetRequiredService<SystemDiagnosticsViewModel>());
            IServiceProviderIsService serviceProbe =
                host.Services.GetRequiredService<IServiceProviderIsService>();
            Assert.True(serviceProbe.IsService(typeof(TaxonomyPage)));
            Assert.True(serviceProbe.IsService(typeof(SettingsPage)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
