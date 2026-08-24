using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Ingest;
using Xunit;

namespace TraceFold.Media.Application.Tests.Ingest;

public sealed class ConflictDetectionServiceTests
{
    private const string HashA =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HashB =
        "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 24, 1, 2, 3, TimeSpan.Zero);
    private static readonly LibraryFolders Folders = new(
        "T:\\inbox",
        "T:\\library",
        "T:\\work");

    [Fact]
    public async Task Detector_combines_registered_name_and_all_content_matches()
    {
        var catalog = new RecordingConflictStore
        {
            Registered =
            [
                Registered(
                    "same.jpg",
                    HashA,
                    ConflictReason.Name | ConflictReason.Content),
                Registered("copy.jpg", HashA, ConflictReason.Content)
            ]
        };
        var storage = new SnapshotStorage(
            File("same.jpg", HashA),
            File("copy.jpg", HashA));
        var service = new ConflictDetectionService(catalog, storage);

        ConflictCaseRecord? result = await service.DetectAsync(
            Preparation("same.jpg", HashA),
            Folders,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(2, result.Matches.Count);
        Assert.Equal("same.jpg", result.Matches[0].ActualFilename);
        Assert.Equal(
            ConflictReason.Name | ConflictReason.Content,
            result.Matches[0].Reason);
        Assert.Equal("copy.jpg", result.Matches[1].ActualFilename);
    }

    [Fact]
    public async Task Detector_adds_unmanaged_same_name_match()
    {
        var catalog = new RecordingConflictStore();
        var storage = new SnapshotStorage(File("same.jpg", HashB));
        var service = new ConflictDetectionService(catalog, storage);

        ConflictCaseRecord? result = await service.DetectAsync(
            Preparation("same.jpg", HashA),
            Folders,
            TestContext.Current.CancellationToken);

        ConflictMatchRecord match = Assert.Single(result!.Matches);
        Assert.False(match.IsRegistered);
        Assert.Equal(ConflictReason.Name, match.Reason);
        Assert.Null(match.AssetId);
    }

    [Fact]
    public async Task Detector_does_not_persist_when_no_conflict_remains()
    {
        var catalog = new RecordingConflictStore();
        var service = new ConflictDetectionService(
            catalog,
            new SnapshotStorage());

        ConflictCaseRecord? result = await service.DetectAsync(
            Preparation("same.jpg", HashA),
            Folders,
            TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal(0, catalog.ReplaceCallCount);
    }

    [Fact]
    public async Task Registered_same_name_is_not_duplicated_as_unmanaged()
    {
        RegisteredConflictCandidate registered = Registered(
            "same.jpg",
            HashB,
            ConflictReason.Name);
        var catalog = new RecordingConflictStore
        {
            Registered = [registered]
        };
        var service = new ConflictDetectionService(
            catalog,
            new SnapshotStorage(File("same.jpg", HashB)));

        ConflictCaseRecord? result = await service.DetectAsync(
            Preparation("same.jpg", HashA),
            Folders,
            TestContext.Current.CancellationToken);

        ConflictMatchRecord match = Assert.Single(result!.Matches);
        Assert.True(match.IsRegistered);
        Assert.Equal(registered.AssetId, match.AssetId);
    }

    [Fact]
    public async Task Detector_keeps_distinct_assets_with_the_same_content_hash()
    {
        var catalog = new RecordingConflictStore
        {
            Registered =
            [
                Registered("copy-a.jpg", HashA, ConflictReason.Content),
                Registered("copy-b.jpg", HashA, ConflictReason.Content)
            ]
        };
        var service = new ConflictDetectionService(
            catalog,
            new SnapshotStorage(
                File("copy-a.jpg", HashA),
                File("copy-b.jpg", HashA)));

        ConflictCaseRecord? result = await service.DetectAsync(
            Preparation("same.jpg", HashA),
            Folders,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result!.Matches.Count);
        Assert.Equal(
            2,
            result.Matches.Select(match => match.AssetId).Distinct().Count());
    }

    [Fact]
    public async Task Detector_recomputes_reasons_from_verified_disk_identity()
    {
        RegisteredConflictCandidate stale = Registered(
            "copy.jpg",
            HashA,
            ConflictReason.Content);
        var catalog = new RecordingConflictStore
        {
            Registered = [stale]
        };
        var service = new ConflictDetectionService(
            catalog,
            new SnapshotStorage(File("copy.jpg", HashB)));

        ConflictCaseRecord? result = await service.DetectAsync(
            Preparation("same.jpg", HashA),
            Folders,
            TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal(0, catalog.ReplaceCallCount);
    }

    [Fact]
    public async Task Detector_records_lowercase_format_and_proven_video_duration()
    {
        var catalog = new RecordingConflictStore
        {
            Registered =
            [Registered("same.MP4", HashA, ConflictReason.Name)]
        };
        var service = new ConflictDetectionService(
            catalog,
            new SnapshotStorage(File("same.MP4", HashB)),
            new ProbeTool(TimeSpan.FromSeconds(12)));

        ConflictCaseRecord? result = await service.DetectAsync(
            Preparation("same.MP4", HashA, MediaKind.Video),
            Folders,
            TestContext.Current.CancellationToken);

        ConflictTechnicalMetadata technical =
            Assert.Single(result!.Matches).Technical;
        Assert.Equal("mp4", technical.Format);
        Assert.Equal(TimeSpan.FromSeconds(12), technical.Duration);
        Assert.Null(technical.PixelWidth);
        Assert.Null(technical.PixelHeight);
    }

    [Fact]
    public async Task Probe_failure_does_not_suppress_conflict_detection()
    {
        var catalog = new RecordingConflictStore();
        var service = new ConflictDetectionService(
            catalog,
            new SnapshotStorage(File("same.jpg", HashB)),
            new ProbeTool(error: new InvalidOperationException("probe failed")));

        ConflictCaseRecord? result = await service.DetectAsync(
            Preparation("same.jpg", HashA),
            Folders,
            TestContext.Current.CancellationToken);

        ConflictTechnicalMetadata technical =
            Assert.Single(result!.Matches).Technical;
        Assert.Equal("jpg", technical.Format);
        Assert.Null(technical.Duration);
    }

    [Theory]
    [InlineData("source_hash_changed", "conflict.source_hash_changed")]
    [InlineData("target_hash_changed", "conflict.target_hash_changed")]
    [InlineData(
        "target_registration_changed",
        "conflict.target_registration_changed")]
    [InlineData("asset_version_changed", "conflict.asset_version_changed")]
    public async Task Revalidation_invalidates_any_identity_change(
        string change,
        string expectedError)
    {
        RegisteredConflictCandidate registered = Registered(
            "same.jpg",
            HashB,
            ConflictReason.Name,
            metadataVersion: 3);
        var catalog = new RecordingConflictStore
        {
            Registered = [registered]
        };
        var storage = new SnapshotStorage(File("same.jpg", HashB));
        var service = new ConflictDetectionService(catalog, storage);
        IngestPreparation original = Preparation("same.jpg", HashA);
        ConflictCaseRecord conflict = (await service.DetectAsync(
            original,
            Folders,
            TestContext.Current.CancellationToken))!;
        IngestPreparation current = original;

        switch (change)
        {
            case "source_hash_changed":
                current = original with
                {
                    SourceSha256 = HashB,
                    Input = original.Input with { Sha256 = HashB }
                };
                break;
            case "target_hash_changed":
                storage.Set(File("same.jpg", HashA));
                break;
            case "target_registration_changed":
                catalog.Registered = [];
                break;
            case "asset_version_changed":
                catalog.Registered =
                [registered with { AssetMetadataVersion = 4 }];
                break;
            default:
                throw new InvalidOperationException(change);
        }

        ConflictRevalidationResult result = await service.RevalidateAsync(
            conflict,
            current,
            Folders,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Equal(expectedError, result.ErrorCode);
    }

    [Fact]
    public async Task Revalidation_ignores_inbox_metadata_version_changes()
    {
        RegisteredConflictCandidate registered = Registered(
            "same.jpg",
            HashB,
            ConflictReason.Name,
            metadataVersion: 3);
        var catalog = new RecordingConflictStore
        {
            Registered = [registered]
        };
        var storage = new SnapshotStorage(File("same.jpg", HashB));
        var service = new ConflictDetectionService(catalog, storage);
        IngestPreparation original = Preparation("same.jpg", HashA);
        ConflictCaseRecord conflict = (await service.DetectAsync(
            original,
            Folders,
            TestContext.Current.CancellationToken))!;

        ConflictRevalidationResult result = await service.RevalidateAsync(
            conflict,
            original with { MetadataVersion = original.MetadataVersion + 1 },
            Folders,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorCode);
        Assert.Single(result.CurrentMatches);
        Assert.Equal(conflict.Matches[0].Id, result.CurrentMatches[0].Id);
    }

    private static IngestPreparation Preparation(
        string filename,
        string hash,
        MediaKind mediaKind = MediaKind.Image)
    {
        string path = $"T:\\inbox\\{filename}";
        return new IngestPreparation(
            Guid.NewGuid(),
            mediaKind,
            path,
            3,
            ObservedAt,
            hash,
            filename,
            "incoming",
            [],
            null,
            [],
            7,
            new PreparedIngestInput(
                IngestCleanupKind.InboxSource,
                path,
                3,
                ObservedAt,
                hash,
                []));
    }

    private static RegisteredConflictCandidate Registered(
        string filename,
        string storedHash,
        ConflictReason reason,
        long metadataVersion = 1) =>
        new(
            Guid.NewGuid(),
            metadataVersion,
            filename,
            filename,
            null,
            Path.GetExtension(filename).Equals(
                ".mp4",
                StringComparison.OrdinalIgnoreCase)
                ? MediaKind.Video
                : MediaKind.Image,
            3,
            storedHash,
            reason,
            new ConflictMetadataSnapshot(null, [], null, []));

    private static LibraryFileSnapshot File(string filename, string hash) =>
        new(
            $"T:\\library\\{filename}",
            filename,
            3,
            ObservedAt,
            hash);

    private sealed class RecordingConflictStore : IConflictCaseStore
    {
        public IReadOnlyList<RegisteredConflictCandidate> Registered { get; set; }
            = [];

        public int ReplaceCallCount { get; private set; }

        public Task<IReadOnlyList<RegisteredConflictCandidate>>
            FindRegisteredCandidatesAsync(
                string targetFilename,
                string sha256,
                CancellationToken cancellationToken) =>
            Task.FromResult(Registered);

        public Task<ConflictCaseRecord> ReplaceOpenAsync(
            Guid inboxItemId,
            ConflictInputSnapshot input,
            IReadOnlyList<ConflictMatchRecord> matches,
            CancellationToken cancellationToken)
        {
            ReplaceCallCount++;
            return Task.FromResult(new ConflictCaseRecord(
                Guid.NewGuid(),
                inboxItemId,
                ConflictCaseStatus.Open,
                input,
                null,
                null,
                null,
                matches));
        }

        public Task<ConflictCaseRecord?> LoadAsync(
            Guid conflictCaseId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ConflictCaseRecord?>(null);

        public Task<ConflictCaseRecord?> LoadOpenForInboxAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ConflictCaseRecord?>(null);

        public Task ChooseAsync(
            Guid conflictCaseId,
            ConflictResolution resolution,
            Guid? matchId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SkipAsync(
            Guid conflictCaseId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateAsync(
            Guid conflictCaseId,
            string reason,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SnapshotStorage(
        params LibraryFileSnapshot[] snapshots) : IControlledMediaStorage
    {
        private readonly Dictionary<string, LibraryFileSnapshot> _snapshots =
            snapshots.ToDictionary(
                snapshot => snapshot.RelativePath,
                StringComparer.OrdinalIgnoreCase);

        public void Set(LibraryFileSnapshot snapshot) =>
            _snapshots[snapshot.RelativePath] = snapshot;

        public Task<LibraryFileSnapshot?> InspectTargetAsync(
            LibraryFolders folders,
            string targetFilename,
            CancellationToken cancellationToken) =>
            Task.FromResult(_snapshots.GetValueOrDefault(targetFilename));

        public Task<bool> TargetExistsAsync(
            LibraryFolders folders,
            string targetFilename,
            CancellationToken cancellationToken) =>
            Task.FromResult(_snapshots.ContainsKey(targetFilename));

        public Task<StagedFile> StageAsync(
            Guid operationId,
            LibraryFolders folders,
            string sourcePath,
            string expectedSha256,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CommittedFile> CommitAsync(
            Guid operationId,
            LibraryFolders folders,
            StagedFile staged,
            string targetFilename,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SourceDeleteResult> DeleteSourceIfMatchesAsync(
            LibraryFolders folders,
            string sourcePath,
            long expectedSize,
            string expectedSha256,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> VerifyCommittedAsync(
            LibraryFolders folders,
            CommittedFile committed,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CleanupTemporaryFilesAsync(
            Guid operationId,
            LibraryFolders folders,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ProbeTool(
        TimeSpan? duration = null,
        Exception? error = null) : IMediaTool
    {
        public Task<MediaToolAvailability> CheckAvailabilityAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new MediaToolAvailability(true, "test", null));

        public Task<MediaProbeResult> ProbeAsync(
            string trustedPath,
            CancellationToken cancellationToken) =>
            error is null
                ? Task.FromResult(new MediaProbeResult(
                    duration ?? TimeSpan.Zero,
                    [new MediaStreamInfo("video", duration)]))
                : Task.FromException<MediaProbeResult>(error);

        public Task<FfmpegResult> RunFfmpegAsync(
            FfmpegRequest request,
            IProgress<int> progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
