using TraceFold.Media.Application.Intake;

namespace TraceFold.Media.Application.Ingest;

public enum BatchIngestPreviewCategory
{
    Ready,
    RequiresProcessing,
    ConflictBlocked,
    RecoveryBlocked,
    ChangedOrInvalid
}

public sealed record BatchIngestPreviewItemDto(
    Guid InboxItemId,
    int Ordinal,
    string SafeName,
    BatchIngestPreviewCategory Category);

public sealed record BatchIngestPreviewDto(
    IReadOnlyList<BatchIngestPreviewItemDto> Items)
{
    public int ReadyCount => Items.Count(item =>
        item.Category == BatchIngestPreviewCategory.Ready);

    public int RequiresProcessingCount => Items.Count(item =>
        item.Category == BatchIngestPreviewCategory.RequiresProcessing);

    public int ConflictCount => Items.Count(item =>
        item.Category == BatchIngestPreviewCategory.ConflictBlocked);

    public int RecoveryBlockedCount => Items.Count(item =>
        item.Category == BatchIngestPreviewCategory.RecoveryBlocked);

    public int ChangedOrInvalidCount => Items.Count(item =>
        item.Category == BatchIngestPreviewCategory.ChangedOrInvalid);
}

public enum BatchIngestItemOutcome
{
    Completed,
    RequiresProcessing,
    ConflictBlocked,
    RecoveryBlocked,
    ChangedOrInvalid,
    Failed,
    NotStarted
}

public sealed record BatchIngestItemResult(
    Guid InboxItemId,
    int Ordinal,
    BatchIngestItemOutcome Outcome,
    string? ErrorCode);

public sealed record BatchIngestProgress(
    int CompletedItems,
    int TotalItems,
    int CurrentPosition,
    Guid CurrentInboxItemId,
    string SafeName);

public sealed record BatchIngestResult(
    IReadOnlyList<BatchIngestItemResult> Items,
    bool StoppedByUser)
{
    public int CompletedCount => Items.Count(item =>
        item.Outcome == BatchIngestItemOutcome.Completed);

    public int ConflictCount => Items.Count(item =>
        item.Outcome == BatchIngestItemOutcome.ConflictBlocked);
}

public interface IBatchIngestService
{
    Task<BatchIngestPreviewDto> PreviewAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken);

    Task<BatchIngestResult> ExecuteAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        IProgress<BatchIngestProgress>? progress,
        CancellationToken stopAfterCurrent,
        CancellationToken cancellationToken);
}
