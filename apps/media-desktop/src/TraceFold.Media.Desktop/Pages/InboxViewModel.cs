using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Desktop.Dialogs;
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
    private readonly IBatchMetadataService _batchMetadata;
    private readonly IBatchMetadataDialogService _batchMetadataDialog;
    private readonly IBatchProcessingService _batchProcessing;
    private readonly IIngestService _ingest;
    private readonly IIngestRecoveryService _recovery;
    private readonly IConflictQueryService _conflictQuery;
    private readonly IConflictResolutionService _conflictResolution;
    private readonly IConflictDialogService _conflictDialog;
    private readonly IProcessingCommandService? _processingCommands;
    private readonly IProcessingDiagnosticsService? _processingDiagnostics;
    private readonly IRecoveryRevealService? _recoveryReveal;
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
        InboxSelectionViewModel selection,
        InboxBatchSummaryViewModel batchSummary,
        IBatchMetadataService batchMetadata,
        IBatchMetadataDialogService batchMetadataDialog,
        IBatchProcessingService batchProcessing,
        IIngestService ingest,
        IIngestRecoveryService recovery,
        IConflictQueryService conflictQuery,
        IConflictResolutionService conflictResolution,
        IConflictDialogService conflictDialog,
        IProcessingCommandService? processingCommands = null,
        IProcessingDiagnosticsService? processingDiagnostics = null,
        IRecoveryRevealService? recoveryReveal = null)
    {
        _query = query;
        _scanner = scanner;
        _activity = activity;
        _dispatcher = dispatcher;
        _previewResolver = previewResolver;
        Inspector = inspector;
        Selection = selection;
        BatchSummary = batchSummary;
        _batchMetadata = batchMetadata;
        _batchMetadataDialog = batchMetadataDialog;
        _batchProcessing = batchProcessing;
        _ingest = ingest;
        _recovery = recovery;
        _conflictQuery = conflictQuery;
        _conflictResolution = conflictResolution;
        _conflictDialog = conflictDialog;
        _processingCommands = processingCommands;
        _processingDiagnostics = processingDiagnostics;
        _recoveryReveal = recoveryReveal;
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

    public InboxSelectionViewModel Selection { get; }

    public InboxBatchSummaryViewModel BatchSummary { get; }

    public event EventHandler? GridItemsReplacing;

    public event EventHandler? GridItemsReplaced;

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

    [ObservableProperty]
    public partial bool ProcessingAvailable { get; private set; } = true;

    [ObservableProperty]
    public partial Visibility InspectorVisibility { get; private set; } =
        Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility BatchSummaryVisibility { get; private set; } =
        Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility BatchBarVisibility { get; private set; } =
        Visibility.Collapsed;

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

            if (_processingDiagnostics is not null)
            {
                ProcessingDiagnosticsDto diagnostic =
                    await _processingDiagnostics.CheckAsync(cancellationToken);
                ProcessingAvailable = diagnostic.Available;
            }

            await ReloadCardsAsync(cancellationToken);
            NotifyCommands();
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public Task ApplyFiltersAsync(CancellationToken cancellationToken) =>
        ReloadCardsAsync(cancellationToken);

    public Task SelectAsync(
        InboxCardViewModel? item,
        CancellationToken cancellationToken) =>
        SelectManyAsync(
            item is null ? [] : [item],
            cancellationToken);

    public async Task SelectManyAsync(
        IReadOnlyList<InboxCardViewModel> items,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        Selection.ReplaceFromGrid(items, Items);
        await PresentSelectionAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanBatchEditMetadata))]
    private async Task BatchEditMetadataAsync(CancellationToken cancellationToken)
    {
        if (!CanBatchEditMetadata())
        {
            return;
        }

        IsBusy = true;
        try
        {
            IReadOnlyList<InboxSelectionEntry> selection = Selection.Capture();
            BatchMetadataEditorDto? editor = await _batchMetadata.PrepareAsync(
                selection,
                cancellationToken);
            if (editor is null)
            {
                OperationMessage = "选择已变化，请刷新后重新选择。";
                await ReloadCardsAsync(cancellationToken);
                return;
            }

            BatchMetadataDialogResult dialogResult =
                await _batchMetadataDialog.ShowAsync(
                    editor,
                    selection,
                    _batchMetadata.ApplyAsync,
                    cancellationToken);
            if (dialogResult.Applied && dialogResult.Result is not null)
            {
                OperationMessage =
                    $"已修改 {dialogResult.Result.ChangedCount} 项资料，"
                    + $"{dialogResult.Result.UnchangedCount} 项无变化。";
                await ReloadCardsAsync(cancellationToken);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBatchStartProcessing))]
    private async Task BatchStartProcessingAsync(CancellationToken cancellationToken)
    {
        if (!CanBatchStartProcessing())
        {
            return;
        }

        IsBusy = true;
        try
        {
            BatchProcessingResult result = await _batchProcessing.StartAsync(
                Selection.Capture(),
                cancellationToken);
            OperationMessage =
                $"已排队 {Count(result, BatchProcessingItemOutcome.Queued)} 项，"
                + $"已在队列 {Count(result, BatchProcessingItemOutcome.AlreadyActive)} 项，"
                + $"无需处理 {Count(result, BatchProcessingItemOutcome.NotRequired)} 项，"
                + $"需要检查 {Count(result, BatchProcessingItemOutcome.NeedsAttention)} 项。";
            await ReloadCardsAsync(cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
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
            if (result.Outcome == IngestOutcome.ConflictBlocked)
            {
                await ShowConflictAsync(selected.Id, cancellationToken);
                return;
            }

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

    [RelayCommand(CanExecute = nameof(CanResolveConflict))]
    private async Task ResolveConflictAsync(CancellationToken cancellationToken)
    {
        InboxCardViewModel? selected = SelectedItem;
        if (selected is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await ShowConflictAsync(selected.Id, cancellationToken);
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

    [RelayCommand(CanExecute = nameof(CanRecheckRecovery))]
    private Task RecheckRecoveryAsync(CancellationToken cancellationToken) =>
        RunRecoveryAsync(
            (itemId, token) => _recovery.RetryAsync(itemId, token),
            "已完成恢复核对。",
            cancellationToken);

    [RelayCommand(CanExecute = nameof(CanRetryCleanup))]
    private Task RetryCleanupAsync(CancellationToken cancellationToken) =>
        RunRecoveryAsync(
            (itemId, token) => _recovery.RetryCleanupAsync(itemId, token),
            "已完成清理重试。",
            cancellationToken);

    [RelayCommand(CanExecute = nameof(CanOpenRecoveryLocation))]
    private async Task OpenRecoveryLocationAsync(CancellationToken cancellationToken)
    {
        InboxCardViewModel? selected = SelectedItem;
        if (selected is null || IsBusy || _recoveryReveal is null)
        {
            return;
        }

        bool revealed = await _recoveryReveal.RevealAsync(
            selected.Id,
            cancellationToken);
        OperationMessage = revealed
            ? "已打开文件位置。"
            : "没有可安全打开的恢复文件位置。";
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
        ClearSelectionForFilterChange();
        _ = ApplyFilterChangeSafeAsync();
    }

    partial void OnSelectedStateFilterChanged(InboxStateFilterOption value)
    {
        _ = value;
        ClearSelectionForFilterChange();
        _ = ApplyFilterChangeSafeAsync();
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

            GridItemsReplacing?.Invoke(this, EventArgs.Empty);
            try
            {
                Items.Clear();
                foreach (InboxCardViewModel item in items)
                {
                    Items.Add(item);
                }

                Selection.Reconcile(Items);
            }
            finally
            {
                GridItemsReplaced?.Invoke(this, EventArgs.Empty);
            }

            await PresentSelectionAsync(
                cancellationToken,
                forceSingleReload: true);
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

    private async Task ApplyFilterChangeSafeAsync()
    {
        try
        {
            await Inspector.Metadata.FlushAsync(CancellationToken.None);
            Inspector.Reset();
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

    private async Task PresentSelectionAsync(
        CancellationToken cancellationToken,
        bool forceSingleReload = false)
    {
        InboxCardViewModel? single = Selection.SingleSelectedItem;
        bool leavingLoadedSingle = Inspector.InboxItemId.HasValue
            && (single is null || Inspector.InboxItemId.Value != single.Id);
        if (leavingLoadedSingle)
        {
            await Inspector.Metadata.FlushAsync(cancellationToken);
        }

        if (Selection.IsBatchSelection)
        {
            SetSelectedItem(null);
            Inspector.Reset();
            await BatchSummary.LoadAsync(
                Selection.Capture(),
                cancellationToken);
            UpdateSelectionVisibility();
            NotifyCommands();
            return;
        }

        BatchSummary.Reset();
        if (single is null)
        {
            SetSelectedItem(null);
            Inspector.Reset();
            UpdateSelectionVisibility();
            NotifyCommands();
            return;
        }

        SetSelectedItem(single);
        if (forceSingleReload
            || Inspector.InboxItemId != single.Id)
        {
            InboxDetailsDto? details = await _query.GetDetailsAsync(
                single.Id,
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
        }

        UpdateSelectionVisibility();
        NotifyCommands();
    }

    private void ClearSelectionForFilterChange()
    {
        Selection.Clear();
        SetSelectedItem(null);
        BatchSummary.Reset();
        UpdateSelectionVisibility();
        NotifyCommands();
    }

    private void SetSelectedItem(InboxCardViewModel? item)
    {
        _settingSelection = true;
        try
        {
            SelectedItem = item;
        }
        finally
        {
            _settingSelection = false;
        }
    }

    private void UpdateSelectionVisibility()
    {
        InspectorVisibility = Selection.IsSingleSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
        BatchSummaryVisibility = Selection.IsBatchSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
        BatchBarVisibility = Selection.IsBatchSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnInboxActivity(object? sender, InboxActivityEventArgs args)
    {
        _ = sender;
        _ = args;
        _dispatcher.Enqueue(() => _ = ReloadCardsSafeAsync());
    }

    private bool CanRefresh() => !IsRefreshing && !IsBusy;

    private bool CanBatchEditMetadata() => !IsBusy
        && Selection.IsBatchSelection
        && BatchSummary.CanEditMetadata;

    private bool CanBatchStartProcessing() => !IsBusy
        && ProcessingAvailable
        && Selection.IsBatchSelection
        && BatchSummary.CanStartProcessing;

    private bool CanIngest() => !IsBusy
        && SelectedItem?.Card.CanIngest == true
        && Inspector.AllowedActions.HasFlag(InboxAction.Ingest);

    private bool CanClearMissing() => !IsBusy
        && Inspector.AllowedActions.HasFlag(InboxAction.ClearMissing);

    private bool CanRetry() => !IsBusy
        && Inspector.AllowedActions.HasFlag(InboxAction.Retry);

    private bool CanRecheckRecovery() => !IsBusy
        && Inspector.AllowedActions.HasFlag(InboxAction.RecheckRecovery);

    private bool CanRetryCleanup() => !IsBusy
        && Inspector.AllowedActions.HasFlag(InboxAction.RetryCleanup);

    private bool CanOpenRecoveryLocation() => !IsBusy
        && _recoveryReveal is not null
        && Inspector.RecoveryVisibility == Microsoft.UI.Xaml.Visibility.Visible
        && Inspector.AllowedActions.HasFlag(InboxAction.OpenFolder);

    private bool CanResolveConflict() => !IsBusy
        && SelectedItem is not null
        && Inspector.AllowedActions.HasFlag(InboxAction.ResolveConflict);

    private bool CanStartProcessing() => !IsBusy
        && _processingCommands is not null
        && ProcessingAvailable
        && Inspector.AllowedActions.HasFlag(InboxAction.StartProcessing);

    private bool CanCancelProcessing() => !IsBusy
        && _processingCommands is not null
        && Inspector.AllowedActions.HasFlag(InboxAction.CancelProcessing);

    private bool CanStopProcessing() => !IsBusy
        && _processingCommands is not null
        && Inspector.AllowedActions.HasFlag(InboxAction.StopProcessing);

    private bool CanRetryProcessing() => !IsBusy
        && _processingCommands is not null
        && ProcessingAvailable
        && Inspector.AllowedActions.HasFlag(InboxAction.RetryProcessing);

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        BatchEditMetadataCommand.NotifyCanExecuteChanged();
        BatchStartProcessingCommand.NotifyCanExecuteChanged();
        IngestCommand.NotifyCanExecuteChanged();
        ClearMissingCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
        RecheckRecoveryCommand.NotifyCanExecuteChanged();
        RetryCleanupCommand.NotifyCanExecuteChanged();
        OpenRecoveryLocationCommand.NotifyCanExecuteChanged();
        ResolveConflictCommand.NotifyCanExecuteChanged();
        StartProcessingCommand.NotifyCanExecuteChanged();
        CancelProcessingCommand.NotifyCanExecuteChanged();
        StopProcessingCommand.NotifyCanExecuteChanged();
        RetryProcessingCommand.NotifyCanExecuteChanged();
    }

    private static int Count(
        BatchProcessingResult result,
        BatchProcessingItemOutcome outcome) =>
        result.Items.Count(item => item.Outcome == outcome);

    private async Task ShowConflictAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        ConflictCaseDto? conflict = await _conflictQuery.GetOpenAsync(
            inboxItemId,
            cancellationToken);
        if (conflict is null)
        {
            OperationMessage = "冲突记录已变化，请刷新后重试。";
            await ReloadCardsAsync(cancellationToken);
            return;
        }

        ConflictDialogResult? decision = await _conflictDialog.ShowAsync(
            conflict,
            cancellationToken);
        if (decision is not null)
        {
            IngestResult result = await _conflictResolution.ResolveAsync(
                conflict.Id,
                decision.Resolution,
                decision.SelectedMatchId,
                cancellationToken);
            OperationMessage = IngestMessage(result);
        }

        await ReloadCardsAsync(cancellationToken);
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

    private async Task RunRecoveryAsync(
        Func<Guid, CancellationToken, Task<RecoveryResult>> command,
        string successMessage,
        CancellationToken cancellationToken)
    {
        InboxCardViewModel? selected = SelectedItem;
        if (selected is null || IsBusy)
        {
            return;
        }

        Guid inboxItemId = selected.Id;
        IsBusy = true;
        try
        {
            RecoveryResult result = await command(inboxItemId, cancellationToken);
            await ReloadCardsAsync(cancellationToken);
            InboxCardViewModel? refreshed = Items.FirstOrDefault(
                item => item.Id == inboxItemId);
            await SelectAsync(refreshed, cancellationToken);
            OperationMessage = result.Succeeded
                ? successMessage
                : "当前项目无法安全重试。";
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
            IngestOutcome.Skipped => "已跳过本次冲突，源文件保持不变。",
            IngestOutcome.ConflictChanged => "冲突内容已变化，请重新确认。",
            IngestOutcome.RequiresProcessing => "需要先完成前置处理。",
            IngestOutcome.SourceMissing => "源文件已不存在。",
            IngestOutcome.SourceChanged => "源文件已变化，请重新刷新。",
            IngestOutcome.CleanupPending => "正式文件已保存，源文件清理待重试。",
            IngestOutcome.ShuttingDown => "软件正在关闭，未开始新的入库操作。",
            _ => "入库未完成，请检查项目状态。"
        };
}
