using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Application.Tests.Ingest;

public sealed class ConflictResolutionServiceTests
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
    public async Task Skip_closes_case_without_starting_execution()
    {
        ResolutionFixture fixture = ResolutionFixture.Create();

        IngestResult result = await fixture.Service.ResolveAsync(
            fixture.Conflict.Id,
            ConflictResolution.Skip,
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.Skipped, result.Outcome);
        Assert.True(fixture.Cases.WasSkipped);
        Assert.Null(fixture.Executor.Plan);
        Assert.Equal(0, fixture.InputResolver.CallCount);
    }

    [Fact]
    public async Task Keep_both_freezes_latest_metadata_and_uses_requested_name()
    {
        ResolutionFixture fixture = ResolutionFixture.Create(
            displayName: "latest display");

        IngestResult result = await fixture.Service.ResolveAsync(
            fixture.Conflict.Id,
            ConflictResolution.KeepBoth,
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.Completed, result.Outcome);
        Assert.Equal(IngestDisposition.KeepBoth, fixture.Executor.Plan!.Disposition);
        Assert.Equal("photo.jpg", fixture.Executor.Plan.RequestedTargetFilename);
        Assert.Equal("latest display", fixture.Executor.Preparation!.DisplayName);
        Assert.True(fixture.Cases.WasChosen);
    }

    [Fact]
    public async Task Replace_registered_builds_plan_from_persisted_selected_match()
    {
        ConflictMatchRecord registered = Match(
            "photo.jpg",
            isRegistered: true,
            ConflictReason.Name);
        ResolutionFixture fixture = ResolutionFixture.Create(registered);

        IngestResult result = await fixture.Service.ResolveAsync(
            fixture.Conflict.Id,
            ConflictResolution.Replace,
            registered.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.Completed, result.Outcome);
        Assert.Equal(
            IngestDisposition.ReplaceRegistered,
            fixture.Executor.Plan!.Disposition);
        Assert.Equal(registered.AssetId, fixture.Executor.Plan.ReplacementAssetId);
        Assert.Equal(
            registered.AssetMetadataVersion,
            fixture.Executor.Plan.ReplacementMetadataVersion);
        Assert.Equal(registered.RelativePath, fixture.Executor.Plan.Target!.RelativePath);
    }

    [Fact]
    public async Task Replace_unmanaged_builds_plan_without_an_asset_id()
    {
        ConflictMatchRecord unmanaged = Match(
            "photo.jpg",
            isRegistered: false,
            ConflictReason.Name);
        ResolutionFixture fixture = ResolutionFixture.Create(unmanaged);

        _ = await fixture.Service.ResolveAsync(
            fixture.Conflict.Id,
            ConflictResolution.Replace,
            unmanaged.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            IngestDisposition.ReplaceUnregistered,
            fixture.Executor.Plan!.Disposition);
        Assert.Null(fixture.Executor.Plan.ReplacementAssetId);
    }

    [Fact]
    public async Task Content_only_replace_requires_one_persisted_match_selection()
    {
        ConflictMatchRecord content = Match(
            "copy.jpg",
            isRegistered: true,
            ConflictReason.Content);
        ResolutionFixture fixture = ResolutionFixture.Create(content);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ResolveAsync(
                fixture.Conflict.Id,
                ConflictResolution.Replace,
                null,
                TestContext.Current.CancellationToken));

        Assert.Null(fixture.Executor.Plan);
    }

    [Fact]
    public async Task Replace_selects_only_one_of_multiple_duplicate_hashes()
    {
        ConflictMatchRecord first = Match(
            "copy-a.jpg",
            isRegistered: true,
            ConflictReason.Content);
        ConflictMatchRecord second = Match(
            "copy-b.jpg",
            isRegistered: true,
            ConflictReason.Content);
        ResolutionFixture fixture = ResolutionFixture.Create(first, second);

        _ = await fixture.Service.ResolveAsync(
            fixture.Conflict.Id,
            ConflictResolution.Replace,
            second.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(second.AssetId, fixture.Executor.Plan!.ReplacementAssetId);
        Assert.Equal("copy-b.jpg", fixture.Executor.Plan.Target!.RelativePath);
    }

    [Fact]
    public async Task Changed_identity_invalidates_and_returns_conflict_changed()
    {
        ResolutionFixture fixture = ResolutionFixture.Create();
        fixture.Detector.Validation = ConflictRevalidationResult.Invalid(
            "conflict.target_hash_changed");
        fixture.Detector.Redetected = null;

        IngestResult result = await fixture.Service.ResolveAsync(
            fixture.Conflict.Id,
            ConflictResolution.KeepBoth,
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.ConflictChanged, result.Outcome);
        Assert.True(fixture.Cases.WasInvalidated);
        Assert.Null(fixture.Executor.Plan);
    }

    [Fact]
    public async Task Changed_identity_returns_the_replacement_conflict_case()
    {
        ResolutionFixture fixture = ResolutionFixture.Create();
        fixture.Detector.Validation = ConflictRevalidationResult.Invalid(
            "conflict.target_hash_changed");
        ConflictCaseRecord replacement = fixture.Conflict with
        {
            Id = Guid.NewGuid()
        };
        fixture.Detector.Redetected = replacement;

        IngestResult result = await fixture.Service.ResolveAsync(
            fixture.Conflict.Id,
            ConflictResolution.KeepBoth,
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.ConflictBlocked, result.Outcome);
        Assert.Equal(replacement.Id, result.ConflictCaseId);
    }

    private static ConflictMatchRecord Match(
        string filename,
        bool isRegistered,
        ConflictReason reason) =>
        new(
            Guid.NewGuid(),
            reason,
            isRegistered,
            isRegistered ? Guid.NewGuid() : null,
            isRegistered ? 4 : null,
            filename,
            filename,
            null,
            MediaKind.Image,
            2,
            ObservedAt,
            HashB,
            new ConflictMetadataSnapshot(null, [], null, []),
            new ConflictTechnicalMetadata("jpg", null, null, null));

    private sealed class ResolutionFixture
    {
        private ResolutionFixture(
            ConflictCaseRecord conflict,
            ResolutionCaseStore cases,
            ResolutionDetector detector,
            ResolutionInputResolver inputResolver,
            RecordingExecutor executor,
            ConflictResolutionService service)
        {
            Conflict = conflict;
            Cases = cases;
            Detector = detector;
            InputResolver = inputResolver;
            Executor = executor;
            Service = service;
        }

        public ConflictCaseRecord Conflict { get; }

        public ResolutionCaseStore Cases { get; }

        public ResolutionDetector Detector { get; }

        public ResolutionInputResolver InputResolver { get; }

        public RecordingExecutor Executor { get; }

        public ConflictResolutionService Service { get; }

        public static ResolutionFixture Create(
            string displayName = "display") =>
            Create(
                displayName,
                Match("photo.jpg", true, ConflictReason.Name));

        public static ResolutionFixture Create(
            params ConflictMatchRecord[] matches) =>
            Create("display", matches);

        private static ResolutionFixture Create(
            string displayName,
            params ConflictMatchRecord[] matches)
        {
            Guid itemId = Guid.NewGuid();
            var conflict = new ConflictCaseRecord(
                Guid.NewGuid(),
                itemId,
                ConflictCaseStatus.Open,
                new ConflictInputSnapshot(
                    "T:\\inbox\\photo.jpg",
                    3,
                    ObservedAt,
                    HashA,
                    "photo.jpg",
                    1),
                null,
                null,
                null,
                matches);
            var cases = new ResolutionCaseStore(conflict);
            var candidate = new IngestCandidate(
                itemId,
                InboxItemState.ConflictBlocked,
                MediaKind.Image,
                "photo.jpg",
                null,
                [new IngestSourceRecord(
                    "T:\\inbox\\photo.jpg",
                    3,
                    ObservedAt,
                    true,
                    true)],
                MetadataSelection.Create(displayName, [], null, []),
                8);
            var ingestStore = new ResolutionIngestStore(candidate);
            var inputResolver = new ResolutionInputResolver();
            var detector = new ResolutionDetector(matches);
            var storage = new ResolutionStorage(matches);
            var executor = new RecordingExecutor();
            var service = new ConflictResolutionService(
                cases,
                ingestStore,
                new ResolutionSettingsStore(),
                inputResolver,
                detector,
                storage,
                executor,
                new InboxActivityNotifier(),
                new MediaOperationCoordinator());
            return new ResolutionFixture(
                conflict,
                cases,
                detector,
                inputResolver,
                executor,
                service);
        }
    }

    private sealed class ResolutionCaseStore(ConflictCaseRecord conflict)
        : IConflictCaseStore
    {
        public bool WasSkipped { get; private set; }

        public bool WasChosen { get; private set; }

        public bool WasInvalidated { get; private set; }

        public Task<ConflictCaseRecord?> LoadAsync(
            Guid conflictCaseId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ConflictCaseRecord?>(
                conflictCaseId == conflict.Id ? conflict : null);

        public Task SkipAsync(
            Guid conflictCaseId,
            CancellationToken cancellationToken)
        {
            WasSkipped = true;
            return Task.CompletedTask;
        }

        public Task ChooseAsync(
            Guid conflictCaseId,
            ConflictResolution resolution,
            Guid? matchId,
            CancellationToken cancellationToken)
        {
            WasChosen = true;
            return Task.CompletedTask;
        }

        public Task InvalidateAsync(
            Guid conflictCaseId,
            string reason,
            CancellationToken cancellationToken)
        {
            WasInvalidated = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RegisteredConflictCandidate>>
            FindRegisteredCandidatesAsync(
                string targetFilename,
                string sha256,
                CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RegisteredConflictCandidate>>([]);

        public Task<ConflictCaseRecord> ReplaceOpenAsync(
            Guid inboxItemId,
            ConflictInputSnapshot input,
            IReadOnlyList<ConflictMatchRecord> matches,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ConflictCaseRecord?> LoadOpenForInboxAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ConflictCaseRecord?>(null);
    }

    private sealed class ResolutionDetector(
        IReadOnlyList<ConflictMatchRecord> matches) : IConflictDetectionService
    {
        public ConflictRevalidationResult Validation { get; set; } =
            ConflictRevalidationResult.Valid(matches);

        public ConflictCaseRecord? Redetected { get; set; }

        public Task<ConflictCaseRecord?> DetectAsync(
            IngestPreparation preparation,
            LibraryFolders folders,
            CancellationToken cancellationToken) =>
            Task.FromResult(Redetected);

        public Task<ConflictRevalidationResult> RevalidateAsync(
            ConflictCaseRecord conflict,
            IngestPreparation current,
            LibraryFolders folders,
            CancellationToken cancellationToken) =>
            Task.FromResult(Validation);
    }

    private sealed class ResolutionInputResolver : IIngestInputResolver
    {
        public int CallCount { get; private set; }

        public Task<PreparedIngestInput> ResolveAsync(
            IngestCandidate candidate,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new PreparedIngestInput(
                IngestCleanupKind.InboxSource,
                "T:\\inbox\\photo.jpg",
                3,
                ObservedAt,
                HashA,
                []));
        }
    }

    private sealed class ResolutionStorage(
        IReadOnlyList<ConflictMatchRecord> matches) : IControlledMediaStorage
    {
        public Task<LibraryFileSnapshot?> InspectTargetAsync(
            LibraryFolders folders,
            string targetFilename,
            CancellationToken cancellationToken)
        {
            ConflictMatchRecord? match = matches.SingleOrDefault(item =>
                StringComparer.OrdinalIgnoreCase.Equals(
                    item.ActualFilename,
                    targetFilename));
            return Task.FromResult(match is null
                ? null
                : new LibraryFileSnapshot(
                    $"T:\\library\\{match.RelativePath}",
                    match.RelativePath,
                    match.Size,
                    match.LastWriteTimeUtc,
                    match.Sha256));
        }

        public Task<bool> TargetExistsAsync(
            LibraryFolders folders,
            string targetFilename,
            CancellationToken cancellationToken) => Task.FromResult(true);

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

    private sealed class RecordingExecutor : IIngestExecutionService
    {
        public IngestPreparation? Preparation { get; private set; }

        public IngestExecutionPlan? Plan { get; private set; }

        public Task<IngestResult> ExecuteAsync(
            IngestPreparation preparation,
            IngestExecutionPlan plan,
            LibraryFolders folders,
            CancellationToken cancellationToken)
        {
            Preparation = preparation;
            Plan = plan;
            return Task.FromResult(new IngestResult(
                IngestOutcome.Completed,
                Guid.NewGuid(),
                plan.ReplacementAssetId ?? Guid.NewGuid(),
                plan.ConflictCaseId,
                "ingest.completed"));
        }
    }

    private sealed class ResolutionSettingsStore : IDirectorySettingsStore
    {
        public Task<LibraryFolders?> LoadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<LibraryFolders?>(Folders);

        public Task SaveAsync(
            LibraryFolders folders,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ResolutionIngestStore(IngestCandidate candidate)
        : IIngestStore
    {
        public Task<IngestCandidate?> LoadCandidateAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IngestCandidate?>(candidate);

        public Task<IngestOperationRecord?> FindActiveAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IngestOperationRecord?>(null);

        public Task<IngestOperationRecord> CreateAsync(
            IngestPreparation preparation,
            IngestExecutionPlan plan,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MarkStagedAsync(
            Guid operationId,
            StagedFile staged,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AbandonForRetryAsync(
            Guid operationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MarkFileCommittedAsync(
            Guid operationId,
            CommittedFile committed,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Guid> CommitAssetAsync(
            Guid operationId,
            CommittedFile committed,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MarkCleanupPendingAsync(
            Guid operationId,
            string errorCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CompleteAsync(
            Guid operationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task FailAsync(
            Guid operationId,
            string errorCode,
            bool isRetryable,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<IngestOperationRecord>> LoadIncompleteAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IngestOperationRecord>>([]);
    }
}
