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

public sealed class IngestFaultInjectionTests
{
    private const string NewHash =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string OldHash =
        "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private static readonly LibraryFolders Folders = new(
        "T:\\inbox",
        "T:\\library",
        "T:\\work");

    public static TheoryData<IngestFaultPoint> ExecutionFaultPoints => new()
    {
        IngestFaultPoint.AfterOperationCreated,
        IngestFaultPoint.AfterStaged,
        IngestFaultPoint.AfterBackupCreated,
        IngestFaultPoint.AfterFileCommitted,
        IngestFaultPoint.BeforeDatabaseCommit,
        IngestFaultPoint.AfterDatabaseCommit,
        IngestFaultPoint.BeforeCleanup,
        IngestFaultPoint.AfterCleanupEntry,
        IngestFaultPoint.AfterCleanup
    };

    [Fact]
    public async Task Replacement_reports_fault_points_only_after_corresponding_evidence_is_persisted()
    {
        var store = new FaultStore();
        var storage = new FaultStorage();
        var injector = new RecordingInjector(point =>
            AssertPersistedEvidence(point, store, storage));
        var cleanup = new IngestCleanupService(
            store,
            storage,
            logger: null,
            faultInjector: injector);
        var executor = new IngestExecutionService(
            store,
            storage,
            cleanup,
            injector,
            NullLogger<IngestExecutionService>.Instance);

        IngestResult result = await executor.ExecuteAsync(
            Preparation(),
            ReplacementPlan(),
            Folders,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.Completed, result.Outcome);
        Assert.Equal(
            [
                IngestFaultPoint.AfterOperationCreated,
                IngestFaultPoint.AfterStaged,
                IngestFaultPoint.AfterBackupCreated,
                IngestFaultPoint.AfterFileCommitted,
                IngestFaultPoint.BeforeDatabaseCommit,
                IngestFaultPoint.AfterDatabaseCommit,
                IngestFaultPoint.BeforeCleanup,
                IngestFaultPoint.AfterCleanupEntry,
                IngestFaultPoint.AfterCleanup
            ],
            injector.Seen);
    }

    [Theory]
    [MemberData(nameof(ExecutionFaultPoints))]
    public async Task Throwing_at_each_execution_fault_point_preserves_the_last_persisted_boundary(
        IngestFaultPoint faultPoint)
    {
        var store = new FaultStore();
        var storage = new FaultStorage();
        var injector = new RecordingInjector(point =>
        {
            AssertPersistedEvidence(point, store, storage);
            if (point == faultPoint)
            {
                throw new InjectedFaultException(point);
            }
        });
        var cleanup = new IngestCleanupService(
            store,
            storage,
            logger: null,
            faultInjector: injector);
        var executor = new IngestExecutionService(
            store,
            storage,
            cleanup,
            injector,
            NullLogger<IngestExecutionService>.Instance);

        InjectedFaultException error = await Assert.ThrowsAsync<InjectedFaultException>(
            () => executor.ExecuteAsync(
                Preparation(),
                ReplacementPlan(),
                Folders,
                TestContext.Current.CancellationToken));

        Assert.Equal(faultPoint, error.Point);
        if (faultPoint is IngestFaultPoint.BeforeDatabaseCommit
            or IngestFaultPoint.AfterDatabaseCommit
            or IngestFaultPoint.BeforeCleanup)
        {
            Assert.True(storage.SourcePresent);
            Assert.True(storage.BackupPresent);
            Assert.True(storage.NewTargetPresent);
        }

        if (faultPoint >= IngestFaultPoint.AfterDatabaseCommit)
        {
            Assert.Equal(IngestOperationStage.DatabaseCommitted, store.Operation!.Stage);
            Assert.NotNull(store.Operation.AssetId);
        }
    }

    private static void AssertPersistedEvidence(
        IngestFaultPoint point,
        FaultStore store,
        FaultStorage storage)
    {
        Assert.NotNull(store.Operation);
        switch (point)
        {
            case IngestFaultPoint.AfterOperationCreated:
                Assert.Equal(IngestOperationStage.Created, store.Operation.Stage);
                break;
            case IngestFaultPoint.AfterStaged:
                Assert.Equal(IngestOperationStage.Staged, store.Operation.Stage);
                Assert.NotNull(store.Operation.StagedPath);
                break;
            case IngestFaultPoint.AfterBackupCreated:
                Assert.Equal(IngestBackupState.Created, store.Operation.BackupState);
                Assert.NotNull(store.Operation.BackupRelativePath);
                Assert.True(storage.BackupPresent);
                break;
            case IngestFaultPoint.AfterFileCommitted:
            case IngestFaultPoint.BeforeDatabaseCommit:
                Assert.Equal(IngestOperationStage.FileCommitted, store.Operation.Stage);
                Assert.True(storage.NewTargetPresent);
                break;
            case IngestFaultPoint.AfterDatabaseCommit:
            case IngestFaultPoint.BeforeCleanup:
                Assert.Equal(IngestOperationStage.DatabaseCommitted, store.Operation.Stage);
                Assert.NotNull(store.Operation.AssetId);
                Assert.False(store.CleanupCompleted);
                break;
            case IngestFaultPoint.AfterCleanupEntry:
                Assert.True(store.CleanupEntryCompleted);
                Assert.False(store.CleanupCompleted);
                break;
            case IngestFaultPoint.AfterCleanup:
                Assert.True(store.CleanupCompleted);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(point));
        }
    }

    private static IngestPreparation Preparation()
    {
        string source = "T:\\inbox\\photo.jpg";
        return new IngestPreparation(
            Guid.NewGuid(),
            MediaKind.Image,
            source,
            3,
            DateTimeOffset.UtcNow,
            NewHash,
            "photo.jpg",
            null,
            [],
            null,
            [],
            0,
            new PreparedIngestInput(
                IngestCleanupKind.InboxSource,
                source,
                3,
                DateTimeOffset.UtcNow,
                NewHash,
                [new IngestCleanupFile(
                    IngestCleanupKind.InboxSource,
                    source,
                    3,
                    NewHash)]));
    }

    private static IngestExecutionPlan ReplacementPlan()
    {
        var target = new LibraryFileSnapshot(
            "T:\\library\\photo.jpg",
            "photo.jpg",
            2,
            DateTimeOffset.UtcNow,
            OldHash);
        return IngestExecutionPlan.ReplaceUnregistered(Guid.NewGuid(), target);
    }

    private sealed class RecordingInjector(Action<IngestFaultPoint> onReach)
        : IIngestFaultInjector
    {
        public List<IngestFaultPoint> Seen { get; } = [];

        public Task ReachAsync(
            IngestFaultPoint point,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.NotEqual(Guid.Empty, operationId);
            Seen.Add(point);
            onReach(point);
            return Task.CompletedTask;
        }
    }

    private sealed class InjectedFaultException(IngestFaultPoint point)
        : Exception(point.ToString())
    {
        public IngestFaultPoint Point { get; } = point;
    }

    private sealed class FaultStorage : IControlledMediaStorage
    {
        public bool SourcePresent { get; private set; } = true;

        public bool BackupPresent { get; private set; }

        public bool NewTargetPresent { get; private set; }

        public Task<StagedFile> StageAsync(
            Guid operationId,
            LibraryFolders folders,
            string sourcePath,
            string expectedSha256,
            CancellationToken cancellationToken) =>
            Task.FromResult(new StagedFile(
                $"T:\\work\\.tracefold\\ingest\\{operationId:N}\\payload",
                3,
                NewHash));

        public Task<ReplacementBackup> BackupTargetAsync(
            Guid operationId,
            LibraryFolders folders,
            LibraryFileSnapshot expected,
            CancellationToken cancellationToken)
        {
            BackupPresent = true;
            return Task.FromResult(new ReplacementBackup(
                $"T:\\library\\.tracefold\\recovery\\{operationId:N}\\original.jpg",
                $".tracefold\\recovery\\{operationId:N}\\original.jpg",
                2,
                OldHash));
        }

        public Task<CommittedFile> CommitReplacementAsync(
            Guid operationId,
            LibraryFolders folders,
            StagedFile staged,
            LibraryFileSnapshot expectedTarget,
            CancellationToken cancellationToken)
        {
            NewTargetPresent = true;
            return Task.FromResult(new CommittedFile(
                expectedTarget.FullPath,
                expectedTarget.RelativePath,
                3,
                NewHash));
        }

        public Task<bool> VerifyCommittedAsync(
            LibraryFolders folders,
            CommittedFile committed,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> TargetExistsAsync(
            LibraryFolders folders,
            string targetFilename,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CommittedFile> CommitAsync(
            Guid operationId,
            LibraryFolders folders,
            StagedFile staged,
            string targetFilename,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SourceDeleteResult> DeleteSourceIfMatchesAsync(
            LibraryFolders folders,
            string sourcePath,
            long expectedSize,
            string expectedSha256,
            CancellationToken cancellationToken)
        {
            SourcePresent = false;
            return Task.FromResult(new SourceDeleteResult(
                SourceDeleteOutcome.Deleted));
        }

        public Task<SourceDeleteResult> DeleteCleanupFileIfMatchesAsync(
            LibraryFolders folders,
            IngestCleanupFile cleanupFile,
            CancellationToken cancellationToken) =>
            DeleteSourceIfMatchesAsync(
                folders,
                cleanupFile.FullPath,
                cleanupFile.Size,
                cleanupFile.Sha256,
                cancellationToken);

        public Task CleanupTemporaryFilesAsync(
            Guid operationId,
            LibraryFolders folders,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FaultStore : IIngestStore
    {
        public IngestOperationRecord? Operation { get; private set; }

        public bool CleanupCompleted { get; set; }

        public bool CleanupEntryCompleted { get; private set; }

        private Guid CleanupEntryId { get; } = Guid.NewGuid();

        public Task<IngestOperationRecord> CreateAsync(
            IngestPreparation preparation,
            IngestExecutionPlan plan,
            CancellationToken cancellationToken)
        {
            Operation = new IngestOperationRecord(
                Guid.NewGuid(),
                preparation.InboxItemId,
                IngestOperationStage.Created,
                plan.Disposition,
                plan.ConflictCaseId,
                preparation.Input.FullPath,
                preparation.Input.Size,
                preparation.Input.LastWriteTimeUtc,
                preparation.Input.Sha256,
                plan.RequestedTargetFilename,
                null,
                null,
                plan.ReplacementAssetId,
                plan.ReplacementMetadataVersion,
                plan.Target?.RelativePath,
                plan.Target?.Size,
                plan.Target?.LastWriteTimeUtc,
                plan.Target?.Sha256,
                IngestBackupState.Pending,
                null,
                null,
                null,
                null,
                null,
                0,
                null);
            return Task.FromResult(Operation);
        }

        public Task MarkStagedAsync(
            Guid operationId,
            StagedFile staged,
            CancellationToken cancellationToken)
        {
            Operation = Operation! with
            {
                Stage = IngestOperationStage.Staged,
                StagedPath = staged.FullPath
            };
            return Task.CompletedTask;
        }

        public Task MarkBackupCreatedAsync(
            Guid operationId,
            ReplacementBackup backup,
            CancellationToken cancellationToken)
        {
            Operation = Operation! with
            {
                BackupState = IngestBackupState.Created,
                BackupRelativePath = backup.RelativePath,
                BackupSize = backup.Size,
                BackupSha256 = backup.Sha256
            };
            return Task.CompletedTask;
        }

        public Task MarkFileCommittedAsync(
            Guid operationId,
            CommittedFile committed,
            CancellationToken cancellationToken)
        {
            Operation = Operation! with
            {
                Stage = IngestOperationStage.FileCommitted,
                TargetFilename = committed.RelativePath
            };
            return Task.CompletedTask;
        }

        public Task<Guid> CommitAssetAsync(
            Guid operationId,
            CommittedFile committed,
            CancellationToken cancellationToken)
        {
            Guid assetId = Guid.NewGuid();
            Operation = Operation! with
            {
                Stage = IngestOperationStage.DatabaseCommitted,
                AssetId = assetId
            };
            return Task.FromResult(assetId);
        }

        public Task<IngestCandidate?> LoadCandidateAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IngestOperationRecord?> FindActiveAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) => Task.FromResult(Operation);

        public Task AbandonForRetryAsync(
            Guid operationId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkCleanupPendingAsync(
            Guid operationId,
            string errorCode,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CompleteAsync(
            Guid operationId,
            CancellationToken cancellationToken)
        {
            CleanupCompleted = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IngestCleanupEntryRecord>> LoadCleanupEntriesAsync(
            Guid operationId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IngestCleanupEntryRecord>>(
            [
                new(
                    CleanupEntryId,
                    operationId,
                    new IngestCleanupFile(
                        IngestCleanupKind.InboxSource,
                        Operation!.SourcePath,
                        Operation.SourceSize,
                        Operation.SourceSha256),
                    CleanupEntryCompleted)
            ]);

        public Task MarkCleanupEntryCompletedAsync(
            Guid operationId,
            Guid entryId,
            CancellationToken cancellationToken)
        {
            Assert.Equal(CleanupEntryId, entryId);
            CleanupEntryCompleted = true;
            return Task.CompletedTask;
        }

        public Task FailAsync(
            Guid operationId,
            string errorCode,
            bool isRetryable,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<IngestOperationRecord>> LoadIncompleteAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IngestOperationRecord>>([]);
    }
}
