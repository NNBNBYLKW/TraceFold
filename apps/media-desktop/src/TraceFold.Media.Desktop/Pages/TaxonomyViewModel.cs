using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Desktop.Dialogs;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Desktop.Pages;

public sealed record TaxonomyKindOption(
    TaxonomyKind Kind,
    string DisplayName);

public sealed class TaxonomyViewModel : ObservableObject
{
    private readonly ITaxonomyManagementQueryService _query;
    private readonly ITaxonomyService _taxonomy;
    private readonly ITaxonomyMaintenanceService _maintenance;
    private readonly ITaxonomyMaintenanceDialogService _dialogs;
    private readonly ILogger<TaxonomyViewModel>? _logger;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private long _reloadGeneration;
    private int _pendingWriteCount;
    private TaxonomyKind _selectedKind = TaxonomyKind.Author;
    private string _searchText = string.Empty;
    private TaxonomyReferenceFilter _referenceFilter =
        TaxonomyReferenceFilter.All;
    private TaxonomySortField _sortField = TaxonomySortField.DisplayName;
    private TaxonomySortDirection _sortDirection =
        TaxonomySortDirection.Ascending;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private bool _isWriteBusy;
    private TaxonomyManagementTermDto? _renameConflictTarget;
    private bool _canUndoMaintenance;
    private string _undoBannerMessage = string.Empty;

    public TaxonomyViewModel(
        ITaxonomyManagementQueryService query,
        ITaxonomyService taxonomy,
        ITaxonomyMaintenanceService maintenance,
        ITaxonomyMaintenanceDialogService dialogs,
        ILogger<TaxonomyViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(taxonomy);
        ArgumentNullException.ThrowIfNull(maintenance);
        ArgumentNullException.ThrowIfNull(dialogs);
        _query = query;
        _taxonomy = taxonomy;
        _maintenance = maintenance;
        _dialogs = dialogs;
        _logger = logger;
        Selection = new TaxonomySelectionViewModel();
        Selection.PropertyChanged += OnSelectionPropertyChanged;
        CreateCommand = new AsyncRelayCommand<string?>(
            CreateAsync,
            _ => CanCreate);
        RenameCommand = new AsyncRelayCommand<string?>(
            RenameAsync,
            _ => CanRename);
        MergeCommand = new AsyncRelayCommand(
            MergeAsync,
            () => CanMerge);
        DeleteSelectionCommand = new AsyncRelayCommand(
            DeleteSelectionAsync,
            () => CanDeleteSelection);
        UndoCommand = new AsyncRelayCommand(
            UndoMaintenanceAsync,
            () => CanExecuteUndo);
        _maintenance.UndoStateChanged += OnUndoStateChanged;
        UpdateUndoState();
    }

    public IReadOnlyList<TaxonomyKindOption> KindOptions { get; } =
    [
        new(TaxonomyKind.Author, "作者"),
        new(TaxonomyKind.Series, "系列"),
        new(TaxonomyKind.Tag, "内容标签")
    ];

    public ObservableCollection<TaxonomyManagementTermDto> Terms { get; } = [];

    public event EventHandler? TermsReplacing;

    public event EventHandler? TermsReplaced;

    public TaxonomySelectionViewModel Selection { get; }

    public IAsyncRelayCommand<string?> CreateCommand { get; }

    public IAsyncRelayCommand<string?> RenameCommand { get; }

    public IAsyncRelayCommand MergeCommand { get; }

    public IAsyncRelayCommand DeleteSelectionCommand { get; }

    public IAsyncRelayCommand DeleteCommand => DeleteSelectionCommand;

    public IAsyncRelayCommand UndoCommand { get; }

