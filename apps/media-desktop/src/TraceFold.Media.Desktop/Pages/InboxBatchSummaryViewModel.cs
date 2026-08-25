using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using TraceFold.Media.Application.Ingest;
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

    [ObservableProperty]
    public partial bool IsIngestRunning { get; private set; }

    [ObservableProperty]
    public partial int CompletedItems { get; private set; }

    [ObservableProperty]
    public partial int TotalItems { get; private set; }

    [ObservableProperty]
    public partial double ProgressPercent { get; private set; }

    [ObservableProperty]
    public partial string CurrentItemText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanStopAfterCurrent { get; private set; }

    [ObservableProperty]
    public partial int CompletedCount { get; private set; }

    [ObservableProperty]
    public partial int ConflictCount { get; private set; }

    [ObservableProperty]
    public partial int RequiresProcessingCount { get; private set; }

    [ObservableProperty]
    public partial int FailedOrChangedCount { get; private set; }

    [ObservableProperty]
    public partial int NotStartedCount { get; private set; }

    [ObservableProperty]
    public partial bool HasFinalResult { get; private set; }

    [ObservableProperty]
    public partial bool HasIncompleteItems { get; private set; }

    [ObservableProperty]
    public partial Visibility IngestProgressVisibility { get; private set; } =
        Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility FinalResultVisibility { get; private set; } =
        Visibility.Collapsed;

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

    public void BeginIngest(int totalItems)
    {
        if (totalItems < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(totalItems));
        }

        HasFinalResult = false;
        HasIncompleteItems = false;
        CompletedCount = 0;
        ConflictCount = 0;
        RequiresProcessingCount = 0;
        FailedOrChangedCount = 0;
        NotStartedCount = 0;
        IsIngestRunning = true;
        CompletedItems = 0;
        TotalItems = totalItems;
        ProgressPercent = 0;
        CurrentItemText = "准备开始批量入库。";
        CanStopAfterCurrent = true;
        IngestProgressVisibility = Visibility.Visible;
        FinalResultVisibility = Visibility.Collapsed;
    }

    public void Report(BatchIngestProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        CompletedItems = Math.Clamp(progress.CompletedItems, 0, progress.TotalItems);
        TotalItems = Math.Max(1, progress.TotalItems);
        ProgressPercent = Math.Clamp(
            CompletedItems * 100d / TotalItems,
            0,
            100);
        CurrentItemText = $"正在处理：{progress.SafeName}";
    }

    public void Complete(BatchIngestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        IsIngestRunning = false;
        CanStopAfterCurrent = false;
        IngestProgressVisibility = Visibility.Collapsed;
        CurrentItemText = string.Empty;
        CompletedItems = result.Items.Count;
        TotalItems = result.Items.Count;
        ProgressPercent = result.Items.Count == 0 ? 0 : 100;
        CompletedCount = Count(result, BatchIngestItemOutcome.Completed);
        ConflictCount = Count(result, BatchIngestItemOutcome.ConflictBlocked);
        RequiresProcessingCount = Count(
            result,
            BatchIngestItemOutcome.RequiresProcessing);
        FailedOrChangedCount = result.Items.Count(item => item.Outcome is
            BatchIngestItemOutcome.RecoveryBlocked
            or BatchIngestItemOutcome.ChangedOrInvalid
            or BatchIngestItemOutcome.Failed);
        NotStartedCount = Count(result, BatchIngestItemOutcome.NotStarted);
        HasIncompleteItems = result.Items.Any(item =>
            item.Outcome != BatchIngestItemOutcome.Completed);
        HasFinalResult = true;
        FinalResultVisibility = Visibility.Visible;
    }

    private static int Count(
        BatchIngestResult result,
        BatchIngestItemOutcome outcome) =>
        result.Items.Count(item => item.Outcome == outcome);
}
