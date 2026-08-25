using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Application.Processing;

public sealed class BatchProcessingService(
    IInboxSelectionStore selectionStore,
    IProcessingCommandService commands,
    ILogger<BatchProcessingService>? logger = null) : IBatchProcessingService
{
    public async Task<BatchProcessingResult> StartAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        Guid batchId = Guid.NewGuid();
        long started = Stopwatch.GetTimestamp();
        logger?.LogInformation(
            "Batch processing started BatchId {BatchId} SelectedCount {SelectedCount} Phase {Phase}",
            batchId,
            selection.Count,
            "start");
        InboxSelectionEntry[] ordered = selection
            .OrderBy(item => item.Ordinal)
            .ToArray();
        IReadOnlyList<InboxSelectionItemDto> current;
        try
        {
            current = await selectionStore.LoadAsync(
                selection, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Finish(
                new BatchProcessingResult(NotStarted(ordered)),
                batchId,
                started,
                logger);
        }

        Dictionary<Guid, InboxSelectionItemDto> byId = current
            .GroupBy(item => item.InboxItemId)
            .ToDictionary(group => group.Key, group => group.First());
        var results = new List<BatchProcessingItemResult>(ordered.Length);
        for (int index = 0; index < ordered.Length; index++)
        {
            InboxSelectionEntry entry = ordered[index];
            if (cancellationToken.IsCancellationRequested)
            {
                AppendNotStarted(results, ordered, index);
                break;
            }

            if (!byId.TryGetValue(entry.InboxItemId, out InboxSelectionItemDto? item))
            {
                results.Add(new BatchProcessingItemResult(
                    entry.InboxItemId,
                    entry.Ordinal,
                    BatchProcessingItemOutcome.NeedsAttention,
                    "batch.processing.selection_missing"));
                continue;
            }

            if (item.AllowedActions.HasFlag(InboxAction.CancelProcessing)
                || item.AllowedActions.HasFlag(InboxAction.StopProcessing))
            {
                results.Add(new BatchProcessingItemResult(
                    entry.InboxItemId,
                    entry.Ordinal,
                    BatchProcessingItemOutcome.AlreadyActive,
                    null));
                continue;
            }

            if (!item.AllowedActions.HasFlag(InboxAction.StartProcessing))
            {
                results.Add(new BatchProcessingItemResult(
                    entry.InboxItemId,
                    entry.Ordinal,
                    BatchProcessingItemOutcome.NotRequired,
                    null));
                continue;
            }

            ProcessingCommandResult command;
            try
            {
                command = await commands.StartAsync(
                    entry.InboxItemId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                AppendNotStarted(results, ordered, index);
                break;
            }

            BatchProcessingItemOutcome outcome = Map(command.Outcome);
            results.Add(new BatchProcessingItemResult(
                entry.InboxItemId,
                entry.Ordinal,
                outcome,
                outcome is BatchProcessingItemOutcome.NeedsAttention
                    or BatchProcessingItemOutcome.NotStarted
                    ? command.Message
                    : null));
            if (command.Outcome == ProcessingCommandOutcome.ShuttingDown)
            {
                AppendNotStarted(results, ordered, index + 1);
                break;
            }
        }

        return Finish(
            new BatchProcessingResult(results),
            batchId,
            started,
            logger);
    }

    private static BatchProcessingResult Finish(
        BatchProcessingResult result,
        Guid batchId,
        long started,
        ILogger<BatchProcessingService>? logger)
    {
        int queued = Count(result, BatchProcessingItemOutcome.Queued);
        int active = Count(result, BatchProcessingItemOutcome.AlreadyActive);
        int notRequired = Count(result, BatchProcessingItemOutcome.NotRequired);
        int needsAttention = Count(
            result,
            BatchProcessingItemOutcome.NeedsAttention);
        int notStarted = Count(result, BatchProcessingItemOutcome.NotStarted);
        string? errorCode = needsAttention > 0
            ? "batch.processing.needs_attention"
            : notStarted > 0 ? "batch.processing.not_started" : null;
        logger?.LogInformation(
            "Batch processing finished BatchId {BatchId} Phase {Phase} SelectedCount {SelectedCount} QueuedCount {QueuedCount} AlreadyActiveCount {AlreadyActiveCount} NotRequiredCount {NotRequiredCount} NeedsAttentionCount {NeedsAttentionCount} NotStartedCount {NotStartedCount} ErrorCode {ErrorCode} DurationMs {DurationMs}",
            batchId,
            "finish",
            result.Items.Count,
            queued,
            active,
            notRequired,
            needsAttention,
            notStarted,
            errorCode,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    private static int Count(
        BatchProcessingResult result,
        BatchProcessingItemOutcome outcome) =>
        result.Items.Count(item => item.Outcome == outcome);

    private static BatchProcessingItemOutcome Map(ProcessingCommandOutcome value) =>
        value switch
        {
            ProcessingCommandOutcome.Queued => BatchProcessingItemOutcome.Queued,
            ProcessingCommandOutcome.AlreadyActive =>
                BatchProcessingItemOutcome.AlreadyActive,
            ProcessingCommandOutcome.NotEligible =>
                BatchProcessingItemOutcome.NeedsAttention,
            ProcessingCommandOutcome.SourceMissing or
            ProcessingCommandOutcome.SourceChanged or
            ProcessingCommandOutcome.ToolUnavailable or
            ProcessingCommandOutcome.Failed =>
                BatchProcessingItemOutcome.NeedsAttention,
            ProcessingCommandOutcome.ShuttingDown =>
                BatchProcessingItemOutcome.NotStarted,
            _ => BatchProcessingItemOutcome.NeedsAttention
        };

    private static IReadOnlyList<BatchProcessingItemResult> NotStarted(
        IReadOnlyList<InboxSelectionEntry> entries) =>
        entries.Select(entry => new BatchProcessingItemResult(
            entry.InboxItemId,
            entry.Ordinal,
            BatchProcessingItemOutcome.NotStarted,
            "batch.processing.not_started")).ToArray();

    private static void AppendNotStarted(
        ICollection<BatchProcessingItemResult> results,
        IReadOnlyList<InboxSelectionEntry> entries,
        int startIndex)
    {
        for (int index = startIndex; index < entries.Count; index++)
        {
            InboxSelectionEntry entry = entries[index];
            results.Add(new BatchProcessingItemResult(
                entry.InboxItemId,
                entry.Ordinal,
                BatchProcessingItemOutcome.NotStarted,
                "batch.processing.not_started"));
        }
    }
}
