using CommunityToolkit.Mvvm.ComponentModel;
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
    }

    private static string StateMessage(InboxItemState state) =>
        state switch
        {
            InboxItemState.RequiresProcessing =>
                "此文件需要先完成前置处理，当前版本暂不支持正式入库。",
            InboxItemState.ConflictBlocked =>
                "检测到同名或重复内容，源文件已保留。",
            InboxItemState.SourceMissing =>
                "源文件已不存在，可清除这条失效记录。",
            InboxItemState.Failed =>
                "处理失败，源文件仍保留；可在确认后重试。",
            InboxItemState.CleanupPending =>
                "正式文件已保存，源文件清理尚未完成。",
            _ => string.Empty
        };
}
