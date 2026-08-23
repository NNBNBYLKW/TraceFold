using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Desktop.Services;

namespace TraceFold.Media.Desktop.Pages;

public sealed record InboxStateFilterOption(
    string Label,
    InboxItemState? State)
{
    public override string ToString() => Label;
}

public sealed partial class InboxViewModel : ObservableObject
{
    private readonly IInboxQueryService _query;
    private readonly IInboxScanner _scanner;
    private readonly IInboxActivityNotifier _activity;
    private readonly IUiDispatcher _dispatcher;
    private readonly IMediaPreviewResolver _previewResolver;
    private readonly IOrdinaryIngestService _ingest;
    private readonly IIngestRecoveryService _recovery;
    private readonly IProcessingCommandService? _processingCommands;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private bool _hasLoaded;
    private bool _settingSelection;

    public InboxViewModel(
        IInboxQueryService query,
        IInboxScanner scanner,
        IInboxActivityNotifier activity,
        IUiDispatcher dispatcher,
        IMediaPreviewResolver previewResolver,
        InboxInspectorViewModel inspector,
        IOrdinaryIngestService ingest,
        IIngestRecoveryService recovery,
        IProcessingCommandService? processingCommands = null)
    {
        _query = query;
        _scanner = scanner;
        _activity = activity;
        _dispatcher = dispatcher;
        _previewResolver = previewResolver;
        Inspector = inspector;
        _ingest = ingest;
        _recovery = recovery;
        _processingCommands = processingCommands;
        StateFilters =
        [
            new("全部状态", null),
            new("待入库", InboxItemState.ReadyToTag),
            new("需要处理", InboxItemState.RequiresProcessing),
            new("排队中", InboxItemState.ProcessingQueued),
            new("处理中", InboxItemState.Processing),
            new("已中断", InboxItemState.ProcessingInterrupted),
            new("存在冲突", InboxItemState.ConflictBlocked),
            new("源文件缺失", InboxItemState.SourceMissing),
            new("处理失败", InboxItemState.Failed)
        ];
        SelectedStateFilter = StateFilters[0];
    }

    public ObservableCollection<InboxCardViewModel> Items { get; } = [];

    public IReadOnlyList<InboxStateFilterOption> StateFilters { get; }

    public InboxInspectorViewModel Inspector { get; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InboxStateFilterOption SelectedStateFilter { get; set; }

    [ObservableProperty]
    public partial InboxCardViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; private set; }

