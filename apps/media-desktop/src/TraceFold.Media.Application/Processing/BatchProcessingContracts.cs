using TraceFold.Media.Application.Intake;

namespace TraceFold.Media.Application.Processing;

public enum BatchProcessingItemOutcome
{
    Queued,
    AlreadyActive,
    NotRequired,
    NeedsAttention,
    NotStarted
}

public sealed record BatchProcessingItemResult(
    Guid InboxItemId,
    int Ordinal,
    BatchProcessingItemOutcome Outcome,
    string? ErrorCode);

public sealed record BatchProcessingResult(
    IReadOnlyList<BatchProcessingItemResult> Items)
{
    public int QueuedCount => Items.Count(item =>
        item.Outcome == BatchProcessingItemOutcome.Queued);
}

public interface IBatchProcessingService
{
    Task<BatchProcessingResult> StartAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken);
}
