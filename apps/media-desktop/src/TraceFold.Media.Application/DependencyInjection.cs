using Microsoft.Extensions.DependencyInjection;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
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
        services.AddSingleton<MediaPreviewResolver>();
        services.AddSingleton<IMediaPreviewResolver>(provider =>
            provider.GetRequiredService<MediaPreviewResolver>());
        services.AddSingleton<OrdinaryIngestService>();
        services.AddSingleton<IOrdinaryIngestService>(provider =>
            provider.GetRequiredService<OrdinaryIngestService>());
        services.AddSingleton<IngestRecoveryService>();
        services.AddSingleton<IIngestRecoveryService>(provider =>
            provider.GetRequiredService<IngestRecoveryService>());
        services.AddSingleton<IMediaOperationCoordinator, MediaOperationCoordinator>();
        services.AddSingleton<IMediaRuntimeGate, MediaRuntimeGate>();
        services.AddSingleton<IInboxActivityNotifier, InboxActivityNotifier>();
        return services;
    }
}
