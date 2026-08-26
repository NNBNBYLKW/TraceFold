using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed partial class LibraryMetadataDraftViewModel : ObservableObject
{
    public LibraryMetadataDraftViewModel(ITaxonomySearchService taxonomy)
    {
        ArgumentNullException.ThrowIfNull(taxonomy);
        AddAuthorPicker = new(taxonomy, TaxonomyKind.Author);
        AddTagPicker = new(taxonomy, TaxonomyKind.Tag);
        RemoveAuthorPicker = new(taxonomy, TaxonomyKind.Author);
        RemoveTagPicker = new(taxonomy, TaxonomyKind.Tag);
        ReplaceAuthorPicker = new(taxonomy, TaxonomyKind.Author);
        ReplaceSeriesPicker = new(taxonomy, TaxonomyKind.Series);
        ReplaceTagPicker = new(taxonomy, TaxonomyKind.Tag);

        Observe(AddAuthorPicker);
        Observe(AddTagPicker);
        Observe(ReplaceAuthorPicker);
        Observe(ReplaceSeriesPicker);
        Observe(ReplaceTagPicker);
    }

    public event EventHandler? DraftChanged;

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

    public bool ReplaceAuthorsInputEnabled =>
        ReplaceAuthorsEnabled && !ClearAuthors;

    public bool ReplaceSeriesInputEnabled =>
        ReplaceSeriesEnabled && !ClearSeries;

    public bool ReplaceTagsInputEnabled =>
        ReplaceTagsEnabled && !ClearTags;

    public void Load(LibraryBatchEditorDto editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (editor.Assets.Count == 0)
        {
            throw new ArgumentException(
                "The formal library editor cannot be empty.",
                nameof(editor));
        }

        SelectedCount = editor.Assets.Count;
        Mode = BatchMetadataMode.Add;
        ReplaceAuthorsEnabled = false;
        ClearAuthors = false;
        ReplaceSeriesEnabled = false;
        ClearSeries = false;
        ReplaceTagsEnabled = false;
        ClearTags = false;
        StatusMessage = string.Empty;

        AddAuthorPicker.ResetSelection();
        AddTagPicker.ResetSelection();
        ReplaceAuthorPicker.ResetSelection();
        ReplaceSeriesPicker.ResetSelection();
        ReplaceTagPicker.ResetSelection();

        UnobserveOptions(RemoveAuthorPicker.AvailableExisting);
        UnobserveOptions(RemoveTagPicker.AvailableExisting);
        RemoveAuthorPicker.SetAvailableExisting(editor.Authors.Select(
            coverage => new BatchTermCoverageOptionViewModel(
                coverage,
                editor.Assets.Count)));
        RemoveTagPicker.SetAvailableExisting(editor.Tags.Select(
            coverage => new BatchTermCoverageOptionViewModel(
                coverage,
                editor.Assets.Count)));
        ObserveOptions(RemoveAuthorPicker.AvailableExisting);
        ObserveOptions(RemoveTagPicker.AvailableExisting);
        OnPropertyChanged(nameof(RemoveAuthors));
        OnPropertyChanged(nameof(RemoveTags));
        RaiseDraftChanged();
    }

    public bool TryBuildDraft(
        out BatchMetadataDraft? draft,
        out string? error)
    {
        try
        {
            draft = Mode switch
            {
                BatchMetadataMode.Add => BuildAddDraft(),
                BatchMetadataMode.Remove => BuildRemoveDraft(),
                BatchMetadataMode.Replace => BuildReplaceDraft(),
                _ => throw new MetadataDraftValidationException(
                    "批量资料操作无效。")
            };
            error = null;
            StatusMessage = string.Empty;
            return true;
        }
        catch (MetadataDraftValidationException validation)
        {
            draft = null;
            error = validation.Message;
            StatusMessage = validation.Message;
            return false;
        }
        catch (ArgumentException)
        {
            draft = null;
            error = "批量资料输入无效，请检查后重试。";
            StatusMessage = error;
            return false;
        }
    }

    partial void OnModeChanged(BatchMetadataMode value) => RaiseDraftChanged();

    partial void OnReplaceAuthorsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ReplaceAuthorsInputEnabled));
        RaiseDraftChanged();
    }

    partial void OnClearAuthorsChanged(bool value)
    {
        OnPropertyChanged(nameof(ReplaceAuthorsInputEnabled));
        RaiseDraftChanged();
    }

    partial void OnReplaceSeriesEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ReplaceSeriesInputEnabled));
        RaiseDraftChanged();
    }

    partial void OnClearSeriesChanged(bool value)
    {
        OnPropertyChanged(nameof(ReplaceSeriesInputEnabled));
        RaiseDraftChanged();
    }

    partial void OnReplaceTagsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ReplaceTagsInputEnabled));
        RaiseDraftChanged();
    }

    partial void OnClearTagsChanged(bool value)
    {
        OnPropertyChanged(nameof(ReplaceTagsInputEnabled));
        RaiseDraftChanged();
    }

    private BatchMetadataDraft? BuildAddDraft()
    {
        BatchTaxonomyReference[] authors =
            PreviewableValues(AddAuthorPicker);
        BatchTaxonomyReference[] tags = PreviewableValues(AddTagPicker);
        if (authors.Length == 0 && tags.Length == 0)
        {
            return null;
        }

        return BatchMetadataDraft.Create(
            BatchMetadataMode.Add,
            SetEdit(BatchMetadataFieldOperation.Add, authors),
            BatchMetadataSeriesEdit.Unchanged,
            SetEdit(BatchMetadataFieldOperation.Add, tags));
    }

    private BatchMetadataDraft? BuildRemoveDraft()
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
            return null;
        }

        return BatchMetadataDraft.Create(
            BatchMetadataMode.Remove,
            SetEdit(BatchMetadataFieldOperation.Remove, authors),
            BatchMetadataSeriesEdit.Unchanged,
            SetEdit(BatchMetadataFieldOperation.Remove, tags));
    }

    private BatchMetadataDraft? BuildReplaceDraft()
    {
        if (!ReplaceAuthorsEnabled
            && !ReplaceSeriesEnabled
            && !ReplaceTagsEnabled)
        {
            return null;
        }

        return BatchMetadataDraft.Create(
            BatchMetadataMode.Replace,
            ReplaceSetEdit(
                ReplaceAuthorsEnabled,
                ClearAuthors,
                PreviewableValues(ReplaceAuthorPicker)),
            ReplaceSeriesEdit(),
            ReplaceSetEdit(
                ReplaceTagsEnabled,
                ClearTags,
                PreviewableValues(ReplaceTagPicker)));
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
            PreviewableValues(ReplaceSeriesPicker).SingleOrDefault();
        return selected is null
            ? throw new MetadataDraftValidationException(
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
            ? throw new MetadataDraftValidationException(
                "已启用的替换字段必须选择资料或明确清空。")
            : new(BatchMetadataFieldOperation.Replace, values.ToArray());
    }

    private static BatchMetadataSetEdit SetEdit(
        BatchMetadataFieldOperation operation,
        IReadOnlyList<BatchTaxonomyReference> values) =>
        values.Count == 0
            ? BatchMetadataSetEdit.Unchanged
            : new(operation, values);

    private static BatchTaxonomyReference[] PreviewableValues(
        BatchTaxonomyPickerViewModel picker) =>
        picker.SelectedChoices
            .Select(choice => choice.Value.ExistingId.HasValue
                ? BatchTaxonomyReference.New(
                    choice.Value.Kind,
                    choice.DisplayText)
                : choice.Value)
            .ToArray();

    private void Observe(BatchTaxonomyPickerViewModel picker) =>
        picker.SelectedChoices.CollectionChanged += (_, _) => RaiseDraftChanged();

    private void ObserveOptions(
        IEnumerable<BatchTermCoverageOptionViewModel> options)
    {
        foreach (BatchTermCoverageOptionViewModel option in options)
        {
            option.PropertyChanged += OnRemoveOptionChanged;
        }
    }

    private void UnobserveOptions(
        IEnumerable<BatchTermCoverageOptionViewModel> options)
    {
        foreach (BatchTermCoverageOptionViewModel option in options)
        {
            option.PropertyChanged -= OnRemoveOptionChanged;
        }
    }

    private void OnRemoveOptionChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BatchTermCoverageOptionViewModel.IsSelected))
        {
            RaiseDraftChanged();
        }
    }

    private void RaiseDraftChanged() => DraftChanged?.Invoke(this, EventArgs.Empty);

    private sealed class MetadataDraftValidationException(string message)
        : InvalidOperationException(message);
}
