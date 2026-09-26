using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Desktop.Diagnostics;

public sealed class Stage5ABatchProbeScenario : IPackageProbeScenario
{
    private const string PhotoFixture = "photo.jpg";
    private const string VideoFixture = "paired.mp4";
    private const string AudioFixture = "paired.m4a";
    private const string PhotoCopyFilename = "photo-copy.jpg";
    private const string OrdinaryVideoFilename = "ordinary.mp4";

    public PackageProbeScenarioKind Kind => PackageProbeScenarioKind.Stage5ABatch;

    public async Task<int> RunAsync(
        PackageProbeRequest request,
        PackageProbeEnvironment environment,
        CancellationToken cancellationToken)
    {
        environment.CreateRuntimeDirectories();
        environment.ValidateOwnedProbeTree([.. environment.RuntimeDirectoryPaths]);

        var clock = new ProbeTimeProvider(DateTimeOffset.Parse("2026-08-24T12:00:00Z"));
        IHost? host = null;
        bool atomicMetadata = false;
        bool genericProcessingQueued = false;
        bool processingCompleted = false;
        int completedIngestCount = 0;
        int conflictCount = 0;
        bool conflictSourceRetained = false;
        bool internalStorageClean = false;
        try
        {
            host = environment.BuildHost(clock);
            await environment.StartHostAsync(host, saveFolders: true, cancellationToken);
            CopyInputs(environment);
            await PackageProbeEnvironment.ObserveAsync(host, clock, cancellationToken);

            IInboxQueryService inbox = host.Services.GetRequiredService<IInboxQueryService>();
            IReadOnlyList<InboxCardDto> cards = await inbox.SearchAsync(
                new InboxQuery(null, null), cancellationToken);
            InboxCardDto photo = Find(cards, PhotoFixture);
            InboxCardDto photoCopy = Find(cards, PhotoCopyFilename);
            InboxCardDto ordinaryVideo = Find(cards, OrdinaryVideoFilename);
            InboxCardDto pairedVideo = Find(cards, VideoFixture);
            string scanDetail = "count=" + cards.Count + ";" + string.Join(
                ";",
                new[] { photo, photoCopy, ordinaryVideo, pairedVideo }
                    .Select((card, ordinal) => ordinal + ":" + card.MediaKind + ":" + card.State));
            PackageProbeEnvironment.RequireStep("probe.batch_scan_invalid", scanDetail,
                ("count", cards.Count == 4),
                ("photo_kind", photo.MediaKind == MediaKind.Image),
                ("photo_copy_kind", photoCopy.MediaKind == MediaKind.Image),
                ("ordinary_video_kind", ordinaryVideo.MediaKind == MediaKind.Video),
                ("ordinary_video_state", ordinaryVideo.State == InboxItemState.ReadyToTag),
                ("paired_video_kind", pairedVideo.MediaKind == MediaKind.Video),
                ("paired_video_state", pairedVideo.State == InboxItemState.RequiresProcessing));

            InboxCardDto[] ordered = [photo, photoCopy, ordinaryVideo, pairedVideo];
            IReadOnlyList<InboxSelectionEntry> initialSelection = Selection(ordered);
            BatchMetadataResult metadata = await host.Services
                .GetRequiredService<IBatchMetadataService>()
                .ApplyAsync(CreateMetadataCommand(initialSelection), cancellationToken);
            IReadOnlyList<InboxDetailsDto> documents = await ReadDocumentsAsync(
                inbox, ordered, cancellationToken);
            atomicMetadata = metadata.Succeeded
                && metadata.ChangedCount == 4
                && metadata.UnchangedCount == 0
                && metadata.AffectedCount == 4
                && SameVersionOneTerms(documents);
            PackageProbeEnvironment.Require(atomicMetadata, "probe.batch_metadata_invalid");

            IReadOnlyList<InboxSelectionEntry> selection = documents
                .Select((document, ordinal) => new InboxSelectionEntry(
                    document.Card.Id, document.Metadata.Version, ordinal))
                .ToArray();
            BatchProcessingResult processing = await host.Services
                .GetRequiredService<IBatchProcessingService>()
                .StartAsync(selection, cancellationToken);
            genericProcessingQueued = processing.Items.Count == 4
                && processing.Items.Count(item => item.Outcome == BatchProcessingItemOutcome.Queued) == 1
                && processing.Items.Count(item => item.Outcome == BatchProcessingItemOutcome.NotRequired) == 3
                && processing.Items.Single(item => item.Outcome == BatchProcessingItemOutcome.Queued)
                    .InboxItemId == pairedVideo.Id;
            PackageProbeEnvironment.Require(
                genericProcessingQueued, "probe.batch_processing_not_queued");

            InboxDetailsDto queued = (await inbox.GetDetailsAsync(
                pairedVideo.Id, cancellationToken))!;
            PackageProbeEnvironment.Require(queued.Processing is not null,
                "probe.batch_processing_job_missing");
            InboxDetailsDto processed = await PackageProbeEnvironment.WaitForProcessingAsync(
                inbox, pairedVideo.Id, queued.Processing!.JobId, cancellationToken);
            processingCompleted = processed.Card.State == InboxItemState.ReadyToTag;
            PackageProbeEnvironment.Require(
                processingCompleted, "probe.batch_processing_not_completed");

            BatchIngestPreviewDto preview = await host.Services
                .GetRequiredService<IBatchIngestService>()
                .PreviewAsync(selection, cancellationToken);
            PackageProbeEnvironment.Require(
                preview.Items.Count == 4 && preview.ReadyCount == 4,
                "probe.batch_preview_not_ready");

            BatchIngestResult ingest = await host.Services
                .GetRequiredService<IBatchIngestService>()
                .ExecuteAsync(selection, null, CancellationToken.None, cancellationToken);
            completedIngestCount = ingest.CompletedCount;
            conflictCount = ingest.ConflictCount;
            string ingestDetail = "stopped=" + ingest.StoppedByUser
                + ";count=" + ingest.Items.Count
                + ";completed=" + completedIngestCount
                + ";conflicts=" + conflictCount
                + ";" + string.Join(
                    ",",
                    ingest.Items.Select(item =>
                        item.Ordinal + ":" + item.Outcome + ":" + item.ErrorCode));
            PackageProbeEnvironment.RequireStep("probe.batch_ingest_outcomes_invalid", ingestDetail,
                ("not_stopped", !ingest.StoppedByUser),
                ("count", ingest.Items.Count == 4),
                ("ordinal0", ingest.Items.Count > 0
                    && ingest.Items[0].Outcome == BatchIngestItemOutcome.Completed),
                ("ordinal1", ingest.Items.Count > 1
                    && ingest.Items[1].Outcome == BatchIngestItemOutcome.ConflictBlocked),
                ("ordinal2", ingest.Items.Count > 2
                    && ingest.Items[2].Outcome == BatchIngestItemOutcome.Completed),
                ("ordinal3", ingest.Items.Count > 3
                    && ingest.Items[3].Outcome == BatchIngestItemOutcome.Completed),
                ("completed_count", completedIngestCount == 3),
                ("conflict_count", conflictCount == 1));

            conflictSourceRetained = File.Exists(
                Path.Combine(environment.InboxDirectory, PhotoCopyFilename));
            bool videoSourceRemoved = !File.Exists(
                Path.Combine(environment.InboxDirectory, VideoFixture));
            bool audioSourceRemoved = !File.Exists(
                Path.Combine(environment.InboxDirectory, AudioFixture));
            PackageProbeEnvironment.RequireStep("probe.batch_source_cleanup_invalid",
                "conflict_source=" + conflictSourceRetained
                    + ";video_removed=" + videoSourceRemoved
                    + ";audio_removed=" + audioSourceRemoved,
                ("conflict_source_retained", conflictSourceRetained),
                ("video_source_removed", videoSourceRemoved),
                ("audio_source_removed", audioSourceRemoved));
            IReadOnlyList<LibraryAssetDto> assets = await host.Services
                .GetRequiredService<ILibraryQueryService>()
                .SearchAsync(LibraryQuery.Default, cancellationToken);
            int libraryImageCount = assets.Count(asset => asset.MediaKind == MediaKind.Image);
            int libraryVideoCount = assets.Count(asset => asset.MediaKind == MediaKind.Video);
            PackageProbeEnvironment.RequireStep("probe.batch_library_invalid",
                "count=" + assets.Count
                    + ";images=" + libraryImageCount
                    + ";videos=" + libraryVideoCount,
                ("count", assets.Count == 3),
                ("images", libraryImageCount == 1),
                ("videos", libraryVideoCount == 2),
                ("has_photo", assets.Any(asset => asset.ActualFilename.Equals(
                    PhotoFixture, StringComparison.OrdinalIgnoreCase))),
                ("has_ordinary_video", assets.Any(asset => asset.ActualFilename.Equals(
                    OrdinaryVideoFilename, StringComparison.OrdinalIgnoreCase))),
                ("has_paired_video", assets.Any(asset => asset.ActualFilename.Equals(
                    VideoFixture, StringComparison.OrdinalIgnoreCase))));
            internalStorageClean = environment.InternalWorkIsClean();
            PackageProbeEnvironment.Require(
                internalStorageClean, "probe.batch_internal_storage_retained");

            await WriteResultAsync(environment, new Stage5ABatchProbeResult(
                true, atomicMetadata, genericProcessingQueued, processingCompleted,
                completedIngestCount, conflictCount, conflictSourceRetained,
                internalStorageClean, null), cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProbeFailureException error)
        {
            if (host is not null)
            {
                PackageProbeEnvironment.WriteDatabaseFileState(host, "first_error");
            }

            await WriteFailureAsync(environment, atomicMetadata, genericProcessingQueued,
                processingCompleted, completedIngestCount, conflictCount,
                conflictSourceRetained, internalStorageClean, error.ErrorCode);
            return 1;
        }
        catch (Exception error)
        {
            PackageProbeTrace.WriteException("stage5a", error);
            if (host is not null)
            {
                PackageProbeEnvironment.WriteDatabaseFileState(host, "first_error");
            }

            await WriteFailureAsync(environment, atomicMetadata, genericProcessingQueued,
                processingCompleted, completedIngestCount, conflictCount,
                conflictSourceRetained, internalStorageClean, "probe.unexpected_failure");
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

    private static void CopyInputs(PackageProbeEnvironment environment)
    {
        Copy(environment, PhotoFixture, PhotoFixture);
        Copy(environment, PhotoFixture, PhotoCopyFilename);
        Copy(environment, VideoFixture, OrdinaryVideoFilename);
        Copy(environment, VideoFixture, VideoFixture);
        Copy(environment, AudioFixture, AudioFixture);
    }

    private static void Copy(PackageProbeEnvironment environment, string fixture, string target) =>
        File.Copy(Path.Combine(environment.FixturesDirectory, fixture),
            Path.Combine(environment.InboxDirectory, target), overwrite: false);

    private static InboxCardDto Find(IReadOnlyList<InboxCardDto> cards, string filename) =>
        cards.Single(card => card.ActualFilename.Equals(
            filename, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<InboxSelectionEntry> Selection(
        IReadOnlyList<InboxCardDto> cards) =>
        cards.Select((card, ordinal) => new InboxSelectionEntry(
            card.Id, card.MetadataVersion, ordinal)).ToArray();

    private static BatchMetadataCommand CreateMetadataCommand(
        IReadOnlyList<InboxSelectionEntry> selection) =>
        BatchMetadataCommand.Create(
            selection,
            BatchMetadataMode.Add,
            new BatchMetadataSetEdit(
                BatchMetadataFieldOperation.Add,
                [BatchTaxonomyReference.New(TaxonomyKind.Author, "Stage 5A Author")]),
            BatchMetadataSeriesEdit.Unchanged,
            new BatchMetadataSetEdit(
                BatchMetadataFieldOperation.Add,
                [BatchTaxonomyReference.New(TaxonomyKind.Tag, "Stage 5A Tag")]));

    private static async Task<IReadOnlyList<InboxDetailsDto>> ReadDocumentsAsync(
        IInboxQueryService inbox,
        IReadOnlyList<InboxCardDto> cards,
        CancellationToken cancellationToken)
    {
        var documents = new List<InboxDetailsDto>(cards.Count);
        foreach (InboxCardDto card in cards)
        {
            InboxDetailsDto? document = await inbox.GetDetailsAsync(card.Id, cancellationToken);
            PackageProbeEnvironment.Require(document is not null,
                "probe.batch_metadata_document_missing");
            documents.Add(document!);
        }

        return documents;
    }

    private static bool SameVersionOneTerms(IReadOnlyList<InboxDetailsDto> documents)
    {
        if (documents.Count != 4
            || documents.Any(document => document.Metadata.Version != 1
                || document.Metadata.Authors.Count != 1
                || document.Metadata.Tags.Count != 1))
        {
            return false;
        }

        Guid author = documents[0].Metadata.Authors[0].Id;
        Guid tag = documents[0].Metadata.Tags[0].Id;
        return author != Guid.Empty && tag != Guid.Empty
            && documents.All(document => document.Metadata.Authors[0].Id == author
                && document.Metadata.Tags[0].Id == tag);
    }

    private static async Task WriteFailureAsync(
        PackageProbeEnvironment environment,
        bool atomicMetadata,
        bool genericProcessingQueued,
        bool processingCompleted,
        int completedIngestCount,
        int conflictCount,
        bool conflictSourceRetained,
        bool internalStorageClean,
        string errorCode) =>
        await WriteResultAsync(environment, new Stage5ABatchProbeResult(
            false, atomicMetadata, genericProcessingQueued, processingCompleted,
            completedIngestCount, conflictCount, conflictSourceRetained,
            internalStorageClean, errorCode), CancellationToken.None);

    private static async Task WriteResultAsync(
        PackageProbeEnvironment environment,
        Stage5ABatchProbeResult result,
        CancellationToken cancellationToken)
    {
        environment.ValidateOwnedProbeTree([.. environment.RuntimeDirectoryPaths]);
        await environment.WriteScenarioResultAsync(result, cancellationToken);
    }
}
