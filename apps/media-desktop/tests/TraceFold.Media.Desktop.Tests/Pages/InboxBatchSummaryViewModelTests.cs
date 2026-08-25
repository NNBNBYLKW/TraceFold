using TraceFold.Media.Application.Intake;
using TraceFold.Media.Desktop.Pages;
using Microsoft.UI.Xaml;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

public sealed class InboxBatchSummaryViewModelTests
{
    [Fact]
    public async Task Load_formats_counts_without_reproducing_item_names()
    {
        var service = new FixedSummaryService(new InboxBatchSummaryDto(
            5, 3, 2, 2, 1, 1, 0, 1, 0,
            CanEditMetadata: true,
            CanStartProcessing: true,
            CanPreviewIngest: true));
        var viewModel = new InboxBatchSummaryViewModel(service);

        await viewModel.LoadAsync(
            Selection(5), TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsVisible);
        Assert.Equal(5, viewModel.SelectedCount);
        Assert.Equal("图片 3 · 视频 2", viewModel.MediaCountText);
        Assert.Equal("可入库 2 · 需处理 1 · 冲突 1 · 恢复受阻 0 · 其他 1",
            viewModel.StateCountText);
        Assert.DoesNotContain("private.jpg", viewModel.MediaCountText);
        Assert.True(viewModel.CanEditMetadata);
        Assert.True(viewModel.CanStartProcessing);
        Assert.True(viewModel.CanPreviewIngest);
    }

    [Fact]
    public async Task Reset_hides_and_clears_the_summary()
    {
        var viewModel = new InboxBatchSummaryViewModel(
            new FixedSummaryService(new InboxBatchSummaryDto(
                2, 1, 1, 2, 0, 0, 0, 0, 0, true, false, true)));
        await viewModel.LoadAsync(
            Selection(2), TestContext.Current.CancellationToken);

        viewModel.Reset();

        Assert.False(viewModel.IsVisible);
        Assert.Equal(0, viewModel.SelectedCount);
        Assert.Equal(string.Empty, viewModel.MediaCountText);
        Assert.Equal(string.Empty, viewModel.StateCountText);
        Assert.False(viewModel.CanEditMetadata);
    }

    [Fact]
    public void End_ingest_clears_running_progress_without_final_result()
    {
        var viewModel = new InboxBatchSummaryViewModel(
            new FixedSummaryService(new InboxBatchSummaryDto(
                2, 1, 1, 2, 0, 0, 0, 0, 0, true, false, true)));
        viewModel.BeginIngest(2);

        viewModel.EndIngest();

        Assert.False(viewModel.IsIngestRunning);
        Assert.False(viewModel.CanStopAfterCurrent);
        Assert.Equal(Visibility.Collapsed, viewModel.IngestProgressVisibility);
        Assert.False(viewModel.HasFinalResult);
    }

    private static IReadOnlyList<InboxSelectionEntry> Selection(int count) =>
        Enumerable.Range(0, count)
            .Select(ordinal => new InboxSelectionEntry(
                Guid.NewGuid(), 0, ordinal))
            .ToArray();

    private sealed class FixedSummaryService(InboxBatchSummaryDto summary)
        : IInboxBatchSummaryService
    {
        public Task<InboxBatchSummaryDto> SummarizeAsync(
            IReadOnlyList<InboxSelectionEntry> selection,
            CancellationToken cancellationToken) =>
            Task.FromResult(summary);
    }
}
