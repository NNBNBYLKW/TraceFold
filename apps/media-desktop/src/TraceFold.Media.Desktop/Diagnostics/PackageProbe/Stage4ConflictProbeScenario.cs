using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Infrastructure.MediaTools;

namespace TraceFold.Media.Desktop.Diagnostics;

public sealed class Stage4ConflictProbeScenario : IPackageProbeScenario
{
    private const string PhotoFixture = "photo.jpg";
    private const string VideoFixture = "paired.mp4";
    private const string AudioFixture = "paired.m4a";

    public PackageProbeScenarioKind Kind => PackageProbeScenarioKind.Stage4Conflict;

    public async Task<int> RunAsync(
        PackageProbeRequest request,
        PackageProbeEnvironment environment,
        CancellationToken cancellationToken)
    {
        environment.CreateRuntimeDirectories();
        environment.ValidateOwnedProbeTree([.. environment.RuntimeDirectoryPaths]);

        var clock = new ProbeTimeProvider(DateTimeOffset.Parse("2026-08-24T12:00:00Z"));
        IHost? host = null;
        string? toolPath = null;
        bool registeredReplace = false;
        bool contentKeepBoth = false;
        bool unmanagedReplace = false;
        bool reopened = false;
        bool processedMedia = false;
        try
        {
            host = environment.BuildHost(clock);
            await environment.StartHostAsync(host, saveFolders: true, cancellationToken);
            MediaToolLocation? location = host.Services
                .GetRequiredService<BundledMediaToolLocator>().TryResolve();
            PackageProbeEnvironment.Require(location is not null, "processing.tool_missing");
            toolPath = location!.FfmpegPath;

            Guid registeredAssetId = await SeedAssetAsync(
                host, environment, PhotoFixture, "registered.jpg", clock, cancellationToken);
            InboxCardDto replaceCard = await environment.CopyAndObserveAsync(
                host, PhotoFixture, "registered.jpg", clock, cancellationToken);
            IngestResult registeredConflict = await host.Services
                .GetRequiredService<IIngestService>().IngestAsync(replaceCard.Id, cancellationToken);
            IngestResult registeredResult = await ResolveAsync(
                host, registeredConflict, ConflictResolution.Replace,
                match => match.AssetId == registeredAssetId, cancellationToken);
            PackageProbeEnvironment.Require(
                registeredResult.Outcome == IngestOutcome.Completed
                && registeredResult.AssetId == registeredAssetId
                && !File.Exists(Path.Combine(environment.InboxDirectory, "registered.jpg")),
                "probe.registered_replace_failed");
            registeredReplace = true;

            InboxCardDto keepBothCard = await environment.CopyAndObserveAsync(
                host, PhotoFixture, "registered.jpg", clock, cancellationToken);
            IngestResult keepBothConflict = await host.Services
                .GetRequiredService<IIngestService>().IngestAsync(keepBothCard.Id, cancellationToken);
            IngestResult keepBothResult = await ResolveAsync(
                host, keepBothConflict, ConflictResolution.KeepBoth, null, cancellationToken);
            PackageProbeEnvironment.Require(
                keepBothResult.Outcome == IngestOutcome.Completed
                && File.Exists(Path.Combine(environment.LibraryDirectory, "registered (2).jpg"))
                && await CountAssetsWithFilenameAsync(
                    host, "registered.jpg", "registered (2).jpg", cancellationToken) == 2,
                "probe.keep_both_failed");
            contentKeepBoth = true;

            File.Copy(
                Path.Combine(environment.FixturesDirectory, PhotoFixture),
                Path.Combine(environment.LibraryDirectory, "unmanaged.jpg"));
            InboxCardDto unmanagedCard = await environment.CopyAndObserveAsync(
                host, PhotoFixture, "unmanaged.jpg", clock, cancellationToken);
            IngestResult unmanagedConflict = await host.Services
                .GetRequiredService<IIngestService>().IngestAsync(unmanagedCard.Id, cancellationToken);
            IngestResult unmanagedResult = await ResolveAsync(
                host, unmanagedConflict, ConflictResolution.Replace,
                match => !match.IsRegistered && match.ActualFilename.Equals(
                    "unmanaged.jpg", StringComparison.OrdinalIgnoreCase), cancellationToken);
            PackageProbeEnvironment.Require(
                unmanagedResult.Outcome == IngestOutcome.Completed
                && unmanagedResult.AssetId.HasValue
                && !File.Exists(Path.Combine(environment.InboxDirectory, "unmanaged.jpg"))
                && await CountAssetsWithFilenameAsync(host, "unmanaged.jpg", null, cancellationToken) == 1,
                "probe.unmanaged_replace_failed");
            unmanagedReplace = true;

            InboxCardDto reopenCard = await environment.CopyAndObserveAsync(
                host, PhotoFixture, "reopen.jpg", clock, cancellationToken);
            IngestResult reopenConflict = await host.Services
                .GetRequiredService<IIngestService>().IngestAsync(reopenCard.Id, cancellationToken);
            PackageProbeEnvironment.Require(
                reopenConflict.Outcome == IngestOutcome.ConflictBlocked
                && reopenConflict.ConflictCaseId.HasValue, "probe.reopen_case_missing");
            Guid reopenCaseId = reopenConflict.ConflictCaseId.GetValueOrDefault();

            await PackageProbeEnvironment.StopHostAsync(host);
            host.Dispose();
            host = environment.BuildHost(clock);
            await environment.StartHostAsync(host, saveFolders: false, cancellationToken);
            ConflictCaseDto? persisted = await host.Services
                .GetRequiredService<IConflictQueryService>().GetOpenAsync(reopenCard.Id, cancellationToken);
            PackageProbeEnvironment.Require(persisted?.Id == reopenCaseId, "probe.reopen_case_not_persisted");
            IngestResult skipped = await host.Services.GetRequiredService<IConflictResolutionService>()
                .ResolveAsync(reopenCaseId, ConflictResolution.Skip, null, cancellationToken);
            PackageProbeEnvironment.Require(
                skipped.Outcome == IngestOutcome.Skipped
                && File.Exists(Path.Combine(environment.InboxDirectory, "reopen.jpg")),
                "probe.reopen_skip_failed");
            reopened = true;

            File.Copy(Path.Combine(environment.FixturesDirectory, VideoFixture),
                Path.Combine(environment.InboxDirectory, VideoFixture));
            File.Copy(Path.Combine(environment.FixturesDirectory, AudioFixture),
                Path.Combine(environment.InboxDirectory, AudioFixture));
            await PackageProbeEnvironment.ObserveAsync(host, clock, cancellationToken);
            InboxCardDto processedCard = await PackageProbeEnvironment.FindCardAsync(
                host, VideoFixture, cancellationToken);
            PackageProbeEnvironment.Require(processedCard.State == InboxItemState.RequiresProcessing,
                "probe.source_not_processable");
            ProcessingCommandResult queued = await host.Services
                .GetRequiredService<IProcessingCommandService>()
                .StartAsync(processedCard.Id, cancellationToken);
            PackageProbeEnvironment.Require(
                queued.Outcome == ProcessingCommandOutcome.Queued && queued.JobId.HasValue,
                "probe.processing_not_queued");
            InboxDetailsDto processed = await PackageProbeEnvironment.WaitForProcessingAsync(
                host.Services.GetRequiredService<IInboxQueryService>(),
                processedCard.Id, queued.JobId.GetValueOrDefault(), cancellationToken);
            PackageProbeEnvironment.Require(processed.Card.State == InboxItemState.ReadyToTag,
                "probe.processed_item_not_ready");
            IngestResult processedResult = await host.Services.GetRequiredService<IIngestService>()
                .IngestAsync(processedCard.Id, cancellationToken);
            PackageProbeEnvironment.Require(
                processedResult.Outcome == IngestOutcome.Completed
                && !File.Exists(Path.Combine(environment.InboxDirectory, VideoFixture))
                && !File.Exists(Path.Combine(environment.InboxDirectory, AudioFixture))
                && File.Exists(Path.Combine(environment.LibraryDirectory, VideoFixture))
                && environment.ProcessingWorkspaceIsEmpty(), "probe.processed_ingest_failed");
            processedMedia = true;

            IReadOnlyList<LibraryAssetDto> assets = await host.Services
                .GetRequiredService<ILibraryQueryService>()
                .SearchAsync(LibraryQuery.Default, cancellationToken);
            int imageAssets = assets.Count(asset => asset.MediaKind == MediaKind.Image);
            int videoAssets = assets.Count(asset => asset.MediaKind == MediaKind.Video);
            PackageProbeEnvironment.Require(
                imageAssets == 3 && videoAssets == 1 && assets.Count == 4,
                "probe.asset_counts_invalid");
            PackageProbeEnvironment.Require(environment.InternalStorageIsClean(),
                "probe.internal_storage_retained");

            await WriteResultAsync(environment, new Stage4PackageProbeResult(
                true, registeredReplace, contentKeepBoth, unmanagedReplace, reopened,
                processedMedia, imageAssets, videoAssets, assets.Count, toolPath, null), cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProbeFailureException error)
        {
            await WriteFailureAsync(environment, registeredReplace, contentKeepBoth,
                unmanagedReplace, reopened, processedMedia, toolPath, error.ErrorCode);
            return 1;
        }
        catch
        {
            await WriteFailureAsync(environment, registeredReplace, contentKeepBoth,
                unmanagedReplace, reopened, processedMedia, toolPath, "probe.unexpected_failure");
            return 1;
        }
        finally
        {
            if (host is not null)
            {
                await PackageProbeEnvironment.StopHostAsync(host);
                host.Dispose();
            }
        }
    }

    private static async Task<Guid> SeedAssetAsync(IHost host, PackageProbeEnvironment environment,
        string fixture, string target, ProbeTimeProvider clock, CancellationToken cancellationToken) =>
        await environment.SeedAssetAsync(host, fixture, target, clock, cancellationToken);

    private static async Task<IngestResult> ResolveAsync(
        IHost host, IngestResult blocked, ConflictResolution resolution,
        Func<ConflictMatchRecord, bool>? matchSelector, CancellationToken cancellationToken)
    {
        PackageProbeEnvironment.Require(blocked.Outcome == IngestOutcome.ConflictBlocked
            && blocked.ConflictCaseId.HasValue, "probe.conflict_missing");
        IConflictCaseStore store = host.Services.GetRequiredService<IConflictCaseStore>();
        ConflictCaseRecord conflict = (await store.LoadAsync(
            blocked.ConflictCaseId!.Value, cancellationToken))!;
        Guid? selectedId = matchSelector is null ? null : conflict.Matches.Single(matchSelector).Id;
        return await host.Services.GetRequiredService<IConflictResolutionService>()
            .ResolveAsync(conflict.Id, resolution, selectedId, cancellationToken);
    }

    private static async Task<int> CountAssetsWithFilenameAsync(
        IHost host, string first, string? second, CancellationToken cancellationToken)
    {
        IReadOnlyList<LibraryAssetDto> assets = await host.Services
            .GetRequiredService<ILibraryQueryService>()
            .SearchAsync(LibraryQuery.Default, cancellationToken);
        return assets.Count(asset => asset.ActualFilename.Equals(first, StringComparison.OrdinalIgnoreCase)
            || second is not null && asset.ActualFilename.Equals(second, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WriteFailureAsync(PackageProbeEnvironment environment,
        bool registeredReplace, bool contentKeepBoth, bool unmanagedReplace, bool reopened,
        bool processedMedia, string? toolPath, string errorCode) =>
        await WriteResultAsync(environment, new Stage4PackageProbeResult(
            false, registeredReplace, contentKeepBoth, unmanagedReplace, reopened, processedMedia,
            0, 0, 0, toolPath, errorCode), CancellationToken.None);

    private static async Task WriteResultAsync(PackageProbeEnvironment environment,
        Stage4PackageProbeResult result, CancellationToken cancellationToken)
    {
        environment.ValidateOwnedProbeTree([.. environment.RuntimeDirectoryPaths]);
        await environment.WriteResultAsync(
            Stage4PackageProbeOptions.ResultFilename, result, cancellationToken);
    }
}