    [ObservableProperty]
    public partial string ScanStatusMessage { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string OperationMessage { get; private set; } = string.Empty;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            if (!_hasLoaded)
            {
                _activity.Changed += OnInboxActivity;
                _hasLoaded = true;
            }

            await ReloadCardsAsync(cancellationToken);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public Task ApplyFiltersAsync(CancellationToken cancellationToken) =>
        ReloadCardsAsync(cancellationToken);

    public async Task SelectAsync(
        InboxCardViewModel? item,
        CancellationToken cancellationToken)
    {
        if (ReferenceEquals(SelectedItem, item)
            && Inspector.InboxItemId == item?.Id)
        {
            return;
        }

        _settingSelection = true;
        try
        {
            SelectedItem = item;
        }
        finally
        {
            _settingSelection = false;
        }

        if (item is null)
        {
            await Inspector.Metadata.FlushAsync(cancellationToken);
            Inspector.Reset();
            NotifyCommands();
            return;
        }

        InboxDetailsDto? details = await _query.GetDetailsAsync(
            item.Id,
            cancellationToken);
        if (details is null)
        {
            Inspector.Reset();
            OperationMessage = "该待入库项目已不存在。";
        }
        else
        {
            await Inspector.LoadAsync(details, cancellationToken);
            OperationMessage = Inspector.StatusMessage;
        }

        NotifyCommands();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsRefreshing = true;
        try
        {
            ScanResult result = await _scanner.ScanAsync(
                ScanTrigger.Manual,
                cancellationToken);
            ScanStatusMessage = result.Message;
            await ReloadCardsAsync(cancellationToken);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanIngest))]
    private async Task IngestAsync(CancellationToken cancellationToken)
    {
        InboxCardViewModel? selected = SelectedItem;
        if (selected is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await Inspector.Metadata.FlushAsync(cancellationToken);
            IngestResult result = await _ingest.IngestAsync(
                selected.Id,
                cancellationToken);
            OperationMessage = IngestMessage(result);
            if (result.Outcome == IngestOutcome.Completed)
            {
                await SelectAsync(null, cancellationToken);
            }

            await ReloadCardsAsync(cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanClearMissing))]
    private async Task ClearMissingAsync(CancellationToken cancellationToken)
    {
        InboxCardViewModel? selected = SelectedItem;
        if (selected is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _query.ClearMissingAsync(selected.Id, cancellationToken);
            await SelectAsync(null, cancellationToken);
            await ReloadCardsAsync(cancellationToken);
            OperationMessage = "失效记录已清除。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private async Task RetryAsync(CancellationToken cancellationToken)
    {
        InboxCardViewModel? selected = SelectedItem;
        if (selected is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            RecoveryResult result = await _recovery.RetryAsync(
                selected.Id,
                cancellationToken);
            OperationMessage = result.Succeeded
                ? "已完成恢复核对。"
                : "当前项目无法安全重试。";
            await ReloadCardsAsync(cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartProcessing))]
    private Task StartProcessingAsync(CancellationToken cancellationToken) =>
        RunProcessingAsync(
            (itemId, token) => _processingCommands!.StartAsync(itemId, token),
            cancellationToken);

    [RelayCommand(CanExecute = nameof(CanCancelProcessing))]
    private Task CancelProcessingAsync(CancellationToken cancellationToken) =>
        RunProcessingAsync(
            (itemId, token) => _processingCommands!.CancelQueuedAsync(itemId, token),
            cancellationToken);

    [RelayCommand(CanExecute = nameof(CanStopProcessing))]
    private Task StopProcessingAsync(CancellationToken cancellationToken) =>
        RunProcessingAsync(
            (itemId, token) => _processingCommands!.StopAsync(itemId, token),
            cancellationToken);

    [RelayCommand(CanExecute = nameof(CanRetryProcessing))]
    private Task RetryProcessingAsync(CancellationToken cancellationToken) =>
        RunProcessingAsync(
            (itemId, token) => _processingCommands!.RetryAsync(itemId, token),
            cancellationToken);

    partial void OnSearchTextChanged(string value)
    {
        _ = value;
        _ = ReloadCardsSafeAsync();
    }

    partial void OnSelectedStateFilterChanged(InboxStateFilterOption value)
    {
        _ = value;
        _ = ReloadCardsSafeAsync();
    }

    partial void OnSelectedItemChanged(InboxCardViewModel? value)
    {
        if (!_settingSelection)
        {
            _ = SelectSafeAsync(value);
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        _ = value;
        NotifyCommands();
    }

    partial void OnIsRefreshingChanged(bool value)
    {
        _ = value;
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private async Task ReloadCardsAsync(CancellationToken cancellationToken)
    {
        await _reloadGate.WaitAsync(cancellationToken);
        try
        {
            IReadOnlyList<InboxCardDto> cards = await _query.SearchAsync(
                new InboxQuery(
                    string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                    SelectedStateFilter.State),
                cancellationToken);
            var items = new List<InboxCardViewModel>(cards.Count);
            foreach (InboxCardDto card in cards)
            {
                string? thumbnail = null;
                if (card.MediaKind == MediaKind.Image)
                {
                    try
                    {
                        PreviewDescriptor descriptor = await _previewResolver.ResolveAsync(
                            new MetadataOwner(MetadataOwnerKind.InboxItem, card.Id),
                            cancellationToken);
                        if (descriptor.Exists)
                        {
                            thumbnail = new Uri(descriptor.TrustedFullPath).AbsoluteUri;
                        }
                    }
                    catch (PreviewNotRegisteredException)
                    {
                    }
                }

                items.Add(new InboxCardViewModel(card, thumbnail));
            }

            Items.Clear();
            foreach (InboxCardViewModel item in items)
            {
                Items.Add(item);
            }
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private async Task ReloadCardsSafeAsync()
    {
        try
        {
            await ReloadCardsAsync(CancellationToken.None);
        }
        catch (Exception error)
        {
            OperationMessage = $"刷新列表失败：{error.Message}";
        }
    }

    private async Task SelectSafeAsync(InboxCardViewModel? item)
    {
        try
        {
            await SelectAsync(item, CancellationToken.None);
        }
        catch (Exception error)
        {
            OperationMessage = $"加载详情失败：{error.Message}";
        }
    }

    private void OnInboxActivity(object? sender, InboxActivityEventArgs args)
    {
        _ = sender;
        _ = args;
        _dispatcher.Enqueue(() => _ = ReloadCardsSafeAsync());
    }

    private bool CanRefresh() => !IsRefreshing && !IsBusy;

    private bool CanIngest() => !IsBusy
        && SelectedItem?.Card.CanIngest == true
        && Inspector.AllowedActions.HasFlag(InboxAction.Ingest);

    private bool CanClearMissing() => !IsBusy
        && Inspector.AllowedActions.HasFlag(InboxAction.ClearMissing);

    private bool CanRetry() => !IsBusy
        && (Inspector.AllowedActions.HasFlag(InboxAction.Retry)
            || Inspector.AllowedActions.HasFlag(InboxAction.RetryCleanup));

    private bool CanStartProcessing() => !IsBusy
        && _processingCommands is not null
        && Inspector.AllowedActions.HasFlag(InboxAction.StartProcessing);

    private bool CanCancelProcessing() => !IsBusy
        && _processingCommands is not null
        && Inspector.AllowedActions.HasFlag(InboxAction.CancelProcessing);

    private bool CanStopProcessing() => !IsBusy
        && _processingCommands is not null
        && Inspector.AllowedActions.HasFlag(InboxAction.StopProcessing);

    private bool CanRetryProcessing() => !IsBusy
        && _processingCommands is not null
        && Inspector.AllowedActions.HasFlag(InboxAction.RetryProcessing);

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        IngestCommand.NotifyCanExecuteChanged();
        ClearMissingCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
        StartProcessingCommand.NotifyCanExecuteChanged();
        CancelProcessingCommand.NotifyCanExecuteChanged();
        StopProcessingCommand.NotifyCanExecuteChanged();
        RetryProcessingCommand.NotifyCanExecuteChanged();
    }

    private async Task RunProcessingAsync(
        Func<Guid, CancellationToken, Task<ProcessingCommandResult>> command,
        CancellationToken cancellationToken)
    {
        InboxCardViewModel? selected = SelectedItem;
        if (selected is null || IsBusy || _processingCommands is null)
        {
            return;
        }

        Guid inboxItemId = selected.Id;
        IsBusy = true;
        try
        {
            ProcessingCommandResult result = await command(
                inboxItemId,
                cancellationToken);
            await ReloadCardsAsync(cancellationToken);
            InboxCardViewModel? refreshed = Items.FirstOrDefault(
                item => item.Id == inboxItemId);
            await SelectAsync(refreshed, cancellationToken);
            OperationMessage = ProcessingMessage(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string ProcessingMessage(ProcessingCommandResult result) =>
        !string.IsNullOrWhiteSpace(result.Message)
            ? result.Message
            : result.Outcome switch
            {
                ProcessingCommandOutcome.Queued => "已加入处理队列。",
                ProcessingCommandOutcome.Cancelled => "已取消排队。",
                ProcessingCommandOutcome.StopRequested => "正在停止处理。",
                ProcessingCommandOutcome.ToolUnavailable => "前置处理工具不可用。",
                ProcessingCommandOutcome.SourceMissing => "源文件已不存在。",
                ProcessingCommandOutcome.SourceChanged => "源文件已变化，请重新刷新。",
                ProcessingCommandOutcome.ShuttingDown => "软件正在关闭。",
                _ => "前置处理操作未完成。"
            };

    private static string IngestMessage(IngestResult result) =>
        result.Outcome switch
        {
            IngestOutcome.Completed => "入库完成。",
            IngestOutcome.ConflictBlocked => "检测到冲突，源文件已保留。",
            IngestOutcome.RequiresProcessing => "需要先完成前置处理。",
            IngestOutcome.SourceMissing => "源文件已不存在。",
            IngestOutcome.SourceChanged => "源文件已变化，请重新刷新。",
            IngestOutcome.CleanupPending => "正式文件已保存，源文件清理待重试。",
            IngestOutcome.ShuttingDown => "软件正在关闭，未开始新的入库操作。",
            _ => "入库未完成，请检查项目状态。"
        };
}
