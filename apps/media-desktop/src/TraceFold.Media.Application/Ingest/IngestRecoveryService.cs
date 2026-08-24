using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Application.Ingest;

public sealed class IngestRecoveryService(
    IIngestStore store,
    IDirectorySettingsStore settingsStore,
    IControlledMediaStorage storage,
    IIngestExecutionService executor,
    ILogger<IngestRecoveryService> logger,
    IIngestCleanupService? cleanup = null) : IIngestRecoveryService
{
    private readonly IIngestCleanupService cleanupService =
        cleanup ?? new IngestCleanupService(store, storage);

    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        LibraryFolders? folders = await settingsStore.LoadAsync(cancellationToken);
        if (folders is null)
        {
            return;
        }

        IReadOnlyList<IngestOperationRecord> incomplete =
            await store.LoadIncompleteAsync(cancellationToken);
        foreach (IngestOperationRecord operation in incomplete)
        {
            if (operation.Stage == IngestOperationStage.RecoveryBlocked)
            {
                continue;
            }

            try
            {
                await ReconcileOneAsync(
                    operation,
                    folders,
                    allowBlockedRetry: false,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                logger.LogError(
                    error,
                    "IngestRecoveryFailed OperationId={OperationId}",
                    operation.Id);
                IngestOperationRecord? current = await store.FindActiveAsync(
                    operation.InboxItemId,
                    cancellationToken);
                if (current is not null
                    && current.Stage is not
                        (IngestOperationStage.Completed
                            or IngestOperationStage.Failed
                            or IngestOperationStage.RecoveryBlocked))
                {
                    await store.MarkRecoveryBlockedAsync(
                        current.Id,
                        "recovery.unexpected_failure",
                        cancellationToken);
                }
            }
        }
    }

    public async Task<RecoveryResult> RetryAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        LibraryFolders? folders = await settingsStore.LoadAsync(cancellationToken);
        IngestOperationRecord? operation = await store.FindActiveAsync(
            inboxItemId,
            cancellationToken);
        if (folders is null
            || operation is null
            || operation.Stage is
                IngestOperationStage.Completed or IngestOperationStage.Failed)
        {
            return new RecoveryResult(false, "recovery.not_retryable");
        }

        bool progressed = await ReconcileOneAsync(
            operation,
            folders,
            allowBlockedRetry: true,
            cancellationToken);
        return progressed
            ? new RecoveryResult(true, "recovery.reconciled")
            : new RecoveryResult(false, "recovery.identity_ambiguous");
    }

    private async Task<bool> ReconcileOneAsync(
        IngestOperationRecord initial,
        LibraryFolders folders,
        bool allowBlockedRetry,
        CancellationToken cancellationToken)
    {
        IngestOperationRecord current = initial;
        for (int transition = 0; transition < 4; transition++)
        {
            if (current.Stage is
                IngestOperationStage.Completed or IngestOperationStage.Failed)
            {
                return true;
            }

            bool wasBlocked = current.Stage == IngestOperationStage.RecoveryBlocked;
            if (wasBlocked && !allowBlockedRetry)
            {
                return false;
            }

            IngestOperationRecord effective = wasBlocked
                ? current with
                {
                    Stage = current.RecoveryBlockedFromStage
                        ?? throw new InvalidOperationException(
                            "A blocked recovery has no persisted prior stage.")
                }
                : current;
            if (effective.Stage is
                    IngestOperationStage.DatabaseCommitted
                    or IngestOperationStage.CleanupPending
                && effective.AssetId is null)
            {
                if (!wasBlocked)
                {
                    await store.MarkRecoveryBlockedAsync(
                        effective.Id,
                        "recovery.asset_identity_missing",
                        cancellationToken);
                }

                return false;
            }

            IngestStorageEvidence storageEvidence =
                await storage.InspectOperationAsync(
                    folders,
                    effective,
                    cancellationToken);
            bool databaseHasNewVersion =
                await store.OperationDatabaseHasNewVersionAsync(
                    effective.Id,
                    cancellationToken);
            var evidence = new IngestRecoveryEvidence(
                effective.Stage,
                effective.Disposition,
                storageEvidence.Source,
                storageEvidence.Staged,
                storageEvidence.Target,
                storageEvidence.Backup,
                effective.BackupState,
                databaseHasNewVersion);
            IngestRecoveryAction action = IngestRecoveryDecision.Decide(evidence);
            if (action == IngestRecoveryAction.Block)
            {
                if (!wasBlocked)
                {
                    await store.MarkRecoveryBlockedAsync(
                        effective.Id,
                        "recovery.identity_ambiguous",
                        cancellationToken);
                }

                return false;
            }

            if (wasBlocked)
            {
                await store.ResumeRecoveryAsync(
                    effective.Id,
                    cancellationToken);
                current = await ReloadAsync(effective, cancellationToken);
            }
            else
            {
                current = effective;
            }

            switch (action)
            {
                case IngestRecoveryAction.ResumeExecution:
                    await executor.ResumeAsync(
                        current,
                        folders,
                        cancellationToken);
                    return true;

                case IngestRecoveryAction.RestoreBackup:
                    await RestoreAndAbandonAsync(
                        current,
                        folders,
                        cancellationToken);
                    return true;

                case IngestRecoveryAction.CommitDatabase:
                    await store.CommitAssetAsync(
                        current.Id,
                        ToCommitted(current, folders),
                        cancellationToken);
                    current = await ReloadAsync(current, cancellationToken);
                    continue;

                case IngestRecoveryAction.Cleanup:
                    await cleanupService.CleanupAsync(
                        current,
                        folders,
                        cancellationToken);
                    return true;

                case IngestRecoveryAction.AbandonForRetry:
                    await storage.CleanupTemporaryFilesAsync(
                        current.Id,
                        folders,
                        cancellationToken);
                    await store.AbandonForRetryAsync(
                        current.Id,
                        cancellationToken);
                    return true;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported recovery action {action}.");
            }
        }

        throw new InvalidOperationException(
            "Ingest recovery exceeded its finite transition limit.");
    }

    private async Task RestoreAndAbandonAsync(
        IngestOperationRecord operation,
        LibraryFolders folders,
        CancellationToken cancellationToken)
    {
        LibraryFileSnapshot target = ToTarget(operation, folders);
        ReplacementBackup backup = ToBackup(operation, folders);
        await storage.RestoreBackupAsync(
            operation.Id,
            folders,
            target,
            backup,
            cancellationToken);
        await store.MarkBackupRestoredAsync(
            operation.Id,
            cancellationToken);
        await storage.CleanupTemporaryFilesAsync(
            operation.Id,
            folders,
            cancellationToken);
        await store.AbandonForRetryAsync(
            operation.Id,
            cancellationToken);
    }

    private async Task<IngestOperationRecord> ReloadAsync(
        IngestOperationRecord operation,
        CancellationToken cancellationToken) =>
        await store.FindActiveAsync(operation.InboxItemId, cancellationToken)
            ?? throw new InvalidOperationException(
                "The active ingest operation disappeared during recovery.");

    private static CommittedFile ToCommitted(
        IngestOperationRecord operation,
        LibraryFolders folders) =>
        new(
            Path.Combine(folders.LibraryDirectory, operation.TargetFilename),
            operation.TargetFilename,
            operation.SourceSize,
            operation.SourceSha256);

    private static LibraryFileSnapshot ToTarget(
        IngestOperationRecord operation,
        LibraryFolders folders)
    {
        string relativePath = operation.ExpectedTargetRelativePath
            ?? throw new InvalidOperationException(
                "A replacement recovery has no target path.");
        return new LibraryFileSnapshot(
            Path.Combine(folders.LibraryDirectory, relativePath),
            relativePath,
            operation.ExpectedTargetSize
                ?? throw new InvalidOperationException(
                    "A replacement recovery has no target size."),
            operation.ExpectedTargetLastWriteTimeUtc
                ?? throw new InvalidOperationException(
                    "A replacement recovery has no target timestamp."),
            operation.ExpectedTargetSha256
                ?? throw new InvalidOperationException(
                    "A replacement recovery has no target hash."));
    }

    private static ReplacementBackup ToBackup(
        IngestOperationRecord operation,
        LibraryFolders folders)
    {
        string relativePath = operation.BackupRelativePath
            ?? throw new InvalidOperationException(
                "A replacement recovery has no backup path.");
        return new ReplacementBackup(
            Path.Combine(folders.LibraryDirectory, relativePath),
            relativePath,
            operation.BackupSize
                ?? throw new InvalidOperationException(
                    "A replacement recovery has no backup size."),
            operation.BackupSha256
                ?? throw new InvalidOperationException(
                    "A replacement recovery has no backup hash."));
    }
}
