using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Application.Ingest;

public sealed class OrdinaryIngestService(
    IInboxScanner scanner,
    IIngestStore store,
    IDirectorySettingsStore settingsStore,
    IFileHasher hasher,
    IControlledMediaStorage storage,
    IMediaOperationCoordinator coordinator,
    ILogger<OrdinaryIngestService> logger) : IOrdinaryIngestService
{
    private readonly SemaphoreSlim _commitGate = new(1, 1);

    public async Task<IngestResult> IngestAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        IMediaOperationLease lease;
        try
        {
            lease = coordinator.Begin(cancellationToken);
        }
        catch (MediaOperationRejectedException)
        {
            return Result(IngestOutcome.ShuttingDown, "ingest.shutting_down");
        }

        await using (lease)
        {
            CancellationToken operationToken = lease.CancellationToken;
            IngestOperationRecord? operation = null;
            bool gateEntered = false;
            try
            {
                await scanner.ScanAsync(ScanTrigger.PreIngest, operationToken);
                IngestOperationRecord? existing = await store.FindActiveAsync(
                    inboxItemId,
                    operationToken);
                if (existing is not null)
                {
                    return ExistingResult(existing);
                }

                IngestCandidate? candidate = await store.LoadCandidateAsync(
                    inboxItemId,
                    operationToken);
                IngestResult? rejected = ValidateCandidate(candidate);
                if (rejected is not null)
                {
                    return rejected;
                }

                IngestSourceRecord source = candidate!.Sources[0];
                if (!MatchesFingerprint(source))
                {
                    return File.Exists(source.FullPath)
                        ? Result(IngestOutcome.SourceChanged, "ingest.source_changed")
                        : Result(IngestOutcome.SourceMissing, "ingest.source_missing");
                }

                string sourceHash = await hasher.ComputeSha256Async(
                    source.FullPath,
                    operationToken);
                LibraryFolders? folders = await settingsStore.LoadAsync(operationToken);
                if (folders is null)
                {
                    return Result(IngestOutcome.NotReady, "ingest.not_ready");
                }

                await _commitGate.WaitAsync(operationToken);
                gateEntered = true;
                if (!MatchesFingerprint(source))
                {
                    return File.Exists(source.FullPath)
                        ? Result(IngestOutcome.SourceChanged, "ingest.source_changed")
                        : Result(IngestOutcome.SourceMissing, "ingest.source_missing");
                }

                ConflictKind? conflict = await storage.TargetExistsAsync(
                    folders,
                    candidate.ActualFilename,
                    operationToken)
                    ? ConflictKind.Name
                    : (await store.CheckConflictsAsync(
                        candidate.ActualFilename,
                        sourceHash,
                        operationToken)).Kind;
                if (conflict.HasValue)
                {
                    await store.BlockConflictAsync(
                        inboxItemId,
                        conflict.Value,
                        operationToken);
                    return Result(
                        IngestOutcome.ConflictBlocked,
                        conflict == ConflictKind.Name
                            ? "ingest.name_conflict"
                            : "ingest.content_conflict");
                }

                operation = await store.CreateAsync(
                    new IngestPreparation(
                        candidate.InboxItemId,
                        candidate.MediaKind,
                        source.FullPath,
                        source.Size,
                        source.LastWriteTimeUtc,
                        sourceHash,
                        candidate.ActualFilename,
                        candidate.Metadata.DisplayName,
                        candidate.Metadata.AuthorIds,
                        candidate.Metadata.SeriesId,
                        candidate.Metadata.TagIds,
                        candidate.MetadataVersion),
                    operationToken);

                StagedFile staged;
                try
                {
                    staged = await storage.StageAsync(
                        operation.Id,
                        folders,
                        source.FullPath,
                        sourceHash,
                        operationToken);
                    await store.MarkStagedAsync(operation.Id, staged, operationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception error)
                {
                    return await FailAsync(
                        operation,
                        "ingest.stage_failed",
                        error,
                        operationToken);
                }

                CommittedFile committed;
                try
                {
                    committed = await storage.CommitAsync(
                        operation.Id,
                        folders,
                        staged,
                        candidate.ActualFilename,
                        operationToken);
                    await store.MarkFileCommittedAsync(
                        operation.Id,
                        committed,
                        operationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception error)
                {
                    return await FailAsync(
                        operation,
                        "ingest.commit_failed",
                        error,
                        operationToken);
                }

                Guid assetId;
                try
                {
                    assetId = await store.CommitAssetAsync(
                        operation.Id,
                        committed,
                        operationToken);
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
                    return Result(
                        IngestOutcome.Failed,
                        "ingest.database_failed",
                        operation.Id);
                }

                bool committedMatches = await storage.VerifyCommittedAsync(
                    folders,
                    committed,
                    operationToken);
                if (!committedMatches)
                {
                    await store.MarkCleanupPendingAsync(
                        operation.Id,
                        "ingest.commit_failed",
                        operationToken);
                    return Result(
                        IngestOutcome.CleanupPending,
                        "ingest.commit_failed",
                        operation.Id,
                        assetId);
                }

                SourceDeleteResult deleted = await storage.DeleteSourceIfMatchesAsync(
                    folders,
                    source.FullPath,
                    source.Size,
                    sourceHash,
                    operationToken);
                if (deleted.Outcome != SourceDeleteOutcome.Deleted)
                {
                    await store.MarkCleanupPendingAsync(
                        operation.Id,
                        "ingest.cleanup_failed",
                        operationToken);
                    return Result(
                        IngestOutcome.CleanupPending,
                        "ingest.cleanup_failed",
                        operation.Id,
                        assetId);
                }

                try
                {
                    await storage.CleanupTemporaryFilesAsync(
                        operation.Id,
                        folders,
                        operationToken);
                    await store.CompleteAsync(operation.Id, operationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception error)
                {
                    logger.LogError(
                        error,
                        "IngestCleanupFailed OperationId={OperationId}",
                        operation.Id);
                    await store.MarkCleanupPendingAsync(
                        operation.Id,
                        "ingest.cleanup_failed",
                        operationToken);
                    return Result(
                        IngestOutcome.CleanupPending,
                        "ingest.cleanup_failed",
                        operation.Id,
                        assetId);
                }

                return Result(
                    IngestOutcome.Completed,
                    "ingest.completed",
                    operation.Id,
                    assetId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException error)
            {
                logger.LogInformation(
                    error,
                    "IngestStopped OperationId={OperationId}",
                    operation?.Id);
                return Result(
                    IngestOutcome.ShuttingDown,
                    "ingest.shutting_down",
                    operation?.Id);
            }
            catch (Exception error)
            {
                logger.LogError(
                    error,
                    "IngestFailed OperationId={OperationId}",
                    operation?.Id);
                if (operation is not null)
                {
                    await store.FailAsync(
                        operation.Id,
                        "ingest.commit_failed",
                        isRetryable: true,
                        CancellationToken.None);
                }

                return Result(
                    IngestOutcome.Failed,
                    "ingest.commit_failed",
                    operation?.Id);
            }
            finally
            {
                if (gateEntered)
                {
                    _commitGate.Release();
                }
            }
        }
    }

    private async Task<IngestResult> FailAsync(
        IngestOperationRecord operation,
        string errorCode,
        Exception error,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            error,
            "IngestStageFailed OperationId={OperationId} ErrorCode={ErrorCode}",
            operation.Id,
            errorCode);
        await store.FailAsync(
            operation.Id,
            errorCode,
            isRetryable: true,
            cancellationToken);
        return Result(IngestOutcome.Failed, errorCode, operation.Id);
    }

    private static IngestResult? ValidateCandidate(IngestCandidate? candidate)
    {
        if (candidate is null)
        {
            return Result(IngestOutcome.NotReady, "ingest.not_ready");
        }

        if (!string.IsNullOrWhiteSpace(candidate.RequiredCapability)
            || candidate.State == InboxItemState.RequiresProcessing)
        {
            return Result(
                IngestOutcome.RequiresProcessing,
                "ingest.requires_processing");
        }

        if (candidate.State == InboxItemState.SourceMissing
            || candidate.Sources.Count == 0
            || candidate.Sources.Any(source => !source.IsPresent))
        {
            return Result(IngestOutcome.SourceMissing, "ingest.source_missing");
        }

        if (candidate.State != InboxItemState.ReadyToTag
            || candidate.Sources.Count != 1
            || !candidate.Sources[0].IsPrimary)
        {
            return Result(IngestOutcome.NotReady, "ingest.not_ready");
        }

        return null;
    }

    private static bool MatchesFingerprint(IngestSourceRecord source)
    {
        if (!File.Exists(source.FullPath))
        {
            return false;
        }

        var info = new FileInfo(source.FullPath);
        return info.Length == source.Size
            && new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)
                == source.LastWriteTimeUtc;
    }

    private static IngestResult ExistingResult(IngestOperationRecord operation) =>
        operation.Stage switch
        {
            IngestOperationStage.Completed => Result(
                IngestOutcome.Completed,
                "ingest.completed",
                operation.Id,
                operation.AssetId),
            IngestOperationStage.DatabaseCommitted or IngestOperationStage.CleanupPending =>
                Result(
                    IngestOutcome.CleanupPending,
                    operation.ErrorCode ?? "ingest.cleanup_failed",
                    operation.Id,
                    operation.AssetId),
            IngestOperationStage.Failed => Result(
                IngestOutcome.Failed,
                operation.ErrorCode ?? "ingest.commit_failed",
                operation.Id,
                operation.AssetId),
            _ => Result(
                IngestOutcome.InProgress,
                "ingest.in_progress",
                operation.Id,
                operation.AssetId)
        };

    private static IngestResult Result(
        IngestOutcome outcome,
        string message,
        Guid? operationId = null,
        Guid? assetId = null) => new(outcome, operationId, assetId, message);
}
