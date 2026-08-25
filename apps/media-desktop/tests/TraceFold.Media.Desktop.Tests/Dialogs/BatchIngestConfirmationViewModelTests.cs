using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Desktop.Dialogs;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Dialogs;

public sealed class BatchIngestConfirmationViewModelTests
{
    [Fact]
    public void Shows_all_categories_and_disables_confirm_without_ready_items()
    {
        BatchIngestPreviewDto preview = Preview(
            ready: 0,
            processing: 2,
            conflicts: 1,
            recovery: 1,
            changed: 3);
        var viewModel = new BatchIngestConfirmationViewModel(preview);

        Assert.False(viewModel.CanConfirm);
        Assert.Equal("可直接入库 0", viewModel.ReadyText);
        Assert.Equal("需要前置处理 2", viewModel.RequiresProcessingText);
        Assert.Equal("存在冲突 1", viewModel.ConflictText);
        Assert.Equal("恢复受阻 1", viewModel.RecoveryText);
        Assert.Equal("变化或不可操作 3", viewModel.ChangedText);
    }

    private static BatchIngestPreviewDto Preview(
        int ready,
        int processing,
        int conflicts,
        int recovery,
        int changed)
    {
        var items = new List<BatchIngestPreviewItemDto>();
        Add(items, ready, BatchIngestPreviewCategory.Ready);
        Add(items, processing, BatchIngestPreviewCategory.RequiresProcessing);
        Add(items, conflicts, BatchIngestPreviewCategory.ConflictBlocked);
        Add(items, recovery, BatchIngestPreviewCategory.RecoveryBlocked);
        Add(items, changed, BatchIngestPreviewCategory.ChangedOrInvalid);
        return new(items);
    }

    private static void Add(
        List<BatchIngestPreviewItemDto> items,
        int count,
        BatchIngestPreviewCategory category)
    {
        for (int index = 0; index < count; index++)
        {
            items.Add(new(
                Guid.NewGuid(),
                items.Count,
                $"item-{items.Count}",
                category));
        }
    }
}
