using Microsoft.Extensions.Logging.Abstractions;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Ingest;
using Xunit;

namespace TraceFold.Media.Application.Tests.Ingest;

public sealed class IngestRecoveryServiceTests
{
    private static readonly LibraryFolders Folders = new(
        "C:/inbox",
        "C:/library",
        "C:/work");

    [Fact]
    public async Task File_committed_with_verified_target_commits_once_and_reconciles_idempotently()
    {
        IngestOperationRecord operation = Operation(IngestOperationStage.FileCommitted);
        var fixture = new RecoveryFixture(
            operation,
            Evidence(
                source: FileIdentityKind.ExpectedSource,
                staged: FileIdentityKind.ExpectedStaged,
                target: FileIdentityKind.ExpectedNew));

        await fixture.Service.ReconcileAsync(TestContext.Current.CancellationToken);
        Guid firstAssetId = fixture.Store.Saved.AssetId!.Value;
        await fixture.Service.ReconcileAsync(TestContext.Current.CancellationToken);

        Assert.Equal(IngestOperationStage.Completed, fixture.Store.Saved.Stage);
        Assert.Equal(firstAssetId, fixture.Store.Saved.AssetId);
        Assert.Equal(1, fixture.Store.AssetCommitCount);
        Assert.Equal(1, fixture.Cleanup.Calls);
    }

    [Fact]
    public async Task Unknown_target_identity_is_blocked_without_storage_mutation()
    {
        IngestOperationRecord operation = Operation(IngestOperationStage.FileCommitted);
        var fixture = new RecoveryFixture(
            operation,
            Evidence(target: FileIdentityKind.Unknown));

        await fixture.Service.ReconcileAsync(TestContext.Current.CancellationToken);

        Assert.Equal(IngestOperationStage.RecoveryBlocked, fixture.Store.Saved.Stage);
        Assert.Equal(
            IngestOperationStage.FileCommitted,
            fixture.Store.Saved.RecoveryBlockedFromStage);
        Assert.Equal("recovery.identity_ambiguous", fixture.Store.Saved.ErrorCode);
        Assert.Equal(0, fixture.Storage.MutationCount);
        Assert.Equal(0, fixture.Store.AssetCommitCount);
    }

    [Fact]
    public async Task Database_committed_without_operation_asset_id_is_blocked()
    {
        IngestOperationRecord operation = Operation(
            IngestOperationStage.DatabaseCommitted) with
        {
            AssetId = null
        };
        var fixture = new RecoveryFixture(
            operation,
            Evidence(target: FileIdentityKind.ExpectedNew));
        fixture.Store.DatabaseHasNewVersion = true;

        await fixture.Service.ReconcileAsync(TestContext.Current.CancellationToken);

        Assert.Equal(IngestOperationStage.RecoveryBlocked, fixture.Store.Saved.Stage);
        Assert.Equal("recovery.asset_identity_missing", fixture.Store.Saved.ErrorCode);
        Assert.Equal(0, fixture.Storage.MutationCount);
        Assert.Equal(0, fixture.Cleanup.Calls);
    }

