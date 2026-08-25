using CommunityToolkit.Mvvm.ComponentModel;
using TraceFold.Media.Application.Intake;

namespace TraceFold.Media.Desktop.Pages;

public sealed partial class InboxBatchSummaryViewModel(
    IInboxBatchSummaryService summaryService) : ObservableObject
{
    [ObservableProperty]
    public partial bool IsVisible { get; private set; }

    [ObservableProperty]
    public partial int SelectedCount { get; private set; }

    [ObservableProperty]
    public partial string MediaCountText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string StateCountText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanEditMetadata { get; private set; }

    [ObservableProperty]
    public partial bool CanStartProcessing { get; private set; }

    [ObservableProperty]
    public partial bool CanPreviewIngest { get; private set; }

    public async Task LoadAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        InboxBatchSummaryDto summary = await summaryService.SummarizeAsync(
            selection,
            cancellationToken);
        SelectedCount = summary.SelectedCount;
        MediaCountText = $"图片 {summary.ImageCount} · 视频 {summary.VideoCount}";
        StateCountText =
            $"可入库 {summary.ReadyCount} · 需处理 {summary.RequiresProcessingCount} "
            + $"· 冲突 {summary.ConflictCount} · 恢复受阻 {summary.RecoveryBlockedCount} "
            + $"· 其他 {summary.OtherCount}";
        CanEditMetadata = summary.CanEditMetadata;
        CanStartProcessing = summary.CanStartProcessing;
        CanPreviewIngest = summary.CanPreviewIngest;
        IsVisible = summary.SelectedCount >= 2;
    }

    public void Reset()
    {
        IsVisible = false;
        SelectedCount = 0;
        MediaCountText = string.Empty;
        StateCountText = string.Empty;
        CanEditMetadata = false;
        CanStartProcessing = false;
        CanPreviewIngest = false;
    }
}
