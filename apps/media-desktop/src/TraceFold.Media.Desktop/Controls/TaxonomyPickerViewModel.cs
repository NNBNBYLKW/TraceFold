using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Desktop.Controls;

public sealed partial class TaxonomyPickerViewModel(
    ITaxonomyService taxonomyService) : ObservableObject
{
    [ObservableProperty]
    public partial TaxonomyKind Kind { get; set; }

    [ObservableProperty]
    public partial string QueryText { get; set; } = string.Empty;

    public ObservableCollection<TaxonomyTermDto> Suggestions { get; } = [];

    public ObservableCollection<TaxonomyTermDto> SelectedTerms { get; } = [];

    public event EventHandler? SelectionChanged;

    public async Task SearchAsync(
        string? query,
        CancellationToken cancellationToken)
    {
        QueryText = query ?? string.Empty;
        IReadOnlyList<TaxonomyTermDto> terms = await taxonomyService.SearchAsync(
            Kind,
            query,
            cancellationToken);
        Replace(Suggestions, terms);
    }

    public async Task CommitAsync(
        string value,
        CancellationToken cancellationToken)
    {
        TaxonomyName name = TaxonomyName.Create(value);
        TaxonomyTermDto? term = Suggestions
            .Concat(SelectedTerms)
            .FirstOrDefault(candidate =>
                candidate.Kind == Kind
                && candidate.NormalizedName == name.Normalized);
        term ??= await taxonomyService.CreateAsync(
            Kind,
            name.Display,
            cancellationToken);

        bool changed;
        if (Kind == TaxonomyKind.Series)
        {
            changed = SelectedTerms.Count != 1 || SelectedTerms[0].Id != term.Id;
            if (changed)
            {
                SelectedTerms.Clear();
                SelectedTerms.Add(term);
            }
        }
        else if (SelectedTerms.All(selected => selected.Id != term.Id))
        {
            SelectedTerms.Add(term);
            changed = true;
        }
        else
        {
            changed = false;
        }

        QueryText = string.Empty;
        Suggestions.Clear();
        if (changed)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Remove(TaxonomyTermDto term)
    {
        ArgumentNullException.ThrowIfNull(term);
        TaxonomyTermDto? selected = SelectedTerms.FirstOrDefault(
            item => item.Id == term.Id);
        if (selected is not null)
        {
            SelectedTerms.Remove(selected);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetSelectedTerms(IEnumerable<TaxonomyTermDto> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        IEnumerable<TaxonomyTermDto> selected = terms
            .Where(term => term.Kind == Kind)
            .DistinctBy(term => term.Id);
        if (Kind == TaxonomyKind.Series)
        {
            selected = selected.Take(1);
        }

        Replace(SelectedTerms, selected);
    }

    private static void Replace(
        ObservableCollection<TaxonomyTermDto> target,
        IEnumerable<TaxonomyTermDto> values)
    {
        target.Clear();
        foreach (TaxonomyTermDto value in values)
        {
            target.Add(value);
        }
    }
}
