using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed record TaxonomyMergePreviewDisplayRow(
    string LocationLabel,
    string VisibleName,
    string BeforeSummary,
    string AfterSummary);

public sealed class TaxonomyMergeDialogViewModel : ObservableObject
{
    private static readonly TaxonomyMergePreviewDto EmptyPreview =
        new([], 0, 0, false,
            TaxonomyMaintenanceErrorCodes.SelectionInvalid);

    private readonly IReadOnlyList<TaxonomyManagementTermDto> _selectedTerms;
    private readonly Guid? _preselectedTargetId;
    private readonly ITaxonomyManagementQueryService _query;
    private readonly ITaxonomyMaintenanceService _maintenance;
    private CancellationTokenSource? _prepareCancellation;
    private long _prepareGeneration;
    private long _searchGeneration;
    private string _targetSearchText = string.Empty;
    private TaxonomyManagementTermDto? _target;
    private bool _renameTarget;
    private string _finalNameText = string.Empty;
    private TaxonomyMergePreparation? _preparation;
    private TaxonomyMergePreviewDto _preview = EmptyPreview;
    private string _statusMessage = string.Empty;
    private bool _isPreparing;
    private bool _isSubmitting;
    private TaxonomyMaintenanceResult? _lastResult;

    public TaxonomyMergeDialogViewModel(
        IReadOnlyList<TaxonomyManagementTermDto> selectedTerms,
        Guid? preselectedTargetId,
        ITaxonomyManagementQueryService query,
        ITaxonomyMaintenanceService maintenance)
    {
        ArgumentNullException.ThrowIfNull(selectedTerms);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(maintenance);
        if (selectedTerms.Count == 0
            || selectedTerms.Any(term => term.Id == Guid.Empty)
            || selectedTerms.Select(term => term.Id).Distinct().Count()
                != selectedTerms.Count
            || selectedTerms.Select(term => term.Kind).Distinct().Count() != 1)
        {
            throw new ArgumentException(
                "The taxonomy merge selection is invalid.",
                nameof(selectedTerms));
        }

        if (preselectedTargetId == Guid.Empty)
        {
            throw new ArgumentException(
                "The preselected taxonomy target is invalid.",
                nameof(preselectedTargetId));
        }

        _selectedTerms = selectedTerms.ToArray();
        _preselectedTargetId = preselectedTargetId;
        _query = query;
        _maintenance = maintenance;
        ApplyCommand = new AsyncRelayCommand(
            async token => await ApplyAsync(token),
            () => CanApply);
    }

    public TaxonomyKind Kind => _selectedTerms[0].Kind;

    public IReadOnlyList<TaxonomyManagementTermDto> SelectedTerms =>
        _selectedTerms;

    public ObservableCollection<TaxonomyManagementTermDto> TargetResults
        { get; } = [];

    public ObservableCollection<TaxonomyManagementTermDto> Sources
        { get; } = [];

    public ObservableCollection<TaxonomyMergePreviewDisplayRow>
        PreviewDisplayRows { get; } = [];

    public IAsyncRelayCommand ApplyCommand { get; }

    public string TargetSearchText
    {
        get => _targetSearchText;
        private set => SetProperty(ref _targetSearchText, value);
    }

    public TaxonomyManagementTermDto? Target
    {
        get => _target;
        private set => SetProperty(ref _target, value);
    }

    public bool RenameTarget
    {
        get => _renameTarget;
        set
        {
            if (SetProperty(ref _renameTarget, value))
            {
                ClearSuccessfulResult();
                RefreshPreview();
            }
        }
    }

    public string FinalNameText
    {
        get => _finalNameText;
        set
        {
            value ??= string.Empty;
            if (SetProperty(ref _finalNameText, value))
            {
                ClearSuccessfulResult();
                RefreshPreview();
            }
        }
    }

    public TaxonomyMergePreparation? Preparation
    {
        get => _preparation;
        private set => SetProperty(ref _preparation, value);
    }

    public TaxonomyMergePreviewDto Preview
    {
        get => _preview;
        private set
        {
            if (SetProperty(ref _preview, value))
            {
                ReplacePreviewDisplayRows();
                OnPropertyChanged(nameof(PreviewRows));
                OnPropertyChanged(nameof(AffectedInboxCount));
                OnPropertyChanged(nameof(AffectedLibraryCount));
                OnPropertyChanged(nameof(AffectedTotalCount));
                NotifyApplyState();
            }
        }
    }

    public IReadOnlyList<TaxonomyMergePreviewRowDto> PreviewRows =>
        Preview.Rows;

    public int AffectedInboxCount => Preview.InboxAffectedCount;

    public int AffectedLibraryCount => Preview.LibraryAffectedCount;

