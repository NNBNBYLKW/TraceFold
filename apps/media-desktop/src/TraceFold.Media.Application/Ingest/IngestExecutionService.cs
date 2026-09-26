using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Application.Ingest;

public sealed class IngestExecutionService(
    IIngestStore store,
    IControlledMediaStorage storage,
    IIngestCleanupService cleanup,
    IIngestFaultInjector faultInjector,
    ILogger<IngestExecutionService> logger,
    IInboxActivityNotifier? activity = null) : IIngestExecutionService
{
    private readonly SemaphoreSlim commitGate = new(1, 1);

    public async Task<IngestResult> ExecuteAsync(
        IngestPreparation preparation,
        IngestExecutionPlan plan,
        LibraryFolders folders,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(folders);
        await commitGate.WaitAsync(cancellationToken);
        try
        {
            return await ExecuteCoreAsync(
                preparation,
                plan,
                folders,
                cancellationToken);
        }
        finally
        {
            commitGate.Release();
            activity?.Publish(InboxActivityKind.ItemChanged);
        }
    }

    public async Task<IngestResult> ResumeAsync(
        IngestOperationRecord operation,
        LibraryFolders folders,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(folders);
        if (operation.Stage is not
            (IngestOperationStage.Created or IngestOperationStage.Staged))
        {
            throw new InvalidOperationException(
                "Only a created or staged ingest operation can resume execution.");
        }

        await commitGate.WaitAsync(cancellationToken);
        try
        {
            return await ExecuteCoreAsync(
                RehydratePreparation(operation, folders),
                RehydratePlan(operation, folders),
                folders,
                cancellationToken,
                operation);
        }
        finally
        {
            commitGate.Release();
            activity?.Publish(InboxActivityKind.ItemChanged);
        }
    }

    private async Task<IngestResult> ExecuteCoreAsync(
        IngestPreparation preparation,
        IngestExecutionPlan plan,
        LibraryFolders folders,
        CancellationToken cancellationToken,
        IngestOperationRecord? persistedOperation = null)
    {
        IngestOperationRecord operation = persistedOperation
            ?? await store.CreateAsync(
                preparation,
                plan,
                cancellationToken);
        if (persistedOperation is null)
        {
            await faultInjector.ReachAsync(
                IngestFaultPoint.AfterOperationCreated,
                operation.Id,
                cancellationToken);
        }

        StagedFile staged;
        if (operation.Stage == IngestOperationStage.Staged)
        {
            staged = new StagedFile(
                operation.StagedPath
                    ?? throw new InvalidOperationException(
                        "A staged ingest operation has no persisted payload path."),
                operation.SourceSize,
                operation.SourceSha256);
        }
        else
        {
            try
            {
                if (persistedOperation is null)
                {
                    staged = await storage.StageAsync(
                        operation.Id,
                        folders,
                        preparation.Input,
                        cancellationToken);
                }
                else
                {
                    IngestStorageEvidence evidence =
                        await storage.InspectOperationAsync(
                            folders,
                            operation,
                            cancellationToken);
                    staged = evidence.Staged.Kind switch
                    {
                        FileIdentityKind.ExpectedStaged => new StagedFile(
                            evidence.Staged.FullPath
                                ?? throw new InvalidOperationException(
                                    "Verified staged evidence has no path."),
                            operation.SourceSize,
                            operation.SourceSha256),
                        FileIdentityKind.Absent => await storage.StageAsync(
                            operation.Id,
                            folders,
                            preparation.Input,
                            cancellationToken),
                        _ => throw new StorageIntegrityException(
                            "The persisted staging evidence is ambiguous.")
                    };
                }

                await store.MarkStagedAsync(
                    operation.Id,
                    staged,
                    cancellationToken);
                operation = operation with
                {
                    Stage = IngestOperationStage.Staged,
                    StagedPath = staged.FullPath
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                return await FailOrdinaryAsync(
                    operation,
                    plan,
                    "ingest.stage_failed",
                    error,
                    cancellationToken);
            }
        }
        await faultInjector.ReachAsync(
            IngestFaultPoint.AfterStaged,
            operation.Id,
            cancellationToken);

        ReplacementBackup? backup = null;
        if (plan.Target is not null)
        {
            try
            {
                backup = await storage.BackupTargetAsync(
                    operation.Id,
                    folders,
                    plan.Target,
                    cancellationToken);
                await store.MarkBackupCreatedAsync(
                    operation.Id,
                    backup,
                    cancellationToken);
                operation = operation with
                {
                    BackupState = IngestBackupState.Created,
                    BackupRelativePath = backup.RelativePath,
                    BackupSize = backup.Size,
                    BackupSha256 = backup.Sha256
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                logger.LogError(
                    "IngestReplacementBackupFailed OperationId={OperationId} ErrorCode={ErrorCode}",
                    operation.Id,
                    "recovery.backup_identity_ambiguous");
                await store.MarkRecoveryBlockedAsync(
                    operation.Id,
                    "recovery.backup_identity_ambiguous",
                    cancellationToken);
                return Result(
                    IngestOutcome.Failed,
                    "recovery.backup_identity_ambiguous",
                    operation.Id,
                    conflictCaseId: plan.ConflictCaseId);
            }

            await faultInjector.ReachAsync(
                IngestFaultPoint.AfterBackupCreated,
                operation.Id,
                cancellationToken);
        }

        CommittedFile committed;
        try
        {
            committed = plan.Disposition switch
            {
                IngestDisposition.Create => await storage.CommitAsync(
                    operation.Id,
                    folders,
                    staged,
                    plan.RequestedTargetFilename,
                    cancellationToken),
                IngestDisposition.KeepBoth => await storage.CommitUniqueAsync(
                    operation.Id,
                    folders,
                    staged,
                    plan.RequestedTargetFilename,
                    cancellationToken),
                IngestDisposition.ReplaceRegistered
                    or IngestDisposition.ReplaceUnregistered =>
                    await storage.CommitReplacementAsync(
                        operation.Id,
                        folders,
                        staged,
                        plan.Target
                            ?? throw new InvalidOperationException(
                                "A replacement plan has no target."),
                        cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(plan))
            };
            await store.MarkFileCommittedAsync(
                operation.Id,
                committed,
                cancellationToken);
            operation = operation with
            {
                Stage = IngestOperationStage.FileCommitted,
                TargetFilename = committed.RelativePath
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            if (backup is not null && plan.Target is not null)
            {
                return await RestoreOrBlockAsync(
                    operation,
                    plan,
                    folders,
                    plan.Target,
                    backup,
                    "ingest.commit_failed",
                    error,
                    cancellationToken);
            }

            return await FailOrdinaryAsync(
                operation,
                plan,
                "ingest.commit_failed",
                error,
                cancellationToken);
        }

        await faultInjector.ReachAsync(
            IngestFaultPoint.AfterFileCommitted,
            operation.Id,
            cancellationToken);

        await faultInjector.ReachAsync(
            IngestFaultPoint.BeforeDatabaseCommit,
            operation.Id,
            cancellationToken);

        Guid assetId;
        try
        {
            assetId = await store.CommitAssetAsync(
                operation.Id,
                committed,
                cancellationToken);
            operation = operation with
            {
                Stage = IngestOperationStage.DatabaseCommitted,
                AssetId = assetId
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            logger.LogError(
                "IngestDatabaseCommitFailed OperationId={OperationId} ErrorCode={ErrorCode}",
                operation.Id,
                "ingest.database_commit_failed");
            if (backup is not null && plan.Target is not null)
            {
                bool canRestore;
                try
                {
                    canRestore =
                        await store.ReplacementDatabaseMatchesExpectedAsync(
                            operation.Id,
                            cancellationToken);
                }
                catch (Exception evidenceError)
                    when (evidenceError is not OperationCanceledException)
                {
                    logger.LogError(
                        "IngestReplacementDatabaseEvidenceFailed OperationId={OperationId} ErrorCode={ErrorCode}",
                        operation.Id,
                        "recovery.database_identity_ambiguous");
                    canRestore = false;
                }

                if (canRestore)
                {
                    return await RestoreOrBlockAsync(
                        operation,
                        plan,
                        folders,
                        plan.Target,
                        backup,
                        "ingest.database_failed",
                        error,
                        cancellationToken);
                }

                await store.MarkRecoveryBlockedAsync(
                    operation.Id,
                    "recovery.database_identity_ambiguous",
                    cancellationToken);
                return Result(
                    IngestOutcome.Failed,
                    "recovery.database_identity_ambiguous",
                    operation.Id,
                    conflictCaseId: plan.ConflictCaseId);
            }

            return Result(
                IngestOutcome.Failed,
                "ingest.database_failed",
                operation.Id,
                conflictCaseId: plan.ConflictCaseId);
        }

        await faultInjector.ReachAsync(
            IngestFaultPoint.AfterDatabaseCommit,
            operation.Id,
            cancellationToken);

        await faultInjector.ReachAsync(
            IngestFaultPoint.BeforeCleanup,
            operation.Id,
            cancellationToken);

        if (!await storage.VerifyCommittedAsync(
                folders,
                committed,
                cancellationToken))
        {
            await store.MarkCleanupPendingAsync(
                operation.Id,
                "ingest.commit_failed",
                cancellationToken);
            return Result(
                IngestOutcome.CleanupPending,
                "ingest.commit_failed",
                operation.Id,
                assetId,
                plan.ConflictCaseId);
        }

        CleanupResult cleanupResult = await cleanup.CleanupAsync(
            operation,
            folders,
            cancellationToken);
        await faultInjector.ReachAsync(
            IngestFaultPoint.AfterCleanup,
            operation.Id,
            cancellationToken);
        if (!cleanupResult.Completed)
        {
            return Result(
                IngestOutcome.CleanupPending,
                cleanupResult.ErrorCode ?? "ingest.cleanup_failed",
                operation.Id,
                assetId,
                plan.ConflictCaseId);
        }

        return Result(
            IngestOutcome.Completed,
            "ingest.completed",
            operation.Id,
            assetId,
            plan.ConflictCaseId);
    }

    private async Task<IngestResult> RestoreOrBlockAsync(
        IngestOperationRecord operation,
        IngestExecutionPlan plan,
        LibraryFolders folders,
        LibraryFileSnapshot target,
        ReplacementBackup backup,
        string failureCode,
        Exception originalError,
        CancellationToken cancellationToken)
    {
        _ = originalError;
        logger.LogError(
            "IngestReplacementFailed OperationId={OperationId} ErrorCode={ErrorCode}",
            operation.Id,
            failureCode);
        try
        {
            await storage.RestoreBackupAsync(
                operation.Id,
                folders,
                target,
                backup,
                cancellationToken);
            await store.MarkBackupRestoredAsync(
                operation.Id,
                cancellationToken);
            return Result(
                IngestOutcome.Failed,
                failureCode,
                operation.Id,
                conflictCaseId: plan.ConflictCaseId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError(
                "IngestReplacementRestoreBlocked OperationId={OperationId} ErrorCode={ErrorCode}",
                operation.Id,
                "recovery.target_identity_ambiguous");
            await store.MarkRecoveryBlockedAsync(
                operation.Id,
                "recovery.target_identity_ambiguous",
                cancellationToken);
            return Result(
                IngestOutcome.Failed,
                "recovery.target_identity_ambiguous",
                operation.Id,
                conflictCaseId: plan.ConflictCaseId);
        }
    }

    private async Task<IngestResult> FailOrdinaryAsync(
        IngestOperationRecord operation,
        IngestExecutionPlan plan,
        string errorCode,
        Exception error,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            "IngestExecutionFailed OperationId={OperationId} ErrorCode={ErrorCode} ExceptionType={ExceptionType} HResult={HResult}",
            operation.Id,
            errorCode,
            error.GetType().FullName,
            error.HResult);
        await store.FailAsync(
            operation.Id,
            errorCode,
            isRetryable: true,
            cancellationToken);
        return Result(
            IngestOutcome.Failed,
            errorCode,
            operation.Id,
            conflictCaseId: plan.ConflictCaseId);
    }

    private static IngestResult Result(
        IngestOutcome outcome,
        string message,
        Guid? operationId = null,
        Guid? assetId = null,
        Guid? conflictCaseId = null) =>
        new(outcome, operationId, assetId, conflictCaseId, message);

    private static IngestPreparation RehydratePreparation(
        IngestOperationRecord operation,
        LibraryFolders folders) =>
        new(
            operation.InboxItemId,
            default,
            operation.SourcePath,
            operation.SourceSize,
            operation.SourceLastWriteTimeUtc,
            operation.SourceSha256,
            operation.TargetFilename,
            null,
            [],
            null,
            [],
            0,
            RehydrateInput(operation, folders));

    private static IngestExecutionPlan RehydratePlan(
        IngestOperationRecord operation,
        LibraryFolders folders) => operation.Disposition switch
    {
        IngestDisposition.Create => IngestExecutionPlan.Create(
            operation.TargetFilename),
        IngestDisposition.KeepBoth => IngestExecutionPlan.KeepBoth(
            operation.ConflictCaseId
                ?? throw new InvalidOperationException(
                    "A persisted Keep Both operation has no conflict id."),
            operation.TargetFilename),
        IngestDisposition.ReplaceRegistered =>
            IngestExecutionPlan.ReplaceRegistered(
                operation.ConflictCaseId
                    ?? throw new InvalidOperationException(
                        "A persisted replacement has no conflict id."),
                operation.ReplacementAssetId
                    ?? throw new InvalidOperationException(
                        "A registered replacement has no asset id."),
                operation.ReplacementAssetMetadataVersion
                    ?? throw new InvalidOperationException(
                        "A registered replacement has no metadata version."),
                RehydrateTarget(operation, folders)),
        IngestDisposition.ReplaceUnregistered =>
            IngestExecutionPlan.ReplaceUnregistered(
                operation.ConflictCaseId
                    ?? throw new InvalidOperationException(
                        "A persisted replacement has no conflict id."),
                RehydrateTarget(operation, folders)),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static LibraryFileSnapshot RehydrateTarget(
        IngestOperationRecord operation,
        LibraryFolders folders) =>
        new(
            Path.Combine(
                folders.LibraryDirectory,
                operation.ExpectedTargetRelativePath
                    ?? throw new InvalidOperationException(
                        "A persisted replacement has no target path.")),
            operation.ExpectedTargetRelativePath,
            operation.ExpectedTargetSize
                ?? throw new InvalidOperationException(
                    "A persisted replacement has no target size."),
            operation.ExpectedTargetLastWriteTimeUtc
                ?? throw new InvalidOperationException(
                    "A persisted replacement has no target timestamp."),
            operation.ExpectedTargetSha256
                ?? throw new InvalidOperationException(
                    "A persisted replacement has no target hash."));

    private static PreparedIngestInput RehydrateInput(
        IngestOperationRecord operation,
        LibraryFolders folders)
    {
        string source = Path.GetFullPath(operation.SourcePath);
        string inbox = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(folders.InboxDirectory));
        IngestCleanupKind kind = StringComparer.OrdinalIgnoreCase.Equals(
            Path.GetDirectoryName(source),
            inbox)
                ? IngestCleanupKind.InboxSource
                : IngestCleanupKind.ProcessingOutput;
        return new PreparedIngestInput(
            kind,
            source,
            operation.SourceSize,
            operation.SourceLastWriteTimeUtc,
            operation.SourceSha256,
            []);
    }
}