    [Fact]
    public async Task One_blocked_operation_does_not_prevent_a_safe_resume()
    {
        IngestOperationRecord blocked = Operation(
            IngestOperationStage.FileCommitted,
            Guid.NewGuid());
        IngestOperationRecord resumable = Operation(
            IngestOperationStage.Staged,
            Guid.NewGuid());
        var store = new RecoveryStore([blocked, resumable]);
        var storage = new RecoveryStorage(new Dictionary<Guid, IngestStorageEvidence>
        {
            [blocked.Id] = Evidence(target: FileIdentityKind.Unknown),
            [resumable.Id] = Evidence(
                source: FileIdentityKind.ExpectedSource,
                staged: FileIdentityKind.ExpectedStaged,
                target: FileIdentityKind.Absent)
        });
        var executor = new RecoveryExecutor(store);
        var cleanup = new RecoveryCleanup(store);
        var service = CreateService(store, storage, executor, cleanup);

        await service.ReconcileAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            IngestOperationStage.RecoveryBlocked,
            store.Get(blocked.Id).Stage);
        Assert.Equal(IngestOperationStage.Completed, store.Get(resumable.Id).Stage);
        Assert.Equal(1, executor.ResumeCalls);
    }

    [Fact]
    public async Task Retry_rechecks_blocked_evidence_before_resuming()
    {
        IngestOperationRecord operation = Operation(
            IngestOperationStage.RecoveryBlocked) with
        {
            RecoveryBlockedFromStage = IngestOperationStage.Staged,
            ErrorCode = "recovery.identity_ambiguous"
        };
        var fixture = new RecoveryFixture(
            operation,
            Evidence(
                source: FileIdentityKind.ExpectedSource,
                staged: FileIdentityKind.ExpectedStaged,
                target: FileIdentityKind.Absent));

        RecoveryResult result = await fixture.Service.RetryAsync(
            operation.InboxItemId,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(IngestOperationStage.Completed, fixture.Store.Saved.Stage);
        Assert.Equal(1, fixture.Executor.ResumeCalls);
        Assert.Equal(1, fixture.Store.ResumeRecoveryCount);
    }

    [Fact]
    public async Task Manual_cleanup_retry_ignores_future_due_time_and_never_resumes_execution()
    {
        IngestOperationRecord operation = Operation(
            IngestOperationStage.CleanupPending) with
        {
            CleanupAttemptCount = 3,
            NextCleanupAttemptAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        var fixture = new RecoveryFixture(
            operation,
            Evidence(target: FileIdentityKind.ExpectedNew));

        RecoveryResult result = await fixture.Service.RetryCleanupAsync(
            operation.InboxItemId,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(1, fixture.Store.ResetCleanupCount);
        Assert.Equal(1, fixture.Cleanup.Calls);
        Assert.Equal(0, fixture.Executor.ResumeCalls);
    }

    private static IngestRecoveryService CreateService(
        RecoveryStore store,
        RecoveryStorage storage,
        RecoveryExecutor executor,
        RecoveryCleanup cleanup) =>
        new(
            store,
            new FixedSettingsStore(),
            storage,
            executor,
            NullLogger<IngestRecoveryService>.Instance,
            cleanup,
            store);

    private sealed class RecoveryFixture
    {
        public RecoveryFixture(
            IngestOperationRecord operation,
            IngestStorageEvidence evidence)
        {
            Store = new RecoveryStore([operation]);
            Storage = new RecoveryStorage(new Dictionary<Guid, IngestStorageEvidence>
            {
                [operation.Id] = evidence
            });
            Executor = new RecoveryExecutor(Store);
            Cleanup = new RecoveryCleanup(Store);
            Service = CreateService(Store, Storage, Executor, Cleanup);
        }

        public RecoveryStore Store { get; }
        public RecoveryStorage Storage { get; }
        public RecoveryExecutor Executor { get; }
        public RecoveryCleanup Cleanup { get; }
        public IngestRecoveryService Service { get; }
    }

    private sealed class RecoveryStore(IEnumerable<IngestOperationRecord> supplied)
        : IIngestStore, IIngestCleanupWorkStore
    {
        private readonly Dictionary<Guid, IngestOperationRecord> operations =
            supplied.ToDictionary(operation => operation.Id);

        public IngestOperationRecord Saved => Assert.Single(operations.Values);
        public int AssetCommitCount { get; private set; }
        public int ResumeRecoveryCount { get; private set; }
        public bool DatabaseHasNewVersion { get; set; }
        public int ResetCleanupCount { get; private set; }

        public IngestOperationRecord Get(Guid operationId) => operations[operationId];

        public Task<IReadOnlyList<IngestOperationRecord>> LoadIncompleteAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IngestOperationRecord>>(
                operations.Values
                    .Where(operation => operation.Stage is not
                        (IngestOperationStage.Completed or IngestOperationStage.Failed))
                    .ToArray());

        public Task<IngestOperationRecord?> FindActiveAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IngestOperationRecord?>(operations.Values
                .SingleOrDefault(operation => operation.InboxItemId == inboxItemId));

        public Task<bool> OperationDatabaseHasNewVersionAsync(
            Guid operationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                DatabaseHasNewVersion
                || operations[operationId].Stage is
                    IngestOperationStage.DatabaseCommitted
                    or IngestOperationStage.CleanupPending
                    or IngestOperationStage.Completed);

        public Task<Guid> CommitAssetAsync(
            Guid operationId,
            CommittedFile committed,
            CancellationToken cancellationToken)
        {
            IngestOperationRecord operation = operations[operationId];
            if (operation.AssetId is Guid existing)
            {
                return Task.FromResult(existing);
            }

            Guid assetId = Guid.NewGuid();
            AssetCommitCount++;
            operations[operationId] = operation with
            {
                Stage = IngestOperationStage.DatabaseCommitted,
                AssetId = assetId
            };
            return Task.FromResult(assetId);
        }

        public Task MarkRecoveryBlockedAsync(
            Guid operationId,
            string errorCode,
            CancellationToken cancellationToken)
        {
            IngestOperationRecord operation = operations[operationId];
            operations[operationId] = operation with
            {
                Stage = IngestOperationStage.RecoveryBlocked,
                RecoveryBlockedFromStage = operation.Stage,
                ErrorCode = errorCode
            };
            return Task.CompletedTask;
        }

        public Task ResumeRecoveryAsync(
            Guid operationId,
            CancellationToken cancellationToken)
        {
            IngestOperationRecord operation = operations[operationId];
            operations[operationId] = operation with
            {
                Stage = operation.RecoveryBlockedFromStage
                    ?? throw new InvalidOperationException(),
                RecoveryBlockedFromStage = null,
                ErrorCode = null
            };
            ResumeRecoveryCount++;
            return Task.CompletedTask;
        }

        public Task MarkBackupRestoredAsync(
            Guid operationId,
            CancellationToken cancellationToken)
        {
            operations[operationId] = operations[operationId] with
            {
                BackupState = IngestBackupState.Restored
            };
            return Task.CompletedTask;
        }

        public Task AbandonForRetryAsync(
            Guid operationId,
            CancellationToken cancellationToken)
        {
            operations[operationId] = operations[operationId] with
            {
                Stage = IngestOperationStage.Failed,
                ErrorCode = "recovery.abandoned_for_retry"
            };
            return Task.CompletedTask;
        }

        public Task CompleteAsync(Guid operationId, CancellationToken cancellationToken)
        {
            operations[operationId] = operations[operationId] with
            {
                Stage = IngestOperationStage.Completed
            };
            return Task.CompletedTask;
        }

        public void CompleteFromResume(Guid operationId) =>
            operations[operationId] = operations[operationId] with
            {
                Stage = IngestOperationStage.Completed,
                AssetId = operations[operationId].AssetId ?? Guid.NewGuid()
            };

        public Task<IngestCandidate?> LoadCandidateAsync(Guid inboxItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IngestOperationRecord> CreateAsync(IngestPreparation preparation, IngestExecutionPlan plan, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkStagedAsync(Guid operationId, StagedFile staged, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkFileCommittedAsync(Guid operationId, CommittedFile committed, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkCleanupPendingAsync(Guid operationId, string errorCode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FailAsync(Guid operationId, string errorCode, bool isRetryable, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<IngestOperationRecord>> LoadDueCleanupAsync(DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DateTimeOffset?> FindNextCleanupDueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ScheduleCleanupRetryAsync(Guid operationId, string errorCode, int failureCount, DateTimeOffset? nextAttemptAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ResetCleanupRetryAsync(Guid operationId, CancellationToken cancellationToken)
        {
            operations[operationId] = operations[operationId] with
            {
                NextCleanupAttemptAtUtc = null
            };
            ResetCleanupCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecoveryStorage(
        IReadOnlyDictionary<Guid, IngestStorageEvidence> evidence)
        : IControlledMediaStorage
    {
        public int MutationCount { get; private set; }

        public Task<IngestStorageEvidence> InspectOperationAsync(
            LibraryFolders folders,
            IngestOperationRecord operation,
            CancellationToken cancellationToken) =>
            Task.FromResult(evidence[operation.Id]);

        public Task RestoreBackupAsync(Guid operationId, LibraryFolders folders, LibraryFileSnapshot target, ReplacementBackup backup, CancellationToken cancellationToken)
        {
            MutationCount++;
            return Task.CompletedTask;
        }

        public Task<bool> TargetExistsAsync(LibraryFolders folders, string targetFilename, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StagedFile> StageAsync(Guid operationId, LibraryFolders folders, string sourcePath, string expectedSha256, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CommittedFile> CommitAsync(Guid operationId, LibraryFolders folders, StagedFile staged, string targetFilename, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SourceDeleteResult> DeleteSourceIfMatchesAsync(LibraryFolders folders, string sourcePath, long expectedSize, string expectedSha256, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> VerifyCommittedAsync(LibraryFolders folders, CommittedFile committed, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CleanupTemporaryFilesAsync(Guid operationId, LibraryFolders folders, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecoveryExecutor(RecoveryStore store)
        : IIngestExecutionService
    {
        public int ResumeCalls { get; private set; }

        public Task<IngestResult> ResumeAsync(
            IngestOperationRecord operation,
            LibraryFolders folders,
            CancellationToken cancellationToken)
        {
            ResumeCalls++;
            store.CompleteFromResume(operation.Id);
            return Task.FromResult(new IngestResult(
                IngestOutcome.Completed,
                operation.Id,
                store.Get(operation.Id).AssetId,
                operation.ConflictCaseId,
                "ingest.completed"));
        }

        public Task<IngestResult> ExecuteAsync(IngestPreparation preparation, IngestExecutionPlan plan, LibraryFolders folders, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecoveryCleanup(RecoveryStore store) : IIngestCleanupService
    {
        public int Calls { get; private set; }

        public async Task<CleanupResult> CleanupAsync(
            IngestOperationRecord operation,
            LibraryFolders folders,
            CancellationToken cancellationToken)
        {
            Calls++;
            await store.CompleteAsync(operation.Id, cancellationToken);
            return new CleanupResult(true, null);
        }
    }

    private sealed class FixedSettingsStore : IDirectorySettingsStore
    {
        public Task<LibraryFolders?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<LibraryFolders?>(Folders);
        public Task SaveAsync(LibraryFolders value, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static IngestOperationRecord Operation(
        IngestOperationStage stage,
        Guid? operationId = null) =>
        new(
            operationId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            stage,
            IngestDisposition.Create,
            null,
            "C:/inbox/photo.jpg",
            3,
            DateTimeOffset.Parse("2026-08-22T10:00:00Z"),
            new string('A', 64),
            "photo.jpg",
            "C:/work/.tracefold/ingest/payload",
            stage >= IngestOperationStage.DatabaseCommitted ? Guid.NewGuid() : null,
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

    private static IngestStorageEvidence Evidence(
        FileIdentityKind source = FileIdentityKind.ExpectedSource,
        FileIdentityKind staged = FileIdentityKind.ExpectedStaged,
        FileIdentityKind target = FileIdentityKind.ExpectedNew,
        FileIdentityKind backup = FileIdentityKind.Absent) =>
        new(Identity(source), Identity(staged), Identity(target), Identity(backup));

    private static ObservedFileIdentity Identity(FileIdentityKind kind) =>
        new(kind, null, null, null);
}