    public int AffectedTotalCount =>
        checked(AffectedInboxCount + AffectedLibraryCount);

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsPreparing
    {
        get => _isPreparing;
        private set
        {
            if (SetProperty(ref _isPreparing, value))
            {
                NotifyApplyState();
            }
        }
    }

    public bool IsSubmitting
    {
        get => _isSubmitting;
        private set
        {
            if (SetProperty(ref _isSubmitting, value))
            {
                NotifyApplyState();
            }
        }
    }

    public TaxonomyMaintenanceResult? LastResult
    {
        get => _lastResult;
        private set
        {
            if (SetProperty(ref _lastResult, value))
            {
                NotifyApplyState();
            }
        }
    }

    public bool CanApply =>
        !IsPreparing
        && !IsSubmitting
        && LastResult?.Succeeded != true
        && Preparation is not null
        && Preview.CanApply;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await SearchTargetsAsync(string.Empty, cancellationToken);
        if (_preselectedTargetId is not Guid targetId)
        {
            return;
        }

        TaxonomyManagementTermDto? target = TargetResults.FirstOrDefault(
            term => term.Id == targetId);
        if (target is null)
        {
            StatusMessage = "预设的合并目标已不存在，请重新选择。";
            return;
        }

        await SelectTargetAsync(target, cancellationToken);
    }

    public async Task SearchTargetsAsync(
        string? searchText,
        CancellationToken cancellationToken)
    {
        string normalizedSearch = string.IsNullOrWhiteSpace(searchText)
            ? string.Empty
            : searchText.Trim();
        TargetSearchText = normalizedSearch;
        long generation = Interlocked.Increment(ref _searchGeneration);
        IReadOnlyList<TaxonomyManagementTermDto> results =
            await _query.QueryAsync(
                TaxonomyManagementQuery.Create(
                    Kind,
                    normalizedSearch,
                    TaxonomyReferenceFilter.All,
                    TaxonomySortField.DisplayName,
                    TaxonomySortDirection.Ascending),
                cancellationToken);
        if (generation != Volatile.Read(ref _searchGeneration))
        {
            return;
        }

        TargetResults.Clear();
        foreach (TaxonomyManagementTermDto result in results.Where(
                     term => term.Kind == Kind))
        {
            TargetResults.Add(result);
        }
    }

    public async Task SelectTargetAsync(
        TaxonomyManagementTermDto? target,
        CancellationToken cancellationToken)
    {
        long generation = Interlocked.Increment(ref _prepareGeneration);
        var nextCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        CancellationTokenSource? previous = Interlocked.Exchange(
            ref _prepareCancellation,
            nextCancellation);
        previous?.Cancel();
        previous?.Dispose();

        Target = target;
        Sources.Clear();
        Preparation = null;
        Preview = EmptyPreview;
        LastResult = null;
        if (target is null)
        {
            StatusMessage = "请选择一个现有词条作为目标。";
            FinishPreparation(nextCancellation, generation);
            return;
        }

        if (target.Kind != Kind)
        {
            StatusMessage = "合并目标必须属于同一种词表。";
            FinishPreparation(nextCancellation, generation);
            return;
        }

        foreach (TaxonomyManagementTermDto source in _selectedTerms.Where(
                     term => term.Id != target.Id))
        {
            Sources.Add(source);
        }

        _finalNameText = target.DisplayName;
        OnPropertyChanged(nameof(FinalNameText));
        if (Sources.Count == 0)
        {
            StatusMessage = "至少保留一个源词条才能合并。";
            FinishPreparation(nextCancellation, generation);
            return;
        }

        IsPreparing = true;
        try
        {
            TaxonomyMergeSelection selection = TaxonomyMergeSelection.Create(
                Kind,
                Sources.Select(source => source.Id),
                target.Id);
            TaxonomyMergePrepareResult result =
                await _maintenance.PrepareMergeAsync(
                    selection,
                    nextCancellation.Token);
            if (generation != Volatile.Read(ref _prepareGeneration))
            {
                return;
            }

            if (!result.Succeeded || result.Preparation is null)
            {
                StatusMessage = ErrorMessage(result.ErrorCode);
                return;
            }

            Preparation = result.Preparation;
            RefreshPreview();
        }
        catch (OperationCanceledException)
            when (nextCancellation.IsCancellationRequested
                  && !cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (generation == Volatile.Read(ref _prepareGeneration))
            {
                StatusMessage = ErrorMessage(
                    TaxonomyMaintenanceErrorCodes.PersistenceFailed);
            }
        }
        finally
        {
            FinishPreparation(nextCancellation, generation);
        }
    }

    public async Task<TaxonomyMaintenanceResult?> ApplyAsync(
        CancellationToken cancellationToken)
    {
        TaxonomyMergePreparation? preparation = Preparation;
        if (!CanApply || preparation is null)
        {
            StatusMessage = ErrorMessage(Preview.ErrorCode);
            return null;
        }

        IsSubmitting = true;
        try
        {
            string? finalName = RenameTarget ? FinalNameText : null;
            TaxonomyMaintenanceResult result =
                await _maintenance.ApplyMergeAsync(
                    preparation,
                    finalName,
                    cancellationToken);
            LastResult = result;
            StatusMessage = result.Succeeded
                ? $"已合并词条，更新 {result.ChangedInboxCount} 项待入库资料和 {result.ChangedLibraryCount} 项正式资料。"
                : ErrorMessage(result.ErrorCode);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "合并已取消，当前输入已保留。";
            return null;
        }
        catch (Exception)
        {
            StatusMessage = ErrorMessage(
                TaxonomyMaintenanceErrorCodes.PersistenceFailed);
            var failure = new TaxonomyMaintenanceResult(
                false,
                0,
                0,
                0,
                false,
                TaxonomyMaintenanceErrorCodes.PersistenceFailed);
            LastResult = failure;
            return failure;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    public static string ErrorMessage(string? errorCode) => errorCode switch
    {
        TaxonomyMaintenanceErrorCodes.SelectionInvalid =>
            "合并选择已失效，请重新选择。",
        TaxonomyMaintenanceErrorCodes.NotFound =>
            "源词条或目标词条已不存在，请刷新后重试。",
        TaxonomyMaintenanceErrorCodes.KindMismatch =>
            "源词条与目标词条必须属于同一种词表。",
        TaxonomyMaintenanceErrorCodes.TargetInvalid =>
            "合并目标无效，请重新选择。",
        TaxonomyMaintenanceErrorCodes.NameInvalid =>
            "请输入有效的目标名称。",
        TaxonomyMaintenanceErrorCodes.NameConflict =>
            "该名称属于另一个词条，请将它改选为合并目标。",
        TaxonomyMaintenanceErrorCodes.PreviewStale =>
            "合并预览已过期，请重新选择目标。",
        _ => "合并未完成，当前输入已保留，可直接重试。"
    };

    public void ReportOperationFailure() =>
        StatusMessage = ErrorMessage(
            TaxonomyMaintenanceErrorCodes.PersistenceFailed);

    private void RefreshPreview()
    {
        if (Preparation is null)
        {
            NotifyApplyState();
            return;
        }

        try
        {
            Preview = _maintenance.PreviewMerge(
                Preparation,
                RenameTarget ? FinalNameText : null);
            StatusMessage = Preview.CanApply
                ? string.Empty
                : ErrorMessage(Preview.ErrorCode);
        }
        catch (Exception)
        {
            Preview = new TaxonomyMergePreviewDto(
                [],
                0,
                0,
                false,
                TaxonomyMaintenanceErrorCodes.PersistenceFailed);
            StatusMessage = ErrorMessage(Preview.ErrorCode);
        }
    }

    private void ClearSuccessfulResult()
    {
        if (LastResult?.Succeeded == true)
        {
            LastResult = null;
        }
    }

    private void ReplacePreviewDisplayRows()
    {
        PreviewDisplayRows.Clear();
        foreach (TaxonomyMergePreviewRowDto row in Preview.Rows)
        {
            PreviewDisplayRows.Add(new TaxonomyMergePreviewDisplayRow(
                row.Owner.Kind == MetadataOwnerKind.InboxItem
                    ? "待入库"
                    : "正式库",
                row.VisibleName,
                DescribeRelevantTerms(row.Before),
                DescribeRelevantTerms(row.After)));
        }
    }

    private string DescribeRelevantTerms(TaxonomyAssignmentSnapshot assignment)
    {
        IEnumerable<Guid> ids = Kind switch
        {
            TaxonomyKind.Author => assignment.AuthorIds,
            TaxonomyKind.Series => assignment.SeriesId is Guid seriesId
                ? [seriesId]
                : [],
            TaxonomyKind.Tag => assignment.TagIds,
            _ => []
        };
        Dictionary<Guid, string> visibleNames = Sources
            .Append(Target)
            .Where(term => term is not null)
            .Cast<TaxonomyManagementTermDto>()
            .GroupBy(term => term.Id)
            .ToDictionary(group => group.Key, group => group.First().DisplayName);
        string[] relevant = ids
            .Where(visibleNames.ContainsKey)
            .Select(id => visibleNames[id])
            .ToArray();
        return relevant.Length == 0 ? "无相关词条" : string.Join("、", relevant);
    }

    private void FinishPreparation(
        CancellationTokenSource cancellation,
        long generation)
    {
        if (generation == Volatile.Read(ref _prepareGeneration))
        {
            IsPreparing = false;
            Interlocked.CompareExchange(
                ref _prepareCancellation,
                null,
                cancellation);
        }

        cancellation.Dispose();
    }

    private void NotifyApplyState()
    {
        OnPropertyChanged(nameof(CanApply));
        ApplyCommand.NotifyCanExecuteChanged();
    }
}
