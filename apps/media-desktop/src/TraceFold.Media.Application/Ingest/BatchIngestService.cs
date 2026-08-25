using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Application.Ingest;

public sealed class BatchIngestService(
    IInboxSelectionStore selectionStore,
    IIngestService ingest,
    ILogger<BatchIngestService>? logger = null) : IBatchIngestService
{
    public async Task<BatchIngestPreviewDto> PreviewAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        IReadOnlyList<InboxSelectionItemDto> current = await selectionStore.LoadAsync(
            selection, cancellationToken);
        Dictionary<Guid, InboxSelectionItemDto> byId = current
            .GroupBy(item => item.InboxItemId)
            .ToDictionary(group => group.Key, group => group.First());
        return new BatchIngestPreviewDto(selection
            .OrderBy(entry => entry.Ordinal)
            .Select(entry =>
            {
                byId.TryGetValue(
                    entry.InboxItemId, out InboxSelectionItemDto? item);
                return new BatchIngestPreviewItemDto(
                    entry.InboxItemId,
                    entry.Ordinal,
                    item?.SafeName ?? string.Empty,
                    PreviewCategory(entry, item));
            })
            .ToArray());
    }

    public async Task<BatchIngestResult> ExecuteAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        IProgress<BatchIngestProgress>? progress,
        CancellationToken stopAfterCurrent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        Guid batchId = Guid.NewGuid();
        long started = Stopwatch.GetTimestamp();
        logger?.LogInformation(
            "Batch ingest started BatchId {BatchId} SelectedCount {SelectedCount} Phase {Phase}",
            batchId,
            selection.Count,
            "start");
        InboxSelectionEntry[] ordered = selection
            .OrderBy(entry => entry.Ordinal)
            .ToArray();
        var items = new List<BatchIngestItemResult>(ordered.Length);
        bool stopped = false;
        for (int index = 0; index < ordered.Length; index++)
        {
            InboxSelectionEntry entry = ordered[index];
            if (stopAfterCurrent.IsCancellationRequested)
            {
                stopped = true;
                AppendNotStarted(items, ordered, index);
                break;
            }

            try
            {
                IReadOnlyList<InboxSelectionItemDto> loaded =
                    await selectionStore.LoadAsync([entry], cancellationToken);
                InboxSelectionItemDto? current = loaded.Count switch
                {
                    0 => null,
                    1 => loaded[0],
                    _ => throw new InvalidOperationException(
                        "A selection item resolved more than once.")
                };
                BatchIngestItemResult? rejected = Revalidate(entry, current);
                if (rejected is not null)
                {
                    items.Add(rejected);
                    continue;
                }

                string safeName = current!.SafeName;
                progress?.Report(new BatchIngestProgress(
                    items.Count,
                    ordered.Length,
                    entry.InboxItemId,
                    safeName));
                IngestResult single = await ingest.IngestAsync(
                    entry.InboxItemId, cancellationToken);
                items.Add(Map(entry, single));
                progress?.Report(new BatchIngestProgress(
                    items.Count,
                    ordered.Length,
                    entry.InboxItemId,
                    safeName));
                if (single.Outcome == IngestOutcome.ShuttingDown)
                {
                    AppendNotStarted(items, ordered, index + 1);
                    break;
                }
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                items.Add(new BatchIngestItemResult(
                    entry.InboxItemId,
                    entry.Ordinal,
                    BatchIngestItemOutcome.Failed,
                    "batch.ingest.orchestration_failed"));
                AppendNotStarted(items, ordered, index + 1);
                break;
            }
        }

        return Finish(
            new BatchIngestResult(items, stopped),
            batchId,
            started,
            logger);
    }

    private static BatchIngestResult Finish(
        BatchIngestResult result,
        Guid batchId,
        long started,
        ILogger<BatchIngestService>? logger)
    {
        int completed = Count(result, BatchIngestItemOutcome.Completed);
        int conflict = Count(result, BatchIngestItemOutcome.ConflictBlocked);
        int processing = Count(
            result,
            BatchIngestItemOutcome.RequiresProcessing);
        int recovery = Count(result, BatchIngestItemOutcome.RecoveryBlocked);
        int changed = Count(result, BatchIngestItemOutcome.ChangedOrInvalid);
        int failed = Count(result, BatchIngestItemOutcome.Failed);
        int notStarted = Count(result, BatchIngestItemOutcome.NotStarted);
        string? errorCode = failed > 0
            ? "batch.ingest.failed"
            : conflict > 0 ? "batch.ingest.conflict_blocked"
            : processing > 0 ? "batch.ingest.requires_processing"
            : recovery > 0 ? "batch.ingest.recovery_blocked"
            : changed > 0 ? "batch.ingest.changed_or_invalid"
            : notStarted > 0 ? "batch.ingest.not_started"
            : null;
        logger?.LogInformation(
            "Batch ingest finished BatchId {BatchId} Phase {Phase} SelectedCount {SelectedCount} CompletedCount {CompletedCount} ConflictCount {ConflictCount} RequiresProcessingCount {RequiresProcessingCount} RecoveryBlockedCount {RecoveryBlockedCount} ChangedCount {ChangedCount} FailedCount {FailedCount} NotStartedCount {NotStartedCount} StoppedByUser {StoppedByUser} ErrorCode {ErrorCode} DurationMs {DurationMs}",
            batchId,
            "finish",
            result.Items.Count,
            completed,
            conflict,
            processing,
            recovery,
            changed,
            failed,
            notStarted,
            result.StoppedByUser,
            errorCode,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    private static int Count(
        BatchIngestResult result,
        BatchIngestItemOutcome outcome) =>
        result.Items.Count(item => item.Outcome == outcome);

    private static BatchIngestPreviewCategory PreviewCategory(
        InboxSelectionEntry entry,
        InboxSelectionItemDto? current)
    {
        if (current is null
            || current.MetadataVersion != entry.ExpectedMetadataVersion)
        {
            return BatchIngestPreviewCategory.ChangedOrInvalid;
        }

        if (current.AllowedActions.HasFlag(InboxAction.Ingest))
        {
            return BatchIngestPreviewCategory.Ready;
        }

        return current.State switch
        {
            InboxItemState.RequiresProcessing
                or InboxItemState.ProcessingQueued
                or InboxItemState.Processing
                or InboxItemState.ProcessingInterrupted =>
                BatchIngestPreviewCategory.RequiresProcessing,
            InboxItemState.ConflictBlocked =>
                BatchIngestPreviewCategory.ConflictBlocked,
            InboxItemState.RecoveryBlocked or InboxItemState.CleanupPending =>
                BatchIngestPreviewCategory.RecoveryBlocked,
            _ => BatchIngestPreviewCategory.ChangedOrInvalid
        };
    }

    private static BatchIngestItemResult? Revalidate(
        InboxSelectionEntry entry,
        InboxSelectionItemDto? current)
    {
        BatchIngestPreviewCategory category = PreviewCategory(entry, current);
        return category switch
        {
            BatchIngestPreviewCategory.Ready => null,
            BatchIngestPreviewCategory.RequiresProcessing => Result(
                entry,
                BatchIngestItemOutcome.RequiresProcessing,
                "batch.ingest.requires_processing"),
            BatchIngestPreviewCategory.ConflictBlocked => Result(
                entry,
                BatchIngestItemOutcome.ConflictBlocked,
                "batch.ingest.conflict_blocked"),
            BatchIngestPreviewCategory.RecoveryBlocked => Result(
                entry,
                BatchIngestItemOutcome.RecoveryBlocked,
                "batch.ingest.recovery_blocked"),
            _ => Result(
                entry,
                BatchIngestItemOutcome.ChangedOrInvalid,
                "batch.ingest.changed_or_invalid")
        };
    }

    private static BatchIngestItemResult Map(
        InboxSelectionEntry entry,
        IngestResult single)
    {
        BatchIngestItemOutcome outcome = single.Outcome switch
        {
            IngestOutcome.Completed => BatchIngestItemOutcome.Completed,
            IngestOutcome.RequiresProcessing =>
                BatchIngestItemOutcome.RequiresProcessing,
            IngestOutcome.ConflictBlocked =>
                BatchIngestItemOutcome.ConflictBlocked,
            IngestOutcome.CleanupPending or IngestOutcome.InProgress =>
                BatchIngestItemOutcome.RecoveryBlocked,
            IngestOutcome.SourceMissing
                or IngestOutcome.SourceChanged
                or IngestOutcome.NotReady
                or IngestOutcome.ConflictChanged
                or IngestOutcome.Skipped =>
                BatchIngestItemOutcome.ChangedOrInvalid,
            IngestOutcome.Failed => BatchIngestItemOutcome.Failed,
            IngestOutcome.ShuttingDown => BatchIngestItemOutcome.NotStarted,
            _ => BatchIngestItemOutcome.ChangedOrInvalid
        };
        return Result(
            entry,
            outcome,
            outcome == BatchIngestItemOutcome.Completed ? null : single.Message);
    }

    private static BatchIngestItemResult Result(
        InboxSelectionEntry entry,
        BatchIngestItemOutcome outcome,
        string? errorCode) =>
        new(entry.InboxItemId, entry.Ordinal, outcome, errorCode);

    private static void AppendNotStarted(
        ICollection<BatchIngestItemResult> items,
        IReadOnlyList<InboxSelectionEntry> ordered,
        int startIndex)
    {
        for (int index = startIndex; index < ordered.Count; index++)
        {
            items.Add(Result(
                ordered[index],
                BatchIngestItemOutcome.NotStarted,
                "batch.ingest.not_started"));
        }
    }
}
