using CommunityToolkit.Mvvm.ComponentModel;
using TraceFold.Media.Application.Metadata;

namespace TraceFold.Media.Desktop.Pages;

public sealed class TaxonomySelectionViewModel : ObservableObject
{
    private readonly List<TaxonomyManagementTermDto> _selected = [];
    private readonly List<TaxonomyManagementTermDto> _queryOrder = [];

    public IReadOnlyList<Guid> SelectedIds =>
        _selected.Select(term => term.Id).ToArray();

    public int SelectedCount => _selected.Count;

    public bool HasSelection => _selected.Count > 0;

    public bool IsSingleSelection => _selected.Count == 1;

    public bool IsMultipleSelection => _selected.Count >= 2;

    public TaxonomyManagementTermDto? SingleSelectedTerm =>
        _selected.Count == 1 ? _selected[0] : null;

    public IReadOnlyList<TaxonomyManagementTermDto> DeleteBlockers =>
        _selected.Where(term => term.ReferenceCount > 0).ToArray();

    public void ReplaceFromList(
        IEnumerable<TaxonomyManagementTermDto> selected,
        IReadOnlyList<TaxonomyManagementTermDto> queryOrder)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(queryOrder);
        TaxonomyManagementTermDto[] selectedTerms = selected.ToArray();
        ValidateUnique(queryOrder, "query");
        ValidateUnique(selectedTerms, "selection");

        HashSet<Guid> visibleIds = queryOrder.Select(term => term.Id).ToHashSet();
        if (selectedTerms.Any(term => !visibleIds.Contains(term.Id)))
        {
            throw new InvalidOperationException(
                "The selected taxonomy term is not present in the current query.");
        }

        HashSet<Guid> selectedIds = selectedTerms.Select(term => term.Id).ToHashSet();
        _queryOrder.Clear();
        _queryOrder.AddRange(queryOrder);
        _selected.Clear();
        _selected.AddRange(_queryOrder.Where(term => selectedIds.Contains(term.Id)));
        NotifyDerivedProperties();
    }

    public IReadOnlyList<TaxonomyManagementTermDto> Capture()
    {
        ValidateUnique(_queryOrder, "query");
        ValidateUnique(_selected, "selection");
        HashSet<Guid> visibleIds = _queryOrder.Select(term => term.Id).ToHashSet();
        if (_selected.Any(term => !visibleIds.Contains(term.Id)))
        {
            throw new InvalidOperationException(
                "The selected taxonomy term is not present in the current query.");
        }

        return _selected.ToArray();
    }

    public void Clear()
    {
        _selected.Clear();
        NotifyDerivedProperties();
    }

    public void Reconcile(IReadOnlyList<TaxonomyManagementTermDto> queryOrder)
    {
        ArgumentNullException.ThrowIfNull(queryOrder);
        ValidateUnique(queryOrder, "query");
        HashSet<Guid> selectedIds = _selected.Select(term => term.Id).ToHashSet();
        _queryOrder.Clear();
        _queryOrder.AddRange(queryOrder);
        _selected.Clear();
        _selected.AddRange(_queryOrder.Where(term => selectedIds.Contains(term.Id)));
        NotifyDerivedProperties();
    }

    private static void ValidateUnique(
        IEnumerable<TaxonomyManagementTermDto> terms,
        string source)
    {
        if (terms.GroupBy(term => term.Id).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException(
                $"The taxonomy {source} contains duplicate term identities.");
        }
    }

    private void NotifyDerivedProperties()
    {
        OnPropertyChanged(nameof(SelectedIds));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsSingleSelection));
        OnPropertyChanged(nameof(IsMultipleSelection));
        OnPropertyChanged(nameof(SingleSelectedTerm));
        OnPropertyChanged(nameof(DeleteBlockers));
    }
}
