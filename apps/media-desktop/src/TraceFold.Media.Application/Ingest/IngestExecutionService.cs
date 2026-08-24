using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Application.Ingest;

public sealed class IngestExecutionService(
    IIngestStore store,
    IControlledMediaStorage storage,
    IIngestCleanupService cleanup,
    IIngestFaultInjector faultInjector,
    ILogger<IngestExecutionService> logger) : IIngestExecutionService
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
        }
    }

    private async Task<IngestResult> ExecuteCoreAsync(
        IngestPreparation preparation,
        IngestExecutionPlan plan,
        LibraryFolders folders,
        CancellationToken cancellationToken)
    {
        IngestOperationRecord operation = await store.CreateAsync(
            preparation,
            plan,
            cancellationToken);
        await faultInjector.ReachAsync(
            IngestFaultPoint.AfterOperationCreated,
            operation.Id,
            cancellationToken);
        StagedFile staged;
        try
        {
            staged = await storage.StageAsync(
                operation.Id,
                folders,
                preparation.Input,
                cancellationToken);
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
            catch (Exception error)
            {
                logger.LogError(
                    error,
                    "IngestReplacementBackupFailed OperationId={OperationId}",
                    operation.Id);
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
                error,
                "IngestDatabaseCommitFailed OperationId={OperationId}",
                operation.Id);
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
                        evidenceError,
                        "IngestReplacementDatabaseEvidenceFailed OperationId={OperationId}",
                        operation.Id);
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
        logger.LogError(
            originalError,
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
        catch (Exception restoreError)
        {
            logger.LogError(
                restoreError,
                "IngestReplacementRestoreBlocked OperationId={OperationId}",
                operation.Id);
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
            error,
            "IngestExecutionFailed OperationId={OperationId} ErrorCode={ErrorCode}",
            operation.Id,
            errorCode);
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
}
