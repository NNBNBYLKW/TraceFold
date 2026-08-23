using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Application.Ingest;

public sealed class IngestRecoveryService(
    IIngestStore store,
    IDirectorySettingsStore settingsStore,
    IControlledMediaStorage storage,
    IFileHasher hasher,
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
            try
            {
                await ReconcileOneAsync(operation, folders, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                logger.LogError(
                    error,
                    "IngestRecoveryFailed OperationId={OperationId}",
                    operation.Id);
                await store.FailAsync(
                    operation.Id,
                    "recovery.unexpected_failure",
                    isRetryable: true,
                    cancellationToken);
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
            || operation.Stage is IngestOperationStage.Completed or IngestOperationStage.Failed)
        {
            return new RecoveryResult(false, "recovery.not_retryable");
        }

        await ReconcileOneAsync(operation, folders, cancellationToken);
        return new RecoveryResult(true, "recovery.reconciled");
    }

    private async Task ReconcileOneAsync(
        IngestOperationRecord operation,
        LibraryFolders folders,
        CancellationToken cancellationToken)
    {
        switch (operation.Stage)
        {
            case IngestOperationStage.Created:
            case IngestOperationStage.Staged:
                if (!await SourceMatchesAsync(operation, cancellationToken))
                {
                    await store.FailAsync(
                        operation.Id,
                        "recovery.source_identity_unknown",
                        isRetryable: false,
                        cancellationToken);
                    return;
                }

                await storage.CleanupTemporaryFilesAsync(
                    operation.Id,
                    folders,
                    cancellationToken);
                await store.AbandonForRetryAsync(operation.Id, cancellationToken);
                return;

            case IngestOperationStage.FileCommitted:
                CommittedFile fileCommitted = ToCommitted(operation, folders);
                if (!await storage.VerifyCommittedAsync(
                        folders,
                        fileCommitted,
                        cancellationToken))
                {
                    await store.FailAsync(
                        operation.Id,
                        "recovery.target_identity_unknown",
                        isRetryable: false,
                        cancellationToken);
                    return;
                }

                Guid assetId = await store.CommitAssetAsync(
                    operation.Id,
                    fileCommitted,
                    cancellationToken);
                await cleanupService.CleanupAsync(
                    operation with
                    {
                        Stage = IngestOperationStage.DatabaseCommitted,
                        AssetId = assetId
                    },
                    folders,
                    cancellationToken);
                return;

            case IngestOperationStage.DatabaseCommitted:
            case IngestOperationStage.CleanupPending:
                CommittedFile committed = ToCommitted(operation, folders);
                if (!operation.AssetId.HasValue
                    || !await storage.VerifyCommittedAsync(
                        folders,
                        committed,
                        cancellationToken))
                {
                    await store.FailAsync(
                        operation.Id,
                        "recovery.target_identity_unknown",
                        isRetryable: false,
                        cancellationToken);
                    return;
                }

                await cleanupService.CleanupAsync(
                    operation,
                    folders,
                    cancellationToken);
                return;

            default:
                return;
        }
    }

    private async Task<bool> SourceMatchesAsync(
        IngestOperationRecord operation,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(operation.SourcePath))
        {
            return false;
        }

        var info = new FileInfo(operation.SourcePath);
        if (info.Length != operation.SourceSize
            || new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)
                != operation.SourceLastWriteTimeUtc)
        {
            return false;
        }

        string hash = await hasher.ComputeSha256Async(
            operation.SourcePath,
            cancellationToken);
        return StringComparer.OrdinalIgnoreCase.Equals(hash, operation.SourceSha256);
    }

    private static CommittedFile ToCommitted(
        IngestOperationRecord operation,
        LibraryFolders folders) =>
        new(
            Path.Combine(folders.LibraryDirectory, operation.TargetFilename),
            operation.TargetFilename,
            operation.SourceSize,
            operation.SourceSha256);
}
