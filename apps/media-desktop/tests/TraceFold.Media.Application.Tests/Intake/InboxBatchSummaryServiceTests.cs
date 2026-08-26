using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;
using Xunit;

namespace TraceFold.Media.Application.Tests.Intake;

public sealed class InboxBatchSummaryServiceTests
{
    [Fact]
    public async Task Summary_uses_current_actions_and_exposes_stale_selection()
    {
        Guid image = Guid.NewGuid();
        Guid video = Guid.NewGuid();
        InboxSelectionEntry[] selection =
        [
            new(image, 2, 0),
            new(video, 4, 1)
        ];
        var store = new FakeInboxSelectionStore(
            selection,
            Item(
                image,
                MediaKind.Image,
                InboxItemState.ReadyToTag,
                InboxAction.EditMetadata | InboxAction.Ingest,
                currentVersion: 2,
                ordinal: 0),
            Item(
                video,
                MediaKind.Video,
                InboxItemState.RequiresProcessing,
                InboxAction.EditMetadata | InboxAction.StartProcessing,
                currentVersion: 5,
                ordinal: 1));
        var service = new InboxBatchSummaryService(store);

        InboxBatchSummaryDto summary = await service.SummarizeAsync(
            store.Selection, TestContext.Current.CancellationToken);

        Assert.Equal(2, summary.SelectedCount);
        Assert.Equal(1, summary.ImageCount);
        Assert.Equal(1, summary.VideoCount);
        Assert.Equal(1, summary.ReadyCount);
        Assert.Equal(1, summary.RequiresProcessingCount);
        Assert.Equal(1, summary.StaleCount);
        Assert.False(summary.CanEditMetadata);
        Assert.True(summary.CanStartProcessing);
        Assert.True(summary.CanPreviewIngest);
    }

    [Fact]
    public async Task Missing_and_non_primary_states_are_counted_as_other()
    {
        Guid recovery = Guid.NewGuid();
        Guid cleanup = Guid.NewGuid();
        Guid missing = Guid.NewGuid();
        InboxSelectionEntry[] selection =
        [
            new(recovery, 0, 0),
            new(cleanup, 0, 1),
            new(missing, 0, 2)
        ];
        var store = new FakeInboxSelectionStore(
            selection,
            Item(
                recovery,
                MediaKind.Video,
                InboxItemState.RecoveryBlocked,
                InboxAction.Preview,
                0,
                0),
            Item(
                cleanup,
                MediaKind.Image,
                InboxItemState.CleanupPending,
                InboxAction.Preview,
                0,
                1));
        var service = new InboxBatchSummaryService(store);

        InboxBatchSummaryDto summary = await service.SummarizeAsync(
            selection, TestContext.Current.CancellationToken);

        Assert.Equal(1, summary.RecoveryBlockedCount);
        Assert.Equal(2, summary.OtherCount);
        Assert.False(summary.CanEditMetadata);
        Assert.True(summary.CanPreviewIngest);
    }

    [Fact]
    public async Task Metadata_edit_requires_two_current_non_stale_editable_items()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        InboxSelectionEntry[] selection = [new(first, 0, 0), new(second, 0, 1)];
        var store = new FakeInboxSelectionStore(
            selection,
            Item(first, MediaKind.Image, InboxItemState.ReadyToTag,
                InboxAction.EditMetadata, 0, 0),
            Item(second, MediaKind.Video, InboxItemState.ConflictBlocked,
                InboxAction.EditMetadata, 0, 1));
        var service = new InboxBatchSummaryService(store);

        InboxBatchSummaryDto summary = await service.SummarizeAsync(
            selection, TestContext.Current.CancellationToken);

        Assert.True(summary.CanEditMetadata);
        Assert.False(summary.CanStartProcessing);
        Assert.True(summary.CanPreviewIngest);
        Assert.Equal(1, summary.ConflictCount);
    }

    private static InboxSelectionItemDto Item(
        Guid id,
        MediaKind mediaKind,
        InboxItemState state,
        InboxAction actions,
        long currentVersion,
        int ordinal) =>
        new(id, currentVersion, ordinal, mediaKind, state, actions, $"item-{ordinal}");

    private sealed class FakeInboxSelectionStore(
        IReadOnlyList<InboxSelectionEntry> selection,
        params InboxSelectionItemDto[] items) : IInboxSelectionStore
    {
        public IReadOnlyList<InboxSelectionEntry> Selection { get; } = selection;

        public Task<IReadOnlyList<InboxSelectionItemDto>> LoadAsync(
            IReadOnlyList<InboxSelectionEntry> requested,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InboxSelectionItemDto>>(items);
    }
}
