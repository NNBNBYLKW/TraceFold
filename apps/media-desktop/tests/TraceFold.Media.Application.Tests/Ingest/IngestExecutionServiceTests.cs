using Microsoft.Extensions.Logging.Abstractions;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Ingest;
using Xunit;

namespace TraceFold.Media.Application.Tests.Ingest;

public sealed class IngestExecutionServiceTests
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
    public async Task Keep_both_uses_storage_selected_filename_and_keeps_display_name()
    {
        var store = new ExecutionStore();
        var storage = new ExecutionStorage
        {
            UniqueResult = Committed("photo (3).jpg")
        };
        var service = CreateService(store, storage);
        IngestPreparation preparation = Preparation("My display");
        IngestExecutionPlan plan = IngestExecutionPlan.KeepBoth(
            Guid.NewGuid(),
            "photo.jpg");

        IngestResult result = await service.ExecuteAsync(
            preparation,
            plan,
            Folders,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.Completed, result.Outcome);
        Assert.Equal("photo (3).jpg", store.Committed!.RelativePath);
        Assert.Equal("My display", store.Preparation!.DisplayName);
    }

    [Fact]
    public async Task Replace_registered_backs_up_before_committing_and_preserves_asset_id()
    {
        Guid assetId = Guid.NewGuid();
        var timeline = new List<string>();
        var store = new ExecutionStore
        {
            AssetId = assetId,
            Timeline = timeline
        };
        var storage = new ExecutionStorage { Timeline = timeline };
        var service = CreateService(store, storage);
        LibraryFileSnapshot target = Target();
        IngestExecutionPlan plan = IngestExecutionPlan.ReplaceRegistered(
            Guid.NewGuid(),
            assetId,
            4,
            target);

        IngestResult result = await service.ExecuteAsync(
            Preparation("new metadata"),
            plan,
            Folders,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.Completed, result.Outcome);
        Assert.Equal(assetId, result.AssetId);
        Assert.Equal(
            ["backup", "backup-persisted", "replace"],
            timeline.Take(3));
    }

    [Fact]
    public async Task Replacement_commit_failure_restores_verified_backup()
    {
        var store = new ExecutionStore();
        var storage = new ExecutionStorage
        {
            CommitReplacementError = new IOException("commit failed")
        };
        var service = CreateService(store, storage);

        IngestResult result = await service.ExecuteAsync(
            Preparation("new metadata"),
            IngestExecutionPlan.ReplaceUnregistered(
                Guid.NewGuid(),
                Target()),
            Folders,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.Failed, result.Outcome);
        Assert.True(storage.RestoreCalled);
        Assert.True(store.BackupRestored);
        Assert.False(store.RecoveryBlocked);
    }

    [Fact]
    public async Task Database_failure_restores_only_when_old_database_version_is_proven()
    {
        var store = new ExecutionStore
        {
            CommitAssetError = new InvalidOperationException("database failed"),
            ReplacementDatabaseMatchesExpected = true
        };
        var storage = new ExecutionStorage();
        var service = CreateService(store, storage);

        IngestResult result = await service.ExecuteAsync(
            Preparation("new metadata"),
            IngestExecutionPlan.ReplaceUnregistered(
                Guid.NewGuid(),
                Target()),
            Folders,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.Failed, result.Outcome);
        Assert.True(storage.RestoreCalled);
        Assert.True(store.BackupRestored);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Uncertain_database_or_restore_blocks_recovery(
        bool databaseCanRestore,
        bool restoreFails)
    {
        var store = new ExecutionStore
        {
            CommitAssetError = new InvalidOperationException("database failed"),
            ReplacementDatabaseMatchesExpected = databaseCanRestore
        };
        var storage = new ExecutionStorage
        {
            RestoreError = restoreFails
                ? new IOException("restore uncertain")
                : null
        };
        var service = CreateService(store, storage);

        IngestResult result = await service.ExecuteAsync(
            Preparation("new metadata"),
            IngestExecutionPlan.ReplaceUnregistered(
                Guid.NewGuid(),
                Target()),
            Folders,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.Failed, result.Outcome);
        Assert.True(store.RecoveryBlocked);
        Assert.False(store.BackupRestored);
    }

    [Fact]
    public async Task Executor_preserves_processed_input_cleanup_manifest()
    {
        var store = new ExecutionStore();
        var storage = new ExecutionStorage();
        var service = CreateService(store, storage);
        IngestPreparation preparation = Preparation("display") with
        {
            Input = new PreparedIngestInput(
                IngestCleanupKind.ProcessingOutput,
                "T:\\work\\.tracefold\\processing\\job\\photo.mp4",
                3,
                ObservedAt,
                HashA,
                [
                    new IngestCleanupFile(
                        IngestCleanupKind.InboxSource,
                        "T:\\inbox\\photo.mp4",
                        2,
                        HashA),
                    new IngestCleanupFile(
                        IngestCleanupKind.ProcessingOutput,
                        "T:\\work\\.tracefold\\processing\\job\\photo.mp4",
                        3,
                        HashA)
                ])
        };

        _ = await service.ExecuteAsync(
            preparation,
            IngestExecutionPlan.Create("photo.mp4"),
            Folders,
            TestContext.Current.CancellationToken);

        Assert.Equal(preparation.Input, store.Preparation!.Input);
    }

    private static IngestExecutionService CreateService(
        ExecutionStore store,
        ExecutionStorage storage) =>
        new(
            store,
            storage,
            new CompletedCleanup(),
            new NoOpIngestFaultInjector(),
            NullLogger<IngestExecutionService>.Instance);

    private static IngestPreparation Preparation(string displayName)
    {
        string source = "T:\\inbox\\photo.jpg";
        return new IngestPreparation(
            Guid.NewGuid(),
            MediaKind.Image,
            source,
            3,
            ObservedAt,
            HashA,
            "photo.jpg",
            displayName,
            [],
            null,
            [],
            5,
            new PreparedIngestInput(
                IngestCleanupKind.InboxSource,
                source,
                3,
                ObservedAt,
                HashA,
                [new IngestCleanupFile(
                    IngestCleanupKind.InboxSource,
                    source,
                    3,
                    HashA)]));
    }

    private static LibraryFileSnapshot Target() =>
        new(
            "T:\\library\\photo.jpg",
            "photo.jpg",
            2,
            ObservedAt,
            HashB);

    private static CommittedFile Committed(string filename = "photo.jpg") =>
        new(
            $"T:\\library\\{filename}",
            filename,
            3,
            HashA);

    private sealed class ExecutionStore : IIngestStore
    {
        private IngestOperationRecord? _operation;

        public IngestPreparation? Preparation { get; private set; }

        public IngestExecutionPlan? Plan { get; private set; }

        public CommittedFile? Committed { get; private set; }

        public Guid AssetId { get; set; } = Guid.NewGuid();

        public Exception? CommitAssetError { get; set; }

        public bool ReplacementDatabaseMatchesExpected { get; set; } = true;

        public bool BackupRestored { get; private set; }

        public bool RecoveryBlocked { get; private set; }

        public List<string>? Timeline { get; set; }

        public Task<IngestOperationRecord> CreateAsync(
            IngestPreparation preparation,
            IngestExecutionPlan plan,
            CancellationToken cancellationToken)
        {
            Preparation = preparation;
            Plan = plan;
            _operation = Operation(preparation, plan);
            return Task.FromResult(_operation);
        }

        public Task MarkBackupCreatedAsync(
            Guid operationId,
            ReplacementBackup backup,
            CancellationToken cancellationToken)
        {
            Timeline?.Add("backup-persisted");
            _operation = _operation! with
            {
                BackupState = IngestBackupState.Created,
                BackupRelativePath = backup.RelativePath,
                BackupSize = backup.Size,
                BackupSha256 = backup.Sha256
            };
            return Task.CompletedTask;
        }

        public Task MarkBackupRestoredAsync(
            Guid operationId,
            CancellationToken cancellationToken)
        {
            BackupRestored = true;
            return Task.CompletedTask;
        }

        public Task MarkRecoveryBlockedAsync(
            Guid operationId,
            string errorCode,
            CancellationToken cancellationToken)
        {
            RecoveryBlocked = true;
            return Task.CompletedTask;
        }

        public Task<bool> ReplacementDatabaseMatchesExpectedAsync(
            Guid operationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(ReplacementDatabaseMatchesExpected);

        public Task MarkStagedAsync(
            Guid operationId,
            StagedFile staged,
            CancellationToken cancellationToken)
        {
            _operation = _operation! with
            {
                Stage = IngestOperationStage.Staged,
                StagedPath = staged.FullPath
            };
            return Task.CompletedTask;
        }

        public Task MarkFileCommittedAsync(
            Guid operationId,
            CommittedFile committed,
            CancellationToken cancellationToken)
        {
            Committed = committed;
            _operation = _operation! with
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
            if (CommitAssetError is not null)
            {
                return Task.FromException<Guid>(CommitAssetError);
            }

            _operation = _operation! with
            {
                Stage = IngestOperationStage.DatabaseCommitted,
                AssetId = AssetId
            };
            return Task.FromResult(AssetId);
        }

        public Task FailAsync(
            Guid operationId,
            string errorCode,
            bool isRetryable,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkCleanupPendingAsync(
            Guid operationId,
            string errorCode,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IngestCandidate?> LoadCandidateAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IngestCandidate?>(null);

        public Task<IngestOperationRecord?> FindActiveAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) => Task.FromResult(_operation);

        public Task AbandonForRetryAsync(
            Guid operationId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CompleteAsync(
            Guid operationId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<IngestOperationRecord>> LoadIncompleteAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IngestOperationRecord>>([]);

        private static IngestOperationRecord Operation(
            IngestPreparation preparation,
            IngestExecutionPlan plan) =>
            new(
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
                plan.Target is null
                    ? IngestBackupState.NotRequired
                    : IngestBackupState.Pending,
                null,
                null,
                null,
                null,
                null,
                0,
                null);
    }

    private sealed class ExecutionStorage : IControlledMediaStorage
    {
        public CommittedFile UniqueResult { get; set; } = Committed();

        public Exception? CommitReplacementError { get; set; }

        public Exception? RestoreError { get; set; }

        public bool RestoreCalled { get; private set; }

        public List<string>? Timeline { get; set; }

        public Task<StagedFile> StageAsync(
            Guid operationId,
            LibraryFolders folders,
            string sourcePath,
            string expectedSha256,
            CancellationToken cancellationToken) =>
            Task.FromResult(new StagedFile(
                $"T:\\work\\{operationId:N}\\payload",
                3,
                HashA));

        public Task<StagedFile> StageAsync(
            Guid operationId,
            LibraryFolders folders,
            PreparedIngestInput input,
            CancellationToken cancellationToken) =>
            StageAsync(
                operationId,
                folders,
                input.FullPath,
                input.Sha256,
                cancellationToken);

        public Task<ReplacementBackup> BackupTargetAsync(
            Guid operationId,
            LibraryFolders folders,
            LibraryFileSnapshot expected,
            CancellationToken cancellationToken)
        {
            Timeline?.Add("backup");
            return Task.FromResult(new ReplacementBackup(
                $"T:\\library\\.tracefold\\recovery\\{operationId:N}\\original.jpg",
                $".tracefold\\recovery\\{operationId:N}\\original.jpg",
                expected.Size,
                expected.Sha256));
        }

        public Task<CommittedFile> CommitReplacementAsync(
            Guid operationId,
            LibraryFolders folders,
            StagedFile staged,
            LibraryFileSnapshot expectedTarget,
            CancellationToken cancellationToken)
        {
            Timeline?.Add("replace");
            return CommitReplacementError is null
                ? Task.FromResult(Committed(expectedTarget.RelativePath))
                : Task.FromException<CommittedFile>(CommitReplacementError);
        }

        public Task RestoreBackupAsync(
            Guid operationId,
            LibraryFolders folders,
            LibraryFileSnapshot target,
            ReplacementBackup backup,
            CancellationToken cancellationToken)
        {
            RestoreCalled = true;
            return RestoreError is null
                ? Task.CompletedTask
                : Task.FromException(RestoreError);
        }

        public Task<CommittedFile> CommitUniqueAsync(
            Guid operationId,
            LibraryFolders folders,
            StagedFile staged,
            string requestedFilename,
            CancellationToken cancellationToken) =>
            Task.FromResult(UniqueResult);

        public Task<CommittedFile> CommitAsync(
            Guid operationId,
            LibraryFolders folders,
            StagedFile staged,
            string targetFilename,
            CancellationToken cancellationToken) =>
            Task.FromResult(Committed(targetFilename));

        public Task<bool> VerifyCommittedAsync(
            LibraryFolders folders,
            CommittedFile committed,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> TargetExistsAsync(
            LibraryFolders folders,
            string targetFilename,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<SourceDeleteResult> DeleteSourceIfMatchesAsync(
            LibraryFolders folders,
            string sourcePath,
            long expectedSize,
            string expectedSha256,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SourceDeleteResult(SourceDeleteOutcome.Deleted));

        public Task CleanupTemporaryFilesAsync(
            Guid operationId,
            LibraryFolders folders,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CompletedCleanup : IIngestCleanupService
    {
        public Task<CleanupResult> CleanupAsync(
            IngestOperationRecord operation,
            LibraryFolders folders,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CleanupResult(true, null));
    }
}
