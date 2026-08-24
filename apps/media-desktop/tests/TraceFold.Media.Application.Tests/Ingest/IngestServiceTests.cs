using Microsoft.Extensions.Logging.Abstractions;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Application.Tests.Ingest;

public sealed class IngestServiceTests
{
    private const string HashA =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 24, 1, 2, 3, TimeSpan.Zero);
    private static readonly LibraryFolders Folders = new(
        "T:\\inbox",
        "T:\\library",
        "T:\\work");

    [Fact]
    public async Task Initial_ingest_returns_persisted_case_before_execution()
    {
        IngestFixture fixture = IngestFixture.Create();
        fixture.Detector.Result = Conflict(fixture.ItemId);

        IngestResult result = await fixture.Service.IngestAsync(
            fixture.ItemId,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.ConflictBlocked, result.Outcome);
        Assert.Equal(fixture.Detector.Result.Id, result.ConflictCaseId);
        Assert.Null(result.OperationId);
        Assert.Equal(0, fixture.Executor.CallCount);
    }

    [Fact]
    public async Task Ordinary_no_conflict_ingest_uses_the_single_execution_pipeline()
    {
        IngestFixture fixture = IngestFixture.Create();

        IngestResult result = await fixture.Service.IngestAsync(
            fixture.ItemId,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.Completed, result.Outcome);
        Assert.Equal(1, fixture.Executor.CallCount);
        Assert.Equal(IngestDisposition.Create, fixture.Executor.Plan!.Disposition);
        Assert.Equal("photo.jpg", fixture.Executor.Plan.RequestedTargetFilename);
        Assert.Equal(HashA, fixture.Executor.Preparation!.Input.Sha256);
    }

    private static ConflictCaseRecord Conflict(Guid inboxItemId)
    {
        var match = new ConflictMatchRecord(
            Guid.NewGuid(),
            ConflictReason.Name,
            false,
            null,
            null,
            "photo.jpg",
            "photo.jpg",
            null,
            MediaKind.Image,
            2,
            ObservedAt,
            new string('B', 64),
            new ConflictMetadataSnapshot(null, [], null, []),
            new ConflictTechnicalMetadata("jpg", null, null, null));
        return new ConflictCaseRecord(
            Guid.NewGuid(),
            inboxItemId,
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
            [match]);
    }

    private sealed class IngestFixture
    {
        private IngestFixture(
            Guid itemId,
            DetectionStub detector,
            ExecutionStub executor,
            IngestService service)
        {
            ItemId = itemId;
            Detector = detector;
            Executor = executor;
            Service = service;
        }

        public Guid ItemId { get; }

        public DetectionStub Detector { get; }

        public ExecutionStub Executor { get; }

        public IngestService Service { get; }

        public static IngestFixture Create()
        {
            Guid itemId = Guid.NewGuid();
            var candidate = new IngestCandidate(
                itemId,
                InboxItemState.ReadyToTag,
                MediaKind.Image,
                "photo.jpg",
                null,
                [new IngestSourceRecord(
                    "T:\\inbox\\photo.jpg",
                    3,
                    ObservedAt,
                    true,
                    true)],
                MetadataSelection.Create("display", [], null, []),
                1);
            var detector = new DetectionStub();
            var executor = new ExecutionStub();
            var service = new IngestService(
                new CompletedScanner(),
                new CandidateStore(candidate),
                new FixedSettingsStore(),
                new InputResolver(),
                detector,
                executor,
                new MediaOperationCoordinator(),
                NullLogger<IngestService>.Instance);
            return new IngestFixture(itemId, detector, executor, service);
        }
    }

    private sealed class DetectionStub : IConflictDetectionService
    {
        public ConflictCaseRecord? Result { get; set; }

        public Task<ConflictCaseRecord?> DetectAsync(
            IngestPreparation preparation,
            LibraryFolders folders,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result);

        public Task<ConflictRevalidationResult> RevalidateAsync(
            ConflictCaseRecord conflict,
            IngestPreparation current,
            LibraryFolders folders,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ExecutionStub : IIngestExecutionService
    {
        public int CallCount { get; private set; }

        public IngestPreparation? Preparation { get; private set; }

        public IngestExecutionPlan? Plan { get; private set; }

        public Task<IngestResult> ExecuteAsync(
            IngestPreparation preparation,
            IngestExecutionPlan plan,
            LibraryFolders folders,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Preparation = preparation;
            Plan = plan;
            return Task.FromResult(new IngestResult(
                IngestOutcome.Completed,
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                "ingest.completed"));
        }
    }

    private sealed class CandidateStore(IngestCandidate candidate) : IIngestStore
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

    private sealed class InputResolver : IIngestInputResolver
    {
        public Task<PreparedIngestInput> ResolveAsync(
            IngestCandidate candidate,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PreparedIngestInput(
                IngestCleanupKind.InboxSource,
                "T:\\inbox\\photo.jpg",
                3,
                ObservedAt,
                HashA,
                []));
    }

    private sealed class FixedSettingsStore : IDirectorySettingsStore
    {
        public Task<LibraryFolders?> LoadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<LibraryFolders?>(Folders);

        public Task SaveAsync(
            LibraryFolders folders,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CompletedScanner : IInboxScanner
    {
        public Task<ScanResult> ScanAsync(
            ScanTrigger trigger,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ScanResult(
                ScanOutcome.Completed,
                1,
                0,
                1,
                DateTimeOffset.UtcNow,
                "ok"));
    }
}
