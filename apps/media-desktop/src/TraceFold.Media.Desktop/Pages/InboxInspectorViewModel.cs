using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Desktop.Pages;

public sealed partial class InboxInspectorViewModel(
    MetadataEditorViewModel metadata) : ObservableObject
{
    [ObservableProperty]
    public partial Guid? InboxItemId { get; private set; }

    [ObservableProperty]
    public partial string ActualFilename { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial InboxItemState? State { get; private set; }

    [ObservableProperty]
    public partial PreviewDescriptor? Preview { get; private set; }

    [ObservableProperty]
    public partial InboxAction AllowedActions { get; private set; }

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorCode { get; private set; }

    [ObservableProperty]
    public partial string? RequiredCapability { get; private set; }

    [ObservableProperty]
    public partial string ProcessorName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ProcessingStatusText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ProcessingStepText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial double ProcessingProgressPercent { get; private set; }

    [ObservableProperty]
    public partial Visibility ProcessingVisibility { get; private set; } =
        Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility StartProcessingVisibility { get; private set; } =
        Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility CancelProcessingVisibility { get; private set; } =
        Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility StopProcessingVisibility { get; private set; } =
        Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility RetryProcessingVisibility { get; private set; } =
        Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility ResolveConflictVisibility { get; private set; } =
        Visibility.Collapsed;

    [ObservableProperty]
    public partial string RecoveryStatusText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial Visibility RecoveryVisibility { get; private set; } =
        Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility RecheckRecoveryVisibility { get; private set; } =
        Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility RetryCleanupVisibility { get; private set; } =
        Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility OpenRecoveryLocationVisibility { get; private set; } =
        Visibility.Collapsed;

    public MetadataEditorViewModel Metadata { get; } = metadata;

    public async Task LoadAsync(
        InboxDetailsDto details,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        await Metadata.LoadAsync(
            new MetadataOwner(MetadataOwnerKind.InboxItem, details.Card.Id),
            cancellationToken);
        InboxItemId = details.Card.Id;
        ActualFilename = details.Card.ActualFilename;
        State = details.Card.State;
        Preview = details.Preview;
        AllowedActions = details.AllowedActions;
        ErrorCode = details.ErrorCode;
        RequiredCapability = details.RequiredCapability;
        StatusMessage = StateMessage(details.Card.State);
        ApplyProcessing(details);
        ApplyRecovery(details);
    }

    public void Reset()
    {
        InboxItemId = null;
        ActualFilename = string.Empty;
        State = null;
        Preview = null;
        AllowedActions = InboxAction.None;
        StatusMessage = string.Empty;
        ErrorCode = null;
        RequiredCapability = null;
        ProcessorName = string.Empty;
        ProcessingStatusText = string.Empty;
        ProcessingStepText = string.Empty;
        ProcessingProgressPercent = 0;
        ProcessingVisibility = Visibility.Collapsed;
        StartProcessingVisibility = Visibility.Collapsed;
        CancelProcessingVisibility = Visibility.Collapsed;
        StopProcessingVisibility = Visibility.Collapsed;
        RetryProcessingVisibility = Visibility.Collapsed;
        ResolveConflictVisibility = Visibility.Collapsed;
        RecoveryStatusText = string.Empty;
        RecoveryVisibility = Visibility.Collapsed;
        RecheckRecoveryVisibility = Visibility.Collapsed;
        RetryCleanupVisibility = Visibility.Collapsed;
        OpenRecoveryLocationVisibility = Visibility.Collapsed;
    }

    private void ApplyProcessing(InboxDetailsDto details)
    {
        ProcessingSummaryDto? processing = details.Processing;
        ProcessorName = processing?.ProcessorDisplayName ?? string.Empty;
        ProcessingStatusText = details.Card.State switch
        {
            InboxItemState.ProcessingQueued => "排队中",
            InboxItemState.Processing => "处理中",
            InboxItemState.ProcessingInterrupted => "已中断",
            InboxItemState.Failed when processing is not null => "处理失败",
            InboxItemState.RequiresProcessing => "等待开始",
            _ => string.Empty
        };
        ProcessingStepText = processing?.StepText ?? string.Empty;
        ProcessingProgressPercent = processing?.ProgressPercent ?? 0;
        ProcessingVisibility = details.Card.State is
            InboxItemState.RequiresProcessing or
            InboxItemState.ProcessingQueued or
            InboxItemState.Processing or
            InboxItemState.ProcessingInterrupted
            || (details.Card.State == InboxItemState.Failed
                && processing is not null)
                ? Visibility.Visible
                : Visibility.Collapsed;
        StartProcessingVisibility = ActionVisibility(
            details.AllowedActions,
            InboxAction.StartProcessing);
        CancelProcessingVisibility = ActionVisibility(
            details.AllowedActions,
            InboxAction.CancelProcessing);
        StopProcessingVisibility = ActionVisibility(
            details.AllowedActions,
            InboxAction.StopProcessing);
        RetryProcessingVisibility = ActionVisibility(
            details.AllowedActions,
            InboxAction.RetryProcessing);
        ResolveConflictVisibility = ActionVisibility(
            details.AllowedActions,
            InboxAction.ResolveConflict);
    }

    private static Visibility ActionVisibility(
        InboxAction allowedActions,
        InboxAction action) =>
        allowedActions.HasFlag(action)
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void ApplyRecovery(InboxDetailsDto details)
    {
        RecoverySummaryDto? recovery = details.Recovery;
        RecoveryStatusText = recovery?.Stage switch
        {
            TraceFold.Media.Domain.Ingest.IngestOperationStage.RecoveryBlocked =>
                "无法自动恢复，请重新检查。",
            TraceFold.Media.Domain.Ingest.IngestOperationStage.CleanupPending
                when recovery.NextCleanupAttemptAtUtc is not null =>
                "入库完成，等待再次清理。",
            TraceFold.Media.Domain.Ingest.IngestOperationStage.CleanupPending =>
                "入库完成，清理需要手动重试。",
            _ => string.Empty
        };
        RecoveryVisibility = recovery is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        RecheckRecoveryVisibility = ActionVisibility(
            details.AllowedActions,
            InboxAction.RecheckRecovery);
        RetryCleanupVisibility = ActionVisibility(
            details.AllowedActions,
            InboxAction.RetryCleanup);
        OpenRecoveryLocationVisibility = recovery is not null
            && details.AllowedActions.HasFlag(InboxAction.OpenFolder)
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private static string StateMessage(InboxItemState state) =>
        state switch
        {
            InboxItemState.RequiresProcessing =>
                "此文件需要先完成前置处理。",
            InboxItemState.ProcessingQueued =>
                "前置处理已排队，源文件会保持不变。",
            InboxItemState.Processing =>
                "正在生成可验证的入库文件。",
            InboxItemState.ProcessingInterrupted =>
                "前置处理已中断，可确认源文件后重试。",
            InboxItemState.ConflictBlocked =>
                "检测到同名或重复内容，源文件已保留。",
            InboxItemState.SourceMissing =>
                "源文件已不存在，可清除这条失效记录。",
            InboxItemState.Failed =>
                "处理失败，源文件仍保留；可在确认后重试。",
            InboxItemState.CleanupPending =>
                "正式文件已保存，源文件清理尚未完成。",
            InboxItemState.RecoveryBlocked =>
                "恢复证据不明确，文件保持不变。",
            _ => string.Empty
        };
}
