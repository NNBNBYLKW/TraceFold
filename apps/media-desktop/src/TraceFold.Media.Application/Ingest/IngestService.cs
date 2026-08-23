using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Application.Ingest;

public class IngestService(
    IInboxScanner scanner,
    IIngestStore store,
    IDirectorySettingsStore settingsStore,
    IIngestInputResolver inputResolver,
    IControlledMediaStorage storage,
    IIngestCleanupService cleanup,
    IMediaOperationCoordinator coordinator,
    ILogger<IngestService> logger) : IIngestService
{
    private readonly SemaphoreSlim commitGate = new(1, 1);

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

                LibraryFolders? folders = await settingsStore.LoadAsync(operationToken);
                if (folders is null)
                {
                    return Result(IngestOutcome.NotReady, "ingest.not_ready");
                }

                await commitGate.WaitAsync(operationToken);
                gateEntered = true;
                PreparedIngestInput input;
                try
                {
                    input = await inputResolver.ResolveAsync(
                        candidate!,
                        operationToken);
                }
                catch (IngestInputException error)
                {
                    return InputFailure(error);
                }

                ConflictKind? conflict = await ResolveConflictAsync(
                    folders,
                    candidate!.ActualFilename,
                    input.Sha256,
                    operationToken);
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
                    IngestPreparation.From(candidate, input),
                    operationToken);

                StagedFile staged;
                try
                {
                    staged = await storage.StageAsync(
                        operation.Id,
                        folders,
                        input,
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
                    return Result(
                        IngestOutcome.Failed,
                        "ingest.database_failed",
                        operation.Id);
                }

                if (!await storage.VerifyCommittedAsync(
                        folders,
                        committed,
                        operationToken))
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

                CleanupResult cleanupResult = await cleanup.CleanupAsync(
                    operation,
                    folders,
                    operationToken);
                if (!cleanupResult.Completed)
                {
                    return Result(
                        IngestOutcome.CleanupPending,
                        cleanupResult.ErrorCode ?? "ingest.cleanup_failed",
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
                    commitGate.Release();
                }
            }
        }
    }

    private async Task<ConflictKind?> ResolveConflictAsync(
        LibraryFolders folders,
        string targetFilename,
        string sha256,
        CancellationToken cancellationToken) =>
        await storage.TargetExistsAsync(
            folders,
            targetFilename,
            cancellationToken)
            ? ConflictKind.Name
            : (await store.CheckConflictsAsync(
                targetFilename,
                sha256,
                cancellationToken)).Kind;

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

        return candidate.State == InboxItemState.ReadyToTag
            ? null
            : Result(IngestOutcome.NotReady, "ingest.not_ready");
    }

    private static IngestResult InputFailure(IngestInputException error) =>
        error.ErrorCode switch
        {
            "ingest.source_changed" => Result(
                IngestOutcome.SourceChanged,
                error.ErrorCode),
            "ingest.source_missing" => Result(
                IngestOutcome.SourceMissing,
                error.ErrorCode),
            _ => Result(IngestOutcome.NotReady, error.ErrorCode)
        };

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
