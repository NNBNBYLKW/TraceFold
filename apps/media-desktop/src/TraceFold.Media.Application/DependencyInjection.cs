using Microsoft.Extensions.DependencyInjection;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Startup;

namespace TraceFold.Media.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddMediaApplication(
        this IServiceCollection services)
    {
        services.AddSingleton<DirectorySettingsService>();
        services.AddSingleton<IStartupCoordinator, StartupCoordinator>();
        services.AddSingleton<CandidateGroupingService>();
        services.AddSingleton<IInboxGroupingRule, Mp4M4aGroupingRule>();
        services.AddSingleton<IInboxGroupingRule, DirectMediaGroupingRule>();
        services.AddSingleton<ScanInboxService>();
        services.AddSingleton<IInboxScanner>(provider =>
            provider.GetRequiredService<ScanInboxService>());
        services.AddSingleton<ScanPreferencesService>();
        services.AddSingleton<InboxQueryService>();
        services.AddSingleton<IInboxQueryService>(provider =>
            provider.GetRequiredService<InboxQueryService>());
        services.AddSingleton<IScanPreferencesService>(provider =>
            provider.GetRequiredService<ScanPreferencesService>());
        services.AddSingleton<MetadataEditorService>();
        services.AddSingleton<IMetadataEditorService>(provider =>
            provider.GetRequiredService<MetadataEditorService>());
        services.AddSingleton<TaxonomyService>();
        services.AddSingleton<ITaxonomyService>(provider =>
            provider.GetRequiredService<TaxonomyService>());
        services.AddSingleton<LibraryQueryService>();
        services.AddSingleton<ILibraryQueryService>(provider =>
            provider.GetRequiredService<LibraryQueryService>());
        services.AddSingleton<MediaPreviewResolver>();
        services.AddSingleton<IMediaPreviewResolver>(provider =>
            provider.GetRequiredService<MediaPreviewResolver>());
        services.AddSingleton<IMediaProcessor, Mp4M4aMergeProcessor>();
        services.AddSingleton<IMediaProcessorRegistry, MediaProcessorRegistry>();
        services.AddSingleton<IProcessingPlanner, ProcessingPlanner>();
        services.AddSingleton<IProcessingQueueSignal, ProcessingQueueSignal>();
        services.AddSingleton<IProcessingExecutionController,
            ProcessingExecutionController>();
        services.AddSingleton<ProcessingProgressBuffer>();
        services.AddSingleton<IProcessingCommandService, ProcessingCommandService>();
        services.AddSingleton<IProcessingInvalidationService,
            ProcessingInvalidationService>();
        services.AddSingleton<IProcessingJobExecutor, ProcessingJobExecutor>();
        services.AddSingleton<IProcessingRecoveryService, ProcessingRecoveryService>();
        services.AddSingleton<IProcessingDiagnosticsService,
            ProcessingDiagnosticsService>();
        services.AddSingleton<IIngestInputResolver, IngestInputResolver>();
        services.AddSingleton<IIngestCleanupService, IngestCleanupService>();
        services.AddSingleton<IConflictDetectionService,
            ConflictDetectionService>();
        services.AddSingleton<IConflictQueryService, ConflictQueryService>();
        services.AddSingleton<IngestService>();
        services.AddSingleton<IIngestService>(provider =>
            provider.GetRequiredService<IngestService>());
        services.AddSingleton<IngestRecoveryService>();
        services.AddSingleton<IIngestRecoveryService>(provider =>
            provider.GetRequiredService<IngestRecoveryService>());
        services.AddSingleton<IMediaOperationCoordinator, MediaOperationCoordinator>();
        services.AddSingleton<IMediaRuntimeGate, MediaRuntimeGate>();
        services.AddSingleton<IInboxActivityNotifier, InboxActivityNotifier>();
        return services;
    }
}