    public TaxonomyKind SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (SetProperty(ref _selectedKind, value))
            {
                OnPropertyChanged(nameof(SelectedKindDisplayName));
                ClearSelectionForQueryChange();
                QueueReload(preserveSelection: false);
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            value ??= string.Empty;
            if (SetProperty(ref _searchText, value))
            {
                ClearSelectionForQueryChange();
                QueueReload(preserveSelection: false);
            }
        }
    }

    public TaxonomyReferenceFilter ReferenceFilter
    {
        get => _referenceFilter;
        set
        {
            if (SetProperty(ref _referenceFilter, value))
            {
                ClearSelectionForQueryChange();
                QueueReload(preserveSelection: false);
            }
        }
    }

    public TaxonomySortField SortField
    {
        get => _sortField;
        set
        {
            if (SetProperty(ref _sortField, value))
            {
                QueueReload(preserveSelection: true);
            }
        }
    }

    public TaxonomySortDirection SortDirection
    {
        get => _sortDirection;
        set
        {
            if (SetProperty(ref _sortDirection, value))
            {
                QueueReload(preserveSelection: true);
            }
        }
    }

    // The inspector uses this projection while the page synchronizes the
    // Extended ListView through Selection.
    public TaxonomyManagementTermDto? SelectedTerm
    {
        get => Selection.SingleSelectedTerm;
        set => ReplaceSelection(value is null ? [] : [value]);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyWriteCommandState();
            }
        }
    }

    public bool IsWriteBusy
    {
        get => _isWriteBusy;
        private set
        {
            if (SetProperty(ref _isWriteBusy, value))
            {
                NotifyWriteCommandState();
            }
        }
    }

    public IReadOnlyList<TaxonomyManagementTermDto> DeleteBlockers =>
        Selection.DeleteBlockers;

    public int SelectedCount => Selection.SelectedCount;

    public int SelectedReferenceCount =>
        Selection.Capture().Sum(term => term.ReferenceCount);

    public int SelectedInboxReferenceCount =>
        Selection.Capture().Sum(term => term.InboxReferenceCount);

    public int SelectedLibraryReferenceCount =>
        Selection.Capture().Sum(term => term.LibraryReferenceCount);

    public string SelectedDisplayName => SelectedTerm?.DisplayName ?? string.Empty;

    public string SelectedKindDisplayName => KindOptions
        .Single(option => option.Kind == SelectedKind)
        .DisplayName;

    public int SelectedTermInboxReferenceCount =>
        SelectedTerm?.InboxReferenceCount ?? 0;

    public int SelectedTermLibraryReferenceCount =>
        SelectedTerm?.LibraryReferenceCount ?? 0;

    public int SelectedTermReferenceCount => SelectedTerm?.ReferenceCount ?? 0;

    public Visibility EmptyInspectorVisibility =>
        Selection.SelectedCount == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility SingleInspectorVisibility =>
        Selection.IsSingleSelection
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility MultipleInspectorVisibility =>
        Selection.IsMultipleSelection
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility DeleteBlockersVisibility =>
        DeleteBlockers.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility RenameConflictVisibility =>
        RenameConflictTarget is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

    public TaxonomyManagementTermDto? RenameConflictTarget
    {
        get => _renameConflictTarget;
        private set
        {
            if (SetProperty(ref _renameConflictTarget, value))
            {
                OnPropertyChanged(nameof(CanMergeRenameConflict));
                OnPropertyChanged(nameof(RenameConflictVisibility));
            }
        }
    }

    public bool CanCreate => !IsBusy && !IsWriteBusy;

    public bool CanMerge =>
        Selection.SelectedCount >= 1 && !IsBusy && !IsWriteBusy;

    public bool CanDeleteSelection =>
        Selection.SelectedCount >= 1
        && DeleteBlockers.Count == 0
        && !IsBusy
        && !IsWriteBusy;

    public bool CanRename =>
        Selection.IsSingleSelection && !IsBusy && !IsWriteBusy;

    public bool CanDelete => CanDeleteSelection;

    public bool CanMergeRenameConflict =>
        Selection.IsSingleSelection
        && RenameConflictTarget is not null
        && !IsBusy
        && !IsWriteBusy;

    public bool CanExecuteUndo =>
        CanUndoMaintenance && !IsBusy && !IsWriteBusy;

    public bool CanUndoMaintenance
    {
        get => _canUndoMaintenance;
        private set
        {
            if (SetProperty(ref _canUndoMaintenance, value))
            {
                OnPropertyChanged(nameof(CanExecuteUndo));
            }
        }
    }

    public string UndoBannerMessage
    {
        get => _undoBannerMessage;
        private set => SetProperty(ref _undoBannerMessage, value);
    }

    public Task LoadAsync(CancellationToken cancellationToken) =>
        ReloadCurrentAsync(
            preferredSelectionId: null,
            preserveSelection: true,
            cancellationToken);

    public void ReplaceSelection(
        IEnumerable<TaxonomyManagementTermDto> selectedTerms)
    {
        Selection.ReplaceFromList(selectedTerms, Terms);
        RenameConflictTarget = null;
        SetSelectionStatus();
    }

    public Task SelectAsync(
        TaxonomyManagementTermDto? term,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SelectedTerm = term;
        return Task.CompletedTask;
    }

    public Task CreateAsync(
        string? name,
        CancellationToken cancellationToken) =>
        RunWriteAsync(
            token => CreateCoreAsync(name, token),
            cancellationToken);

    private async Task CreateCoreAsync(
        string? name,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "请输入词条名称。";
            return;
        }

        try
        {
            TaxonomyTermDto created = await _taxonomy.CreateAsync(
                SelectedKind,
                name,
                cancellationToken);
            RenameConflictTarget = null;
            await ReloadCurrentAsync(
                created.Id,
                preserveSelection: false,
                cancellationToken);
            StatusMessage = "词条已创建。";
        }
        catch (ArgumentException)
        {
            StatusMessage = "请输入有效的词条名称。";
        }
    }

    public Task RenameAsync(
        string? name,
        CancellationToken cancellationToken) =>
        RunWriteAsync(
            token => RenameCoreAsync(name, token),
            cancellationToken);

    private async Task RenameCoreAsync(
        string? name,
        CancellationToken cancellationToken)
    {
        TaxonomyManagementTermDto? selected = Selection.SingleSelectedTerm;
        if (selected is null || string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = selected is null
                ? "请先选择一个词条。"
                : "请输入词条名称。";
            return;
        }

        TaxonomyMutationResult result;
        try
        {
            result = await _taxonomy.RenameAsync(
                selected.Id,
                name,
                cancellationToken);
        }
        catch (ArgumentException)
        {
            StatusMessage = "请输入有效的词条名称。";
            return;
        }

        if (!result.Succeeded)
        {
            if (result.ErrorCode == "taxonomy.name_conflict")
            {
                RenameConflictTarget = await FindExactConflictAsync(
                    selected.Id,
                    name,
                    cancellationToken);
                StatusMessage = RenameConflictTarget is null
                    ? "词条名称已存在，请刷新后重试。"
                    : "词条名称已存在，可改为合并。";
            }
            else
            {
                RenameConflictTarget = null;
                StatusMessage = result.ErrorCode switch
                {
                    "taxonomy.not_found" => "词条已不存在。",
                    _ => "重命名失败，请重试。"
                };
            }

            return;
        }

        RenameConflictTarget = null;
        await ReloadCurrentAsync(
            selected.Id,
            preserveSelection: false,
            cancellationToken);
        StatusMessage = "词条已重命名。";
    }

    public Task MergeAsync(CancellationToken cancellationToken) =>
        RunWriteAsync(MergeCoreAsync, cancellationToken);

    private async Task MergeCoreAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TaxonomyManagementTermDto> selected = Selection.Capture();
        if (selected.Count == 0)
        {
            StatusMessage = "请先选择要合并的词条。";
            return;
        }

        Guid? preselectedTargetId = RenameConflictTarget?.Id;
        TaxonomyMergeDialogResult? dialogResult = await _dialogs.ShowMergeAsync(
            selected,
            preselectedTargetId,
            cancellationToken);
        if (dialogResult is null)
        {
            return;
        }

        TaxonomyMaintenanceResult result = dialogResult.MaintenanceResult;
        if (!result.Succeeded)
        {
            StatusMessage = MaintenanceFailureMessage(result.ErrorCode, "合并");
            return;
        }

        RenameConflictTarget = null;
        await ReloadCurrentAsync(
            dialogResult.TargetId,
            preserveSelection: true,
            cancellationToken);
        StatusMessage = "词条已合并。";
    }

    public Task DeleteSelectionAsync(CancellationToken cancellationToken) =>
        RunWriteAsync(DeleteSelectionCoreAsync, cancellationToken);

    private async Task DeleteSelectionCoreAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TaxonomyManagementTermDto> selected = Selection.Capture();
        if (selected.Count == 0)
        {
            StatusMessage = "请先选择要删除的词条。";
            return;
        }

        TaxonomyDeletePrepareResult prepared =
            await _maintenance.PrepareDeleteAsync(
                TaxonomyDeleteSelection.Create(
                    SelectedKind,
                    selected.Select(term => term.Id)),
                cancellationToken);
        if (!prepared.Succeeded || prepared.Preparation is null)
        {
            StatusMessage = MaintenanceFailureMessage(
                prepared.ErrorCode,
                "删除");
            return;
        }

        if (!prepared.Preparation.CanDelete)
        {
            StatusMessage = DeleteBlockedMessage(prepared.Preparation.Blockers.Count);
            return;
        }

        if (!await _dialogs.ConfirmDeleteAsync(
                prepared.Preparation,
                cancellationToken))
        {
            return;
        }

        TaxonomyMaintenanceResult result = await _maintenance.DeleteUnusedAsync(
            prepared.Preparation,
            cancellationToken);
        if (!result.Succeeded)
        {
            StatusMessage = MaintenanceFailureMessage(result.ErrorCode, "删除");
            return;
        }

        RenameConflictTarget = null;
        await ReloadCurrentAsync(
            preferredSelectionId: null,
            preserveSelection: true,
            cancellationToken);
        StatusMessage = "词条已删除。";
    }

    public Task DeleteAsync(CancellationToken cancellationToken) =>
        DeleteSelectionAsync(cancellationToken);

    public Task UndoMaintenanceAsync(CancellationToken cancellationToken) =>
        RunWriteAsync(UndoMaintenanceCoreAsync, cancellationToken);

    private async Task UndoMaintenanceCoreAsync(
        CancellationToken cancellationToken)
    {
        TaxonomyMaintenanceUndoResult result =
            await _maintenance.UndoLastAsync(cancellationToken);
        if (!result.Succeeded)
        {
            StatusMessage = MaintenanceFailureMessage(result.ErrorCode, "撤销");
            return;
        }

        await ReloadCurrentAsync(
            preferredSelectionId: null,
            preserveSelection: true,
            cancellationToken);
        StatusMessage = "上一次词表维护已撤销。";
    }

    public void ReportOperationFailure() =>
        StatusMessage = "词条操作失败，请重试。";

    private void ClearSelectionForQueryChange()
    {
        bool hadSelection = Selection.SelectedCount > 0;
        Selection.Clear();
        RenameConflictTarget = null;
        if (hadSelection)
        {
            StatusMessage = "筛选条件已更改，已清除选择。";
        }
    }

    private void QueueReload(bool preserveSelection)
    {
        long generation = Interlocked.Increment(ref _reloadGeneration);
        _ = ReloadSafeAsync(generation, preserveSelection);
    }

    private Task ReloadCurrentAsync(
        Guid? preferredSelectionId,
        bool preserveSelection,
        CancellationToken cancellationToken)
    {
        long generation = Interlocked.Increment(ref _reloadGeneration);
        return ReloadGenerationAsync(
            generation,
            preferredSelectionId,
            preserveSelection,
            cancellationToken);
    }

    private async Task ReloadGenerationAsync(
        long generation,
        Guid? preferredSelectionId,
        bool preserveSelection,
        CancellationToken cancellationToken)
    {
        await _reloadGate.WaitAsync(cancellationToken);
        IsBusy = true;
        try
        {
            TaxonomyManagementQuery query = TaxonomyManagementQuery.Create(
                SelectedKind,
                SearchText,
                ReferenceFilter,
                SortField,
                SortDirection);
            IReadOnlyList<TaxonomyManagementTermDto> terms =
                await _query.QueryAsync(query, cancellationToken);
            if (generation != Volatile.Read(ref _reloadGeneration))
            {
                return;
            }

            TermsReplacing?.Invoke(this, EventArgs.Empty);
            try
            {
                Terms.Clear();
                foreach (TaxonomyManagementTermDto term in terms)
                {
                    Terms.Add(term);
                }

                if (preserveSelection)
                {
                    Selection.Reconcile(terms);
                }
                else
                {
                    Selection.ReplaceFromList([], terms);
                }

                if (preferredSelectionId is Guid id
                    && terms.FirstOrDefault(term => term.Id == id) is { } preferred
                    && !Selection.SelectedIds.Contains(id))
                {
                    Selection.ReplaceFromList(
                        [.. Selection.Capture(), preferred],
                        terms);
                }

                NotifySelectionState();
            }
            finally
            {
                TermsReplaced?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            IsBusy = false;
            _reloadGate.Release();
        }
    }

    private async Task ReloadSafeAsync(
        long generation,
        bool preserveSelection)
    {
        try
        {
            await ReloadGenerationAsync(
                generation,
                preferredSelectionId: null,
                preserveSelection,
                CancellationToken.None);
        }
        catch (Exception error)
        {
            _logger?.LogError(error, "TaxonomyReloadFailed");
            StatusMessage = "加载词条失败，请重试。";
        }
    }

    private async Task<TaxonomyManagementTermDto?> FindExactConflictAsync(
        Guid sourceId,
        string name,
        CancellationToken cancellationToken)
    {
        string normalized;
        try
        {
            normalized = TaxonomyName.Create(name).Normalized;
        }
        catch (ArgumentException)
        {
            return null;
        }

        IReadOnlyList<TaxonomyManagementTermDto> matches =
            await _query.QueryAsync(
                TaxonomyManagementQuery.Create(
                    SelectedKind,
                    null,
                    TaxonomyReferenceFilter.All,
                    TaxonomySortField.DisplayName,
                    TaxonomySortDirection.Ascending),
                cancellationToken);
        return matches.FirstOrDefault(term =>
            term.Id != sourceId
            && string.Equals(
                term.NormalizedName,
                normalized,
                StringComparison.Ordinal));
    }

    private void OnSelectionPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        NotifySelectionState();
    }

    private void NotifySelectionState()
    {
        OnPropertyChanged(nameof(SelectedTerm));
        OnPropertyChanged(nameof(DeleteBlockers));
        OnPropertyChanged(nameof(CanMerge));
        OnPropertyChanged(nameof(CanDeleteSelection));
        OnPropertyChanged(nameof(CanRename));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanMergeRenameConflict));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedReferenceCount));
        OnPropertyChanged(nameof(SelectedInboxReferenceCount));
        OnPropertyChanged(nameof(SelectedLibraryReferenceCount));
        OnPropertyChanged(nameof(SelectedDisplayName));
        OnPropertyChanged(nameof(SelectedTermInboxReferenceCount));
        OnPropertyChanged(nameof(SelectedTermLibraryReferenceCount));
        OnPropertyChanged(nameof(SelectedTermReferenceCount));
        OnPropertyChanged(nameof(EmptyInspectorVisibility));
        OnPropertyChanged(nameof(SingleInspectorVisibility));
        OnPropertyChanged(nameof(MultipleInspectorVisibility));
        OnPropertyChanged(nameof(DeleteBlockersVisibility));
        RenameCommand.NotifyCanExecuteChanged();
        MergeCommand.NotifyCanExecuteChanged();
        DeleteSelectionCommand.NotifyCanExecuteChanged();
    }

    private async Task RunWriteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        int pending = Interlocked.Increment(ref _pendingWriteCount);
        if (pending == 1)
        {
            IsWriteBusy = true;
        }

        bool entered = false;
        try
        {
            await _writeGate.WaitAsync(cancellationToken);
            entered = true;
            await operation(cancellationToken);
        }
        finally
        {
            if (entered)
            {
                _writeGate.Release();
            }

            if (Interlocked.Decrement(ref _pendingWriteCount) == 0)
            {
                IsWriteBusy = false;
            }
        }
    }

    private void NotifyWriteCommandState()
    {
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(CanMerge));
        OnPropertyChanged(nameof(CanDeleteSelection));
        OnPropertyChanged(nameof(CanRename));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanMergeRenameConflict));
        OnPropertyChanged(nameof(CanExecuteUndo));
        CreateCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        MergeCommand.NotifyCanExecuteChanged();
        DeleteSelectionCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }

    private void SetSelectionStatus()
    {
        StatusMessage = DeleteBlockers.Count > 0
            ? DeleteBlockedMessage(DeleteBlockers.Count)
            : string.Empty;
    }

    private void OnUndoStateChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        UpdateUndoState();
    }

    private void UpdateUndoState()
    {
        TaxonomyMaintenanceUndoState state = _maintenance.UndoState;
        CanUndoMaintenance = state.IsAvailable;
        UndoBannerMessage = state.IsAvailable
            ? $"可撤销上一次词表维护：影响 {state.AffectedOwnerCount} 项资料、{state.AffectedTermCount} 个词条。"
            : string.Empty;
        UndoCommand?.NotifyCanExecuteChanged();
    }

    private static string DeleteBlockedMessage(int blockerCount) =>
        $"所选词条中有 {blockerCount} 个仍被资料引用，不能删除。";

    private static string MaintenanceFailureMessage(
        string? errorCode,
        string operation) => errorCode switch
        {
            TaxonomyMaintenanceErrorCodes.SelectionInvalid =>
                "选择已失效，请刷新后重试。",
            TaxonomyMaintenanceErrorCodes.NotFound =>
                "词条已不存在，请刷新后重试。",
            TaxonomyMaintenanceErrorCodes.KindMismatch =>
                "所选词条类型不一致，请重新选择。",
            TaxonomyMaintenanceErrorCodes.TargetInvalid =>
                "合并目标无效，请重新选择。",
            TaxonomyMaintenanceErrorCodes.NameInvalid =>
                "请输入有效的词条名称。",
            TaxonomyMaintenanceErrorCodes.NameConflict =>
                "目标名称已存在，请重新选择。",
            TaxonomyMaintenanceErrorCodes.PreviewStale =>
                "合并预览已过期，请重新确认。",
            TaxonomyMaintenanceErrorCodes.DeleteInUse =>
                "所选词条已被资料引用，不能删除。",
            TaxonomyMaintenanceErrorCodes.UndoUnavailable =>
                "当前没有可撤销的词表操作。",
            TaxonomyMaintenanceErrorCodes.UndoConflict =>
                "相关资料已经变化，无法安全撤销。",
            _ => $"{operation}失败，请重试。"
        };

}
