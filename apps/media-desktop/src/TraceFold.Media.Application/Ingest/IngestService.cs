using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Application.Ingest;

public class IngestService(
    IInboxScanner scanner,
    IIngestStore store,
    IDirectorySettingsStore settingsStore,
    IIngestInputResolver inputResolver,
    IConflictDetectionService detector,
    IIngestExecutionService executor,
    IMediaOperationCoordinator coordinator,
    ILogger<IngestService> logger) : IIngestService
{
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

                IngestPreparation preparation = IngestPreparation.From(
                    candidate!,
                    input);
                ConflictCaseRecord? conflict = await detector.DetectAsync(
                    preparation,
                    folders,
                    operationToken);
                if (conflict is not null)
                {
                    return Result(
                        IngestOutcome.ConflictBlocked,
                        "ingest.conflict",
                        conflictCaseId: conflict.Id);
                }

                return await executor.ExecuteAsync(
                    preparation,
                    IngestExecutionPlan.Create(candidate!.ActualFilename),
                    folders,
                    operationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("IngestStopped");
                return Result(
                    IngestOutcome.ShuttingDown,
                    "ingest.shutting_down");
            }
            catch (Exception error)
            {
                logger.LogError(
                    "IngestFailed InboxItemId={InboxItemId} ErrorCode={ErrorCode} ExceptionType={ExceptionType} HResult={HResult}",
                    inboxItemId,
                    "ingest.commit_failed",
                    error.GetType().FullName,
                    error.HResult);
                return Result(
                    IngestOutcome.Failed,
                    "ingest.commit_failed");
            }
        }
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
                operation.AssetId,
                operation.ConflictCaseId),
            IngestOperationStage.DatabaseCommitted
                or IngestOperationStage.CleanupPending => Result(
                    IngestOutcome.CleanupPending,
                    operation.ErrorCode ?? "ingest.cleanup_failed",
                    operation.Id,
                    operation.AssetId,
                    operation.ConflictCaseId),
            IngestOperationStage.Failed => Result(
                IngestOutcome.Failed,
                operation.ErrorCode ?? "ingest.commit_failed",
                operation.Id,
                operation.AssetId,
                operation.ConflictCaseId),
            _ => Result(
                IngestOutcome.InProgress,
                "ingest.in_progress",
                operation.Id,
                operation.AssetId,
                operation.ConflictCaseId)
        };

    private static IngestResult Result(
        IngestOutcome outcome,
        string message,
        Guid? operationId = null,
        Guid? assetId = null,
        Guid? conflictCaseId = null) =>
        new(outcome, operationId, assetId, conflictCaseId, message);
}
