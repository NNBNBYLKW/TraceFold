using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Application.Intake;

public sealed record InboxBatchSummaryDto(
    int SelectedCount,
    int ImageCount,
    int VideoCount,
    int ReadyCount,
    int RequiresProcessingCount,
    int ConflictCount,
    int RecoveryBlockedCount,
    int OtherCount,
    int StaleCount,
    bool CanEditMetadata,
    bool CanStartProcessing,
    bool CanPreviewIngest);

public interface IInboxBatchSummaryService
{
    Task<InboxBatchSummaryDto> SummarizeAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken);
}

public sealed class InboxBatchSummaryService(IInboxSelectionStore store)
    : IInboxBatchSummaryService
{
    public async Task<InboxBatchSummaryDto> SummarizeAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        IReadOnlyList<InboxSelectionItemDto> current = await store.LoadAsync(
            selection, cancellationToken);
        Dictionary<Guid, InboxSelectionEntry> expected = selection
            .GroupBy(entry => entry.InboxItemId)
            .ToDictionary(group => group.Key, group => group.First());
        int stale = current.Count(item =>
            !expected.TryGetValue(item.InboxItemId, out InboxSelectionEntry? entry)
            || entry.ExpectedMetadataVersion != item.MetadataVersion);
        int missing = selection
            .Select(entry => entry.InboxItemId)
            .Distinct()
            .Count(id => current.All(item => item.InboxItemId != id));
        int other = missing + current.Count(item => item.State is not (
            InboxItemState.ReadyToTag
            or InboxItemState.RequiresProcessing
            or InboxItemState.ConflictBlocked
            or InboxItemState.RecoveryBlocked));
        bool allCurrent = current.Count == selection.Count;
        bool canEdit = selection.Count >= 2
            && allCurrent
            && stale == 0
            && current.All(item =>
                item.AllowedActions.HasFlag(InboxAction.EditMetadata));

        return new InboxBatchSummaryDto(
            selection.Count,
            current.Count(item => item.MediaKind == MediaKind.Image),
            current.Count(item => item.MediaKind == MediaKind.Video),
            current.Count(item => item.State == InboxItemState.ReadyToTag),
            current.Count(item => item.State == InboxItemState.RequiresProcessing),
            current.Count(item => item.State == InboxItemState.ConflictBlocked),
            current.Count(item => item.State == InboxItemState.RecoveryBlocked),
            other,
            stale,
            canEdit,
            current.Any(item =>
                item.AllowedActions.HasFlag(InboxAction.StartProcessing)),
            current.Count >= 2);
    }
}
