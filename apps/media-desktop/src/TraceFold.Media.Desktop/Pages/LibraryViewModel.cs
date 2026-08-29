using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Desktop.Dialogs;
using TraceFold.Media.Desktop.Services;

namespace TraceFold.Media.Desktop.Pages;

public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly ILibraryQueryService _library;
    private readonly ILibrarySortPreferenceService _sortPreferences;
    private readonly IAssetRevealService _reveal;
    private readonly IMediaPreviewResolver _previewResolver;
    private readonly ILibraryBatchEditDialogService _batchDialog;
    private readonly ILibraryBatchEditorService _batchEditor;
    private readonly ILibraryActivityNotifier _activityNotifier;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private bool _settingSelection;
    private bool _settingSearch;
    private bool _sortLoaded;
    private bool _coalescingBatchActivity;
    private bool _batchActivityPending;
    private LibraryQuery? _lastLoadedQuery;

    public LibraryViewModel(
        ILibraryQueryService library,
        ILibrarySortPreferenceService sortPreferences,
        LibrarySelectionViewModel selection,
        LibraryFilterViewModel filter,
        LibraryInspectorViewModel inspector,
        IAssetRevealService reveal,
        IMediaPreviewResolver previewResolver,
        ILibraryBatchEditDialogService batchDialog,
        ILibraryBatchEditorService batchEditor,
        ILibraryActivityNotifier activityNotifier)
    {
        _library = library;
        _sortPreferences = sortPreferences;
        Selection = selection;
        Filter = filter;
        Inspector = inspector;
        _reveal = reveal;
        _previewResolver = previewResolver;
        _batchDialog = batchDialog;
        _batchEditor = batchEditor;
        _activityNotifier = activityNotifier;
        Inspector.Metadata.Saved += OnMetadataSaved;
        Selection.PropertyChanged += OnSelectionPropertyChanged;
        _batchEditor.UndoStateChanged += OnUndoStateChanged;
        _activityNotifier.Changed += OnLibraryActivityChanged;
    }

    public ObservableCollection<LibraryCardViewModel> Items { get; } = [];

    public LibraryInspectorViewModel Inspector { get; }

    public LibrarySelectionViewModel Selection { get; }

    public LibraryFilterViewModel Filter { get; }

    public LibrarySelectionSummaryViewModel SelectionSummary { get; private set; } =
        LibrarySelectionSummaryViewModel.Empty;

    public Visibility EmptySelectionVisibility => Selection.SelectedCount == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility SingleSelectionVisibility => Selection.IsSingleSelection
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility MultiSelectionVisibility => Selection.IsBatchSelection
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility BatchBarVisibility => MultiSelectionVisibility;

    public bool CanUndoBatch =>
        !IsBusy && _batchEditor.UndoState.IsAvailable;

    public int UndoAffectedCount => _batchEditor.UndoState.AffectedCount;

    public Visibility UndoBannerVisibility => CanUndoBatch
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ActiveFiltersVisibility => Filter.ActiveFilterCount > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public event EventHandler? GridItemsReplacing;

    public event EventHandler? GridItemsReplaced;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial LibraryCardViewModel? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = string.Empty;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!_sortLoaded)
        {
            LibrarySortPreference preference =
                await _sortPreferences.LoadAsync(cancellationToken);
            Filter.UpdateSort(preference.Field, preference.Direction);
            _sortLoaded = true;
        }

        await ReloadAsync(cancellationToken);
    }

    public async Task ApplySearchAsync(CancellationToken cancellationToken)
    {
        LibraryQuery previous = Filter.AppliedQuery;
        LibraryQuery query = Filter.UpdateSearch(SearchText);
        if (!QueriesEqual(previous, query))
        {
            await ClearSelectionForQueryChangeAsync(cancellationToken);
        }

        await ReloadAsync(cancellationToken);
    }

    public async Task ApplyFilterAsync(
        LibraryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        LibraryQuery previous = Filter.AppliedQuery;
        Filter.SetAppliedQuery(query);
        NotifyFilterPresentation();
        _settingSearch = true;
        try
        {
            SearchText = query.SearchText ?? string.Empty;
        }
        finally
        {
            _settingSearch = false;
        }

        if (!QueriesEqual(previous, query))
        {
            await ClearSelectionForQueryChangeAsync(cancellationToken);
        }

        await ReloadAsync(cancellationToken);
    }

    public async Task ApplyCurrentFilterAsync(CancellationToken cancellationToken)
    {
        NotifyFilterPresentation();
        if (_lastLoadedQuery is not null
            && !QueriesEqual(_lastLoadedQuery, Filter.AppliedQuery))
        {
            await ClearSelectionForQueryChangeAsync(cancellationToken);
        }

        await ReloadAsync(cancellationToken);
    }

    public async Task ChangeSortAsync(
        LibrarySortField field,
        LibrarySortDirection direction,
        CancellationToken cancellationToken)
    {
        var preference = new LibrarySortPreference(field, direction);
        string? error = await _sortPreferences.SaveAsync(
            preference,
            cancellationToken);
        if (error is not null)
        {
            StatusMessage = $"保存排序设置失败：{error}";
            return;
        }

        Filter.UpdateSort(field, direction);
        await ReloadAsync(cancellationToken);
    }

    public void ReplaceSelectionFromGrid(
        IEnumerable<LibraryCardViewModel> selected)
    {
        Selection.ReplaceFromGrid(selected, Items);
        SetSelectedItemSilently(Selection.SingleSelectedItem);
    }

    public void ClearSelection()
    {
        Selection.Clear();
        SetSelectedItemSilently(null);
    }

    public async Task SelectAsync(
        LibraryCardViewModel? item,
        CancellationToken cancellationToken)
    {
        if (ReferenceEquals(SelectedItem, item)
            && Inspector.AssetId == item?.Id)
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
            await Inspector.ResetAsync(cancellationToken);
            RevealSelectedCommand.NotifyCanExecuteChanged();
            return;
        }

        LibraryAssetDetailsDto? details = await _library.GetDetailsAsync(
            item.Id,
            cancellationToken);
        if (details is null)
        {
            await Inspector.ResetAsync(cancellationToken);
            StatusMessage = "该正式资产已不存在。";
        }
        else
        {
            await Inspector.LoadAsync(details, cancellationToken);
            StatusMessage = string.Empty;
        }

        RevealSelectedCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRevealSelected))]
    private async Task RevealSelectedAsync(CancellationToken cancellationToken)
    {
        LibraryCardViewModel? selected = SelectedItem;
        if (selected is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            AssetRevealResult result = await _reveal.RevealAsync(
                selected.Id,
                cancellationToken);
            StatusMessage = result.Succeeded
                ? string.Empty
                : "正式文件已不在登记位置。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBatchEdit))]
    private async Task BatchEditAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<LibrarySelectionEntry> selection = Selection.Capture();
        if (selection.Count < 2 || IsBusy)
        {
            return;
        }

        IsBusy = true;
        BeginActivityCoalescing();
        try
        {
            LibraryBatchEditDialogResult result = await _batchDialog.ShowAsync(
                selection,
                cancellationToken);
            if (result.Applied || _batchActivityPending)
            {
                await ReloadAsync(cancellationToken);
            }

            if (result.Applied && result.Result is not null)
            {
                StatusMessage =
                    $"已批量修改 {result.Result.ChangedCount} 项正式文件。";
            }
            else if (result.Result is { Succeeded: false } failed)
            {
                StatusMessage = BatchEditError(failed.ErrorCode);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "批量修改已取消，当前选择已保留。";
        }
        catch (Exception)
        {
            StatusMessage = "无法打开或提交批量编辑，当前选择已保留。";
        }
        finally
        {
            EndActivityCoalescing();
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndoBatch))]
    private async Task UndoBatchAsync(CancellationToken cancellationToken)
    {
        if (!CanUndoBatch)
        {
            return;
        }

        IsBusy = true;
        BeginActivityCoalescing();
        try
        {
            LibraryBatchUndoResult result = await _batchEditor.UndoLastAsync(
                cancellationToken);
            if (result.Succeeded || _batchActivityPending)
            {
                await ReloadAsync(cancellationToken);
            }

            StatusMessage = result.Succeeded
                ? $"已撤销上一批修改，恢复 {result.RestoredCount} 项。"
                : UndoError(result.ErrorCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "撤销已取消。";
        }
        catch (Exception)
        {
            StatusMessage = "无法撤销上一批修改。";
        }
        finally
        {
            EndActivityCoalescing();
            IsBusy = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = value;
        if (!_settingSearch)
        {
            _ = ApplySearchSafeAsync();
        }
    }

    partial void OnSelectedItemChanged(LibraryCardViewModel? value)
    {
        if (!_settingSelection)
        {
            _ = SelectSafeAsync(value);
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        _ = value;
        RevealSelectedCommand.NotifyCanExecuteChanged();
        BatchEditCommand.NotifyCanExecuteChanged();
        UndoBatchCommand.NotifyCanExecuteChanged();
        NotifyUndoPresentation();
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await _reloadGate.WaitAsync(cancellationToken);
        try
        {
            Guid? selectedId = SelectedItem?.Id;
            LibraryQuery query = Filter.AppliedQuery;
            IReadOnlyList<LibraryAssetDto> cards = await _library.SearchAsync(
                query,
                cancellationToken);
            var refreshed = new List<LibraryCardViewModel>(cards.Count);
            foreach (LibraryAssetDto card in cards)
            {
                string? thumbnail = null;
                if (card.MediaKind == MediaKind.Image)
                {
                    try
                    {
                        PreviewDescriptor descriptor = await _previewResolver.ResolveAsync(
                            new MetadataOwner(MetadataOwnerKind.MediaAsset, card.Id),
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

                refreshed.Add(new LibraryCardViewModel(card, thumbnail));
            }

            GridItemsReplacing?.Invoke(this, EventArgs.Empty);
            try
            {
                Items.Clear();
                foreach (LibraryCardViewModel item in refreshed)
                {
                    Items.Add(item);
                }

                Selection.Reconcile(Items);
                LibraryCardViewModel? selected = Selection.SingleSelectedItem;
                if (selected is null
                    && selectedId.HasValue
                    && Selection.SelectedCount == 0)
                {
                    selected = Items.FirstOrDefault(
                        item => item.Id == selectedId.Value);
                }

                SetSelectedItemSilently(selected);
                _lastLoadedQuery = query;
            }
            finally
            {
                GridItemsReplaced?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private async Task ReloadSafeAsync()
    {
        try
        {
            await ReloadAsync(CancellationToken.None);
        }
        catch (Exception error)
        {
            StatusMessage = $"刷新正式文件库失败：{error.Message}";
        }
    }

    private async Task ApplySearchSafeAsync()
    {
        try
        {
            await ApplySearchAsync(CancellationToken.None);
        }
        catch (Exception error)
        {
            StatusMessage = $"刷新正式文件库失败：{error.Message}";
        }
    }

    private async Task SelectSafeAsync(LibraryCardViewModel? item)
    {
        try
        {
            await SelectAsync(item, CancellationToken.None);
        }
        catch (Exception error)
        {
            StatusMessage = $"加载资产详情失败：{error.Message}";
        }
    }

    private void OnMetadataSaved(MetadataOwner owner)
    {
        if (owner.Kind == MetadataOwnerKind.MediaAsset)
        {
            _batchEditor.InvalidateUndoFor(owner.Id);
            _ = ReloadSafeAsync();
        }
    }

    private void OnUndoStateChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        NotifyUndoPresentation();
    }

    private void OnLibraryActivityChanged(
        object? sender,
        LibraryActivityEventArgs args)
    {
        _ = sender;
        if (args.Kind is not (
            LibraryActivityKind.BatchChanged
            or LibraryActivityKind.UndoCompleted
            or LibraryActivityKind.TaxonomyChanged))
        {
            return;
        }

        if (_coalescingBatchActivity)
        {
            _batchActivityPending = true;
            return;
        }

        _ = ReloadSafeAsync();
    }

    private void OnSelectionPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs args)
    {
        _ = sender;
        if (args.PropertyName == nameof(Selection.SelectedCount))
        {
            UpdateSelectionPresentation();
            BatchEditCommand.NotifyCanExecuteChanged();
        }
    }

    private void UpdateSelectionPresentation()
    {
        HashSet<Guid> selectedIds = Selection.SelectedIds.ToHashSet();
        SelectionSummary = LibrarySelectionSummaryViewModel.From(
            Items.Where(item => selectedIds.Contains(item.Id)));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(EmptySelectionVisibility));
        OnPropertyChanged(nameof(SingleSelectionVisibility));
        OnPropertyChanged(nameof(MultiSelectionVisibility));
        OnPropertyChanged(nameof(BatchBarVisibility));
    }

    private void NotifyFilterPresentation()
    {
        OnPropertyChanged(nameof(ActiveFiltersVisibility));
    }

    private async Task ClearSelectionForQueryChangeAsync(
        CancellationToken cancellationToken)
    {
        bool hadSelection = Selection.SelectedCount > 0;
        ClearSelection();
        await Inspector.ResetAsync(cancellationToken);
        if (hadSelection)
        {
            StatusMessage = "查询条件已变化，已取消选择。";
        }
    }

    private void SetSelectedItemSilently(LibraryCardViewModel? item)
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

    private static bool QueriesEqual(LibraryQuery left, LibraryQuery right) =>
        StringComparer.Ordinal.Equals(left.SearchText, right.SearchText)
        && left.Media == right.Media
        && left.AuthorIds.SequenceEqual(right.AuthorIds)
        && left.SeriesId == right.SeriesId
        && left.TagIds.SequenceEqual(right.TagIds)
        && left.MissingAuthors == right.MissingAuthors
        && left.MissingSeries == right.MissingSeries
        && left.MissingTags == right.MissingTags
        && left.DisplayName == right.DisplayName
        && left.SortField == right.SortField
        && left.SortDirection == right.SortDirection;

    private bool CanRevealSelected() => !IsBusy && SelectedItem is not null;

    private bool CanBatchEdit() => !IsBusy && Selection.IsBatchSelection;

    private void NotifyUndoPresentation()
    {
        OnPropertyChanged(nameof(CanUndoBatch));
        OnPropertyChanged(nameof(UndoAffectedCount));
        OnPropertyChanged(nameof(UndoBannerVisibility));
        UndoBatchCommand.NotifyCanExecuteChanged();
    }

    private void BeginActivityCoalescing()
    {
        _coalescingBatchActivity = true;
        _batchActivityPending = false;
    }

    private void EndActivityCoalescing()
    {
        _coalescingBatchActivity = false;
        _batchActivityPending = false;
    }

    private static string BatchEditError(string? errorCode) => errorCode switch
    {
        "library.batch.version_conflict" =>
            "选择中的资料已变化，请刷新后重试。",
        "library.batch.selection_missing" =>
            "选择中的资料已不存在，请刷新后重试。",
        _ => "批量修改未完成，当前选择已保留。"
    };

    private static string UndoError(string? errorCode) => errorCode switch
    {
        "library.batch.undo.version_conflict" =>
            "资料已在撤销后发生变化，无法撤销上一批修改。",
        "library.batch.undo.unavailable" => "当前没有可撤销的批量修改。",
        _ => "无法撤销上一批修改。"
    };
}
