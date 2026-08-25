using CommunityToolkit.Mvvm.ComponentModel;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed partial class BatchTermCoverageOptionViewModel(
    BatchTermCoverageDto coverage,
    int totalSelected) : ObservableObject
{
    public BatchTermCoverageDto Coverage { get; } = coverage;

    public int TotalSelected { get; } = totalSelected;

    public string DisplayText { get; } =
        $"{coverage.Term.DisplayName} · {coverage.SelectedCount}/{totalSelected}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

public sealed partial class BatchMetadataDialogViewModel : ObservableObject
{
    private IReadOnlyList<InboxSelectionEntry> _selection = [];

    public BatchMetadataDialogViewModel(ITaxonomySearchService taxonomy)
    {
        AddAuthorPicker = new(taxonomy, TaxonomyKind.Author);
        AddTagPicker = new(taxonomy, TaxonomyKind.Tag);
        RemoveAuthorPicker = new(taxonomy, TaxonomyKind.Author);
        RemoveTagPicker = new(taxonomy, TaxonomyKind.Tag);
        ReplaceAuthorPicker = new(taxonomy, TaxonomyKind.Author);
        ReplaceSeriesPicker = new(taxonomy, TaxonomyKind.Series);
        ReplaceTagPicker = new(taxonomy, TaxonomyKind.Tag);
    }

    public BatchTaxonomyPickerViewModel AddAuthorPicker { get; }

    public BatchTaxonomyPickerViewModel AddTagPicker { get; }

    public BatchTaxonomyPickerViewModel RemoveAuthorPicker { get; }

    public BatchTaxonomyPickerViewModel RemoveTagPicker { get; }

    public BatchTaxonomyPickerViewModel ReplaceAuthorPicker { get; }

    public BatchTaxonomyPickerViewModel ReplaceSeriesPicker { get; }

    public BatchTaxonomyPickerViewModel ReplaceTagPicker { get; }

    public IReadOnlyList<BatchTermCoverageOptionViewModel> RemoveAuthors =>
        RemoveAuthorPicker.AvailableExisting;

    public IReadOnlyList<BatchTermCoverageOptionViewModel> RemoveTags =>
        RemoveTagPicker.AvailableExisting;

    [ObservableProperty]
    public partial BatchMetadataMode Mode { get; set; } = BatchMetadataMode.Add;

    [ObservableProperty]
    public partial int SelectedCount { get; private set; }

    [ObservableProperty]
    public partial bool ReplaceAuthorsEnabled { get; set; }

    [ObservableProperty]
    public partial bool ClearAuthors { get; set; }

    [ObservableProperty]
    public partial bool ReplaceSeriesEnabled { get; set; }

    [ObservableProperty]
    public partial bool ClearSeries { get; set; }

    [ObservableProperty]
    public partial bool ReplaceTagsEnabled { get; set; }

    [ObservableProperty]
    public partial bool ClearTags { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial BatchMetadataResult? LastResult { get; private set; }

    [ObservableProperty]
    public partial bool IsSubmitting { get; set; }

    public bool CanSubmit => !IsSubmitting;

    public bool ReplaceAuthorsInputEnabled =>
        ReplaceAuthorsEnabled && !ClearAuthors;

    public bool ReplaceSeriesInputEnabled =>
        ReplaceSeriesEnabled && !ClearSeries;

    public bool ReplaceTagsInputEnabled =>
        ReplaceTagsEnabled && !ClearTags;

    public void Load(
        BatchMetadataEditorDto editor,
        IReadOnlyList<InboxSelectionEntry> selection)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(selection);
        if (editor.SelectedCount != selection.Count)
        {
            throw new ArgumentException(
                "The editor summary does not match the selection snapshot.");
        }

        _selection = selection.ToArray();
        SelectedCount = editor.SelectedCount;
        Mode = BatchMetadataMode.Add;
        ReplaceAuthorsEnabled = false;
        ClearAuthors = false;
        ReplaceSeriesEnabled = false;
        ClearSeries = false;
        ReplaceTagsEnabled = false;
        ClearTags = false;
        StatusMessage = string.Empty;
        LastResult = null;

        AddAuthorPicker.ResetSelection();
        AddTagPicker.ResetSelection();
        ReplaceAuthorPicker.ResetSelection();
        ReplaceSeriesPicker.ResetSelection();
        ReplaceTagPicker.ResetSelection();
        RemoveAuthorPicker.SetAvailableExisting(editor.Authors.Select(
            coverage => new BatchTermCoverageOptionViewModel(
                coverage,
                editor.SelectedCount)));
        RemoveTagPicker.SetAvailableExisting(editor.Tags.Select(
            coverage => new BatchTermCoverageOptionViewModel(
                coverage,
                editor.SelectedCount)));
        OnPropertyChanged(nameof(RemoveAuthors));
        OnPropertyChanged(nameof(RemoveTags));
    }

    public bool TryBuildCommand(
        out BatchMetadataCommand? command,
        out string? error)
    {
        try
        {
            command = Mode switch
            {
                BatchMetadataMode.Add => BuildAddCommand(),
                BatchMetadataMode.Remove => BuildRemoveCommand(),
                BatchMetadataMode.Replace => BuildReplaceCommand(),
                _ => throw new BatchMetadataEditorValidationException(
                    "批量资料操作无效。")
            };
            error = null;
            StatusMessage = string.Empty;
            return true;
        }
        catch (BatchMetadataEditorValidationException validation)
        {
            command = null;
            error = validation.Message;
            StatusMessage = validation.Message;
            return false;
        }
        catch (ArgumentException)
        {
            command = null;
            error = "批量资料输入无效，请检查后重试。";
            StatusMessage = error;
            return false;
        }
    }

    public void ApplyResult(BatchMetadataResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        LastResult = result;
        StatusMessage = result.Succeeded
            ? $"已修改 {result.ChangedCount} 项资料，{result.UnchangedCount} 项无变化。"
            : ResultError(result.ErrorCode);
    }

    public void ReportInputError(string message)
    {
        StatusMessage = string.IsNullOrWhiteSpace(message)
            ? "输入无效，请检查后重试。"
            : message;
    }

    partial void OnIsSubmittingChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(CanSubmit));
    }

    partial void OnReplaceAuthorsEnabledChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(ReplaceAuthorsInputEnabled));
    }

    partial void OnClearAuthorsChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(ReplaceAuthorsInputEnabled));
    }

    partial void OnReplaceSeriesEnabledChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(ReplaceSeriesInputEnabled));
    }

    partial void OnClearSeriesChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(ReplaceSeriesInputEnabled));
    }

    partial void OnReplaceTagsEnabledChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(ReplaceTagsInputEnabled));
    }

    partial void OnClearTagsChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(ReplaceTagsInputEnabled));
    }

    private BatchMetadataCommand BuildAddCommand()
    {
        BatchTaxonomyReference[] authors =
            AddAuthorPicker.SelectedValues.ToArray();
        BatchTaxonomyReference[] tags = AddTagPicker.SelectedValues.ToArray();
        if (authors.Length == 0 && tags.Length == 0)
        {
            throw new BatchMetadataEditorValidationException(
                "请先选择至少一个要添加的作者或标签。");
        }

        return BatchMetadataCommand.Create(
            _selection,
            BatchMetadataMode.Add,
            SetEdit(BatchMetadataFieldOperation.Add, authors),
            BatchMetadataSeriesEdit.Unchanged,
            SetEdit(BatchMetadataFieldOperation.Add, tags));
    }

    private BatchMetadataCommand BuildRemoveCommand()
    {
        BatchTaxonomyReference[] authors = RemoveAuthors
            .Where(option => option.IsSelected)
            .Select(option => BatchTaxonomyReference.Existing(
                TaxonomyKind.Author,
                option.Coverage.Term.Id))
            .ToArray();
        BatchTaxonomyReference[] tags = RemoveTags
            .Where(option => option.IsSelected)
            .Select(option => BatchTaxonomyReference.Existing(
                TaxonomyKind.Tag,
                option.Coverage.Term.Id))
            .ToArray();
        if (authors.Length == 0 && tags.Length == 0)
        {
            throw new BatchMetadataEditorValidationException(
                "请先选择至少一个要移除的作者或标签。");
        }

        return BatchMetadataCommand.Create(
            _selection,
            BatchMetadataMode.Remove,
            SetEdit(BatchMetadataFieldOperation.Remove, authors),
            BatchMetadataSeriesEdit.Unchanged,
            SetEdit(BatchMetadataFieldOperation.Remove, tags));
    }

    private BatchMetadataCommand BuildReplaceCommand()
    {
        if (!ReplaceAuthorsEnabled
            && !ReplaceSeriesEnabled
            && !ReplaceTagsEnabled)
        {
            throw new BatchMetadataEditorValidationException(
                "请先启用至少一个要替换的字段。");
        }

        BatchMetadataSetEdit authors = ReplaceSetEdit(
            ReplaceAuthorsEnabled,
            ClearAuthors,
            ReplaceAuthorPicker.SelectedValues);
        BatchMetadataSeriesEdit series = ReplaceSeriesEdit();
        BatchMetadataSetEdit tags = ReplaceSetEdit(
            ReplaceTagsEnabled,
            ClearTags,
            ReplaceTagPicker.SelectedValues);
        return BatchMetadataCommand.Create(
            _selection,
            BatchMetadataMode.Replace,
            authors,
            series,
            tags);
    }

    private BatchMetadataSeriesEdit ReplaceSeriesEdit()
    {
        if (!ReplaceSeriesEnabled)
        {
            return BatchMetadataSeriesEdit.Unchanged;
        }

        if (ClearSeries)
        {
            return new(BatchMetadataFieldOperation.Clear, null);
        }

        BatchTaxonomyReference? selected =
            ReplaceSeriesPicker.SelectedValues.SingleOrDefault();
        return selected is null
            ? throw new BatchMetadataEditorValidationException(
                "已启用的替换字段必须选择资料或明确清空。")
            : new(BatchMetadataFieldOperation.Replace, selected);
    }

    private static BatchMetadataSetEdit ReplaceSetEdit(
        bool enabled,
        bool clear,
        IReadOnlyList<BatchTaxonomyReference> values)
    {
        if (!enabled)
        {
            return BatchMetadataSetEdit.Unchanged;
        }

        if (clear)
        {
            return new(BatchMetadataFieldOperation.Clear, []);
        }

        return values.Count == 0
            ? throw new BatchMetadataEditorValidationException(
                "已启用的替换字段必须选择资料或明确清空。")
            : new(BatchMetadataFieldOperation.Replace, values.ToArray());
    }

    private static BatchMetadataSetEdit SetEdit(
        BatchMetadataFieldOperation operation,
        IReadOnlyList<BatchTaxonomyReference> values) =>
        values.Count == 0
            ? BatchMetadataSetEdit.Unchanged
            : new(operation, values);

    private static string ResultError(string? errorCode) => errorCode switch
    {
        "batch.metadata.version_conflict" =>
            "选择中的资料已变化，当前输入已保留，请重新提交。",
        "batch.metadata.selection_changed" =>
            "选择已变化，当前输入已保留，请重新选择后重试。",
        _ => "批量修改未完成，当前输入已保留，可直接重试。"
    };

    private sealed class BatchMetadataEditorValidationException(string message)
        : InvalidOperationException(message);
}
