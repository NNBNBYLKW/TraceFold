using Microsoft.Extensions.Logging.Abstractions;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Ingest;
using Xunit;

namespace TraceFold.Media.Application.Tests.Ingest;

public sealed class IngestRecoveryServiceTests
{
    [Fact]
    public async Task FileCommitted_with_matching_target_completes_database_commit()
    {
        RecoveryFixture fixture = RecoveryFixture.FileCommitted(targetMatches: true);

        await fixture.Service.ReconcileAsync(TestContext.Current.CancellationToken);

        Assert.True(fixture.Store.AssetCommitted);
        Assert.True(fixture.Storage.SourceDeleted);
        Assert.Equal(IngestOperationStage.Completed, fixture.Store.Stage);
    }

    [Fact]
    public async Task Unknown_target_identity_preserves_source_and_reports_failure()
    {
        RecoveryFixture fixture = RecoveryFixture.FileCommitted(targetMatches: false);

        await fixture.Service.ReconcileAsync(TestContext.Current.CancellationToken);

        Assert.False(fixture.Storage.SourceDeleted);
        Assert.Equal(IngestOperationStage.Failed, fixture.Store.Stage);
        Assert.Equal("recovery.target_identity_unknown", fixture.Store.ErrorCode);
    }

    private sealed class RecoveryFixture
    {
        private RecoveryFixture(
            RecoveryStore store,
            RecoveryStorage storage,
            IngestRecoveryService service)
        {
            Store = store;
            Storage = storage;
            Service = service;
        }

        public RecoveryStore Store { get; }

        public RecoveryStorage Storage { get; }

        public IngestRecoveryService Service { get; }

        public static RecoveryFixture FileCommitted(bool targetMatches)
        {
            var operation = new IngestOperationRecord(
                Guid.NewGuid(),
                Guid.NewGuid(),
                IngestOperationStage.FileCommitted,
                IngestDisposition.Create,
                null,
                "C:/inbox/photo.jpg",
                3,
                DateTimeOffset.Parse("2026-08-22T10:00:00Z"),
                new string('A', 64),
                "photo.jpg",
                "C:/work/payload",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                IngestBackupState.NotRequired,
                null,
                null,
                null,
                null,
                null,
                0,
                null);
            var store = new RecoveryStore(operation);
            var storage = new RecoveryStorage(store, targetMatches);
            var service = new IngestRecoveryService(
                store,
                new FixedSettingsStore(
                    new LibraryFolders("C:/inbox", "C:/library", "C:/work")),
                storage,
                new FixedHasher(),
                NullLogger<IngestRecoveryService>.Instance);
            return new RecoveryFixture(store, storage, service);
        }
    }

    private sealed class RecoveryStore(IngestOperationRecord operation) : IIngestStore
    {
        private IngestOperationRecord _operation = operation;

        public IngestOperationStage Stage => _operation.Stage;

        public string? ErrorCode => _operation.ErrorCode;

        public bool AssetCommitted { get; private set; }

        public Task<IReadOnlyList<IngestOperationRecord>> LoadIncompleteAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IngestOperationRecord>>([_operation]);

        public Task<IngestOperationRecord?> FindActiveAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IngestOperationRecord?>(_operation);

        public Task<Guid> CommitAssetAsync(
            Guid operationId,
            CommittedFile committed,
            CancellationToken cancellationToken)
        {
            AssetCommitted = true;
            Guid assetId = Guid.NewGuid();
            _operation = _operation with
            {
                Stage = IngestOperationStage.DatabaseCommitted,
                AssetId = assetId
            };
            return Task.FromResult(assetId);
        }

        public Task CompleteAsync(
            Guid operationId,
            CancellationToken cancellationToken)
        {
            _operation = _operation with { Stage = IngestOperationStage.Completed };
            return Task.CompletedTask;
        }

        public Task FailAsync(
            Guid operationId,
            string errorCode,
            bool isRetryable,
            CancellationToken cancellationToken)
        {
            _operation = _operation with
            {
                Stage = IngestOperationStage.Failed,
                ErrorCode = errorCode
            };
            return Task.CompletedTask;
        }

        public Task<IngestCandidate?> LoadCandidateAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IngestCandidate?>(null);

        public Task<ConflictCheck> CheckConflictsAsync(
            string targetFilename,
            string sourceSha256,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ConflictCheck(null));

        public Task BlockConflictAsync(
            Guid inboxItemId,
            ConflictKind kind,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IngestOperationRecord> CreateAsync(
            IngestPreparation preparation,
            CancellationToken cancellationToken) => Task.FromResult(_operation);

        public Task MarkStagedAsync(
            Guid operationId,
            StagedFile staged,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AbandonForRetryAsync(
            Guid operationId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkFileCommittedAsync(
            Guid operationId,
            CommittedFile committed,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkCleanupPendingAsync(
            Guid operationId,
            string errorCode,
            CancellationToken cancellationToken)
        {
            _operation = _operation with
            {
                Stage = IngestOperationStage.CleanupPending,
                ErrorCode = errorCode
            };
            return Task.CompletedTask;
        }
    }

    private sealed class RecoveryStorage(
        RecoveryStore store,
        bool targetMatches) : IControlledMediaStorage
    {
        public bool SourceDeleted { get; private set; }

        public Task<bool> VerifyCommittedAsync(
            LibraryFolders folders,
            CommittedFile committed,
            CancellationToken cancellationToken) => Task.FromResult(targetMatches);

        public Task<SourceDeleteResult> DeleteSourceIfMatchesAsync(
            LibraryFolders folders,
            string sourcePath,
            long expectedSize,
            string expectedSha256,
            CancellationToken cancellationToken)
        {
            Assert.True(store.AssetCommitted);
            SourceDeleted = true;
            return Task.FromResult(new SourceDeleteResult(SourceDeleteOutcome.Deleted));
        }

        public Task CleanupTemporaryFilesAsync(
            Guid operationId,
            LibraryFolders folders,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> TargetExistsAsync(
            LibraryFolders folders,
            string targetFilename,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<StagedFile> StageAsync(
            Guid operationId,
            LibraryFolders folders,
            string sourcePath,
            string expectedSha256,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CommittedFile> CommitAsync(
            Guid operationId,
            LibraryFolders folders,
            StagedFile staged,
            string targetFilename,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedSettingsStore(LibraryFolders folders)
        : IDirectorySettingsStore
    {
        public Task<LibraryFolders?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<LibraryFolders?>(folders);

        public Task SaveAsync(
            LibraryFolders value,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedHasher : IFileHasher
    {
        public Task<string> ComputeSha256Async(
            string path,
            CancellationToken cancellationToken) =>
            Task.FromResult(new string('A', 64));
    }
}
