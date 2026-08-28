using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed record BatchTaxonomyChoiceViewModel(
    BatchTaxonomyReference Value,
    string DisplayText,
    TaxonomyTermDto? ExistingTerm,
    string RemoveAutomationId);

public sealed partial class BatchTaxonomyPickerViewModel(
    ITaxonomySearchService taxonomy,
    TaxonomyKind kind,
    string? automationScope = null) : ObservableObject
{
    private readonly List<string> _selectedKeys = [];
    private readonly string _automationScope = string.IsNullOrWhiteSpace(
        automationScope)
        ? $"BatchTaxonomy{kind}"
        : automationScope.Trim();

    public TaxonomyKind Kind { get; } = kind;

    [ObservableProperty]
    public partial string InputText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool AllowsFreeText { get; private set; } = true;

    public ObservableCollection<TaxonomyTermDto> Suggestions { get; } = [];

    public ObservableCollection<BatchTaxonomyReference> SelectedValues { get; } = [];

    public ObservableCollection<BatchTaxonomyChoiceViewModel> SelectedChoices { get; } = [];

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
            SelectedChoices.Clear();
            _selectedKeys.Clear();
        }

        if (!_selectedKeys.Contains(key, StringComparer.Ordinal))
        {
            SelectedValues.Add(value);
            SelectedChoices.Add(new(
                value,
                existing?.DisplayName ?? name.Display,
                existing,
                BuildRemoveAutomationId(existing, name)));
            _selectedKeys.Add(key);
        }

        InputText = string.Empty;
        Suggestions.Clear();
    }

    private string BuildRemoveAutomationId(
        TaxonomyTermDto? existing,
        TaxonomyName name)
    {
        string identity;
        if (existing is not null)
        {
            identity = existing.Id.ToString("N");
        }
        else
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{Kind}:{name.Normalized}"));
            identity = Convert.ToHexString(hash.AsSpan(0, 8));
        }

        return $"{_automationScope}-Remove-{identity}";
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
        SelectedChoices.RemoveAt(index);
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
        SelectedChoices.Clear();
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
