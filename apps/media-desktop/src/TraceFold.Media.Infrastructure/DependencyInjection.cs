using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Diagnostics;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Application.Startup;
using TraceFold.Media.Infrastructure.Filesystem;
using TraceFold.Media.Infrastructure.Diagnostics;
using TraceFold.Media.Infrastructure.MediaTools;
using TraceFold.Media.Infrastructure.Paths;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Settings;
using TraceFold.Media.Infrastructure.Workers;

namespace TraceFold.Media.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMediaInfrastructure(
        this IServiceCollection services,
        string? dataRootOverride = null,
        string? mediaToolRootOverride = null)
    {
        var pathProvider = new LocalAppDataPathProvider(dataRootOverride);
        AppDataPaths paths = pathProvider.GetPaths();

        services.AddSingleton<IAppDataPathProvider>(pathProvider);
        services.AddDbContextFactory<MediaDesktopDbContext>(options =>
            options.UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = paths.DatabasePath,
                Pooling = false
            }.ToString()));
        services.AddSingleton<IDirectorySettingsStore, EfDirectorySettingsStore>();
        services.AddSingleton<IDirectoryProbe, LocalDirectoryProbe>();
        services.AddSingleton<IInboxFileCatalog, LocalInboxFileCatalog>();
        services.AddSingleton<IInboxScanStore, EfInboxScanStore>();
        services.AddSingleton<IInboxQueryStore, EfInboxQueryStore>();
        services.AddSingleton<IInboxSelectionStore, EfInboxSelectionStore>();
        services.AddSingleton<EfIngestStore>();
        services.AddSingleton<IIngestStore>(provider =>
            provider.GetRequiredService<EfIngestStore>());
        services.AddSingleton<IIngestCleanupWorkStore>(provider =>
            provider.GetRequiredService<EfIngestStore>());
        services.AddSingleton<IRecoveryLocationResolver,
            EfRecoveryLocationResolver>();
        services.AddSingleton<IConflictCaseStore, EfConflictCaseStore>();
        services.AddSingleton<IProcessingStore, EfProcessingStore>();
        services.AddSingleton<EfTaxonomyStore>();
        services.AddSingleton<ITaxonomyStore>(provider =>
            provider.GetRequiredService<EfTaxonomyStore>());
        services.AddSingleton<ITaxonomyLookup>(provider =>
            provider.GetRequiredService<EfTaxonomyStore>());
        services.AddSingleton<ITaxonomyManagementQueryStore,
            EfTaxonomyManagementQueryStore>();
        services.AddSingleton<EfTaxonomyMaintenanceStore>();
        services.AddSingleton<ITaxonomyMaintenanceStore>(provider =>
            provider.GetRequiredService<EfTaxonomyMaintenanceStore>());
        services.AddSingleton<IMetadataStore, EfMetadataStore>();
        services.AddSingleton<IBatchMetadataStore, EfBatchMetadataStore>();
        services.AddSingleton<ILibraryQueryStore, EfLibraryQueryStore>();
        services.AddSingleton<ILibrarySortPreferenceStore,
            EfLibrarySortPreferenceStore>();
        services.AddSingleton<INamingPresetStore, EfNamingPresetStore>();
        services.AddSingleton<ILibraryBatchEditStore,
            EfLibraryBatchEditStore>();
        services.AddSingleton<IPreviewPathStore, EfPreviewPathStore>();
        services.AddSingleton<IFileHasher, Sha256FileHasher>();
        services.AddSingleton<IControlledMediaStorage, ControlledMediaStorage>();
        services.AddSingleton<IProcessingWorkspace, ControlledProcessingWorkspace>();
        services.AddSingleton<IProcessingFileVerifier, ProcessingFileVerifier>();
        services.AddSingleton(new MediaToolOptions(
            mediaToolRootOverride
            ?? Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg")));
        services.AddSingleton<BundledMediaToolLocator>();
        services.AddSingleton<IMediaTool, FfmpegMediaTool>();
        services.AddSingleton<IVolumeSpaceReader, WindowsVolumeSpaceReader>();
        services.AddSingleton<IDatabaseDiagnosticReader,
            EfDatabaseDiagnosticReader>();
        services.AddSingleton<IControlledProcessRunner, ControlledProcessRunner>();
        services.AddSingleton<IExecutableVersionReader,
            ControlledExecutableVersionReader>();
        services.AddSingleton<IBackgroundWorkSummaryStore,
            EfBackgroundWorkSummaryStore>();
        services.AddSingleton(RuntimeDiagnosticOptions.FromAssembly(
            AppContext.BaseDirectory,
            Assembly.GetEntryAssembly() ?? typeof(DependencyInjection).Assembly));
        services.AddSingleton<ISystemDiagnosticProbe,
            FolderAndDiskDiagnosticProbe>();
        services.AddSingleton<ISystemDiagnosticProbe, DatabaseDiagnosticProbe>();
        services.AddSingleton<ISystemDiagnosticProbe,
            ProcessingToolDiagnosticProbe>();
        services.AddSingleton<ISystemDiagnosticProbe,
            BackgroundWorkDiagnosticProbe>();
        services.AddSingleton<ISystemDiagnosticProbe,
            RuntimeAndLogDiagnosticProbe>();
        services.AddSingleton<IScanPreferencesStore, EfScanPreferencesStore>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IApplicationLifetimeToken, HostApplicationLifetimeToken>();
        services.AddHostedService<CandidateStabilityWorker>();
        services.AddHostedService<AutomaticInboxScanWorker>();
        services.AddHostedService<ProcessingQueueWorker>();
        services.AddHostedService<IngestCleanupWorker>();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        return services;
    }
}
