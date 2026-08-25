using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed partial class BatchTaxonomyPickerViewModel(
    ITaxonomySearchService taxonomy,
    TaxonomyKind kind) : ObservableObject
{
    private readonly List<string> _selectedKeys = [];

    public TaxonomyKind Kind { get; } = kind;

    [ObservableProperty]
    public partial string InputText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool AllowsFreeText { get; private set; } = true;

    public ObservableCollection<TaxonomyTermDto> Suggestions { get; } = [];

    public ObservableCollection<BatchTaxonomyReference> SelectedValues { get; } = [];

    public ObservableCollection<BatchTermCoverageOptionViewModel>
        AvailableExisting { get; } = [];

    public async Task SearchAsync(
        string? query,
        CancellationToken cancellationToken)
    {
        if (!AllowsFreeText)
        {
            return;
        }

        InputText = query ?? string.Empty;
        IReadOnlyList<TaxonomyTermDto> terms = await taxonomy.SearchAsync(
            Kind,
            query,
            cancellationToken);
        Replace(Suggestions, terms.Where(term => term.Kind == Kind));
    }

    public async Task CommitInputAsync(CancellationToken cancellationToken)
    {
        if (!AllowsFreeText)
        {
            throw new InvalidOperationException(
                "Free-text taxonomy values are disabled for this picker.");
        }

        TaxonomyName name = TaxonomyName.Create(InputText);
        IReadOnlyList<TaxonomyTermDto> matches = await taxonomy.SearchAsync(
            Kind,
            name.Display,
            cancellationToken);
        TaxonomyTermDto? existing = matches.FirstOrDefault(term =>
            term.Kind == Kind
            && term.NormalizedName == name.Normalized);
        BatchTaxonomyReference value = existing is null
            ? BatchTaxonomyReference.New(Kind, name.Display)
            : BatchTaxonomyReference.Existing(Kind, existing.Id);
        string key = existing is null
            ? $"name:{name.Normalized}"
            : $"id:{existing.Id:D}";

        if (Kind == TaxonomyKind.Series)
        {
            SelectedValues.Clear();
            _selectedKeys.Clear();
        }

        if (!_selectedKeys.Contains(key, StringComparer.Ordinal))
        {
            SelectedValues.Add(value);
            _selectedKeys.Add(key);
        }

        InputText = string.Empty;
        Suggestions.Clear();
    }

    public void Remove(BatchTaxonomyReference value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int index = SelectedValues.IndexOf(value);
        if (index < 0)
        {
            return;
        }

        SelectedValues.RemoveAt(index);
        _selectedKeys.RemoveAt(index);
    }

    public void SetAvailableExisting(
        IEnumerable<BatchTermCoverageOptionViewModel> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        AllowsFreeText = false;
        ResetSelection();
        Replace(AvailableExisting, options);
    }

    public void ResetSelection()
    {
        InputText = string.Empty;
        Suggestions.Clear();
        SelectedValues.Clear();
        _selectedKeys.Clear();
        foreach (BatchTermCoverageOptionViewModel option in AvailableExisting)
        {
            option.IsSelected = false;
        }
    }

    private static void Replace<T>(
        ObservableCollection<T> target,
        IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
        {
            target.Add(value);
        }
    }
}
