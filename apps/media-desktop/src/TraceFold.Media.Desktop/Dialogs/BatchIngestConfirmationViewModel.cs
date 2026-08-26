using TraceFold.Media.Application.Ingest;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed class BatchIngestConfirmationViewModel
{
    public BatchIngestConfirmationViewModel(BatchIngestPreviewDto preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ReadyText = $"可直接入库 {preview.ReadyCount}";
        RequiresProcessingText =
            $"需要前置处理 {preview.RequiresProcessingCount}";
        ConflictText = $"存在冲突 {preview.ConflictCount}";
        RecoveryText = $"恢复受阻 {preview.RecoveryBlockedCount}";
        ChangedText = $"变化或不可操作 {preview.ChangedOrInvalidCount}";
        CanConfirm = preview.ReadyCount > 0;
    }

    public string ReadyText { get; }

    public string RequiresProcessingText { get; }

    public string ConflictText { get; }

    public string RecoveryText { get; }

    public string ChangedText { get; }

    public bool CanConfirm { get; }
}
