using CommunityToolkit.Mvvm.ComponentModel;
using TraceFold.Media.Application.Library;

namespace TraceFold.Media.Desktop.Pages;

public sealed class LibrarySelectionViewModel : ObservableObject
{
    private readonly List<LibraryCardViewModel> _selected = [];
    private readonly List<LibraryCardViewModel> _queryOrder = [];

    public IReadOnlyList<Guid> SelectedIds =>
        _selected.Select(item => item.Id).ToArray();

    public int SelectedCount => _selected.Count;

    public bool HasSelection => _selected.Count > 0;

    public bool IsSingleSelection => _selected.Count == 1;

    public bool IsBatchSelection => _selected.Count >= 2;

    public LibraryCardViewModel? SingleSelectedItem =>
        _selected.Count == 1 ? _selected[0] : null;

    public void ReplaceFromGrid(
        IEnumerable<LibraryCardViewModel> selected,
        IReadOnlyList<LibraryCardViewModel> queryOrder)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(queryOrder);
        HashSet<Guid> selectedIds = selected.Select(item => item.Id).ToHashSet();
        _queryOrder.Clear();
        _queryOrder.AddRange(queryOrder);
        _selected.Clear();
        _selected.AddRange(_queryOrder.Where(item => selectedIds.Contains(item.Id)));
        NotifyDerivedProperties();
    }

    public IReadOnlyList<LibrarySelectionEntry> Capture()
    {
        if (_queryOrder.GroupBy(item => item.Id).Any(group => group.Count() > 1)
            || _selected.GroupBy(item => item.Id).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException(
                "The library selection contains duplicate asset identities.");
        }

        Dictionary<Guid, int> ordinals = _queryOrder
            .Select((item, ordinal) => (item.Id, Ordinal: ordinal))
            .ToDictionary(item => item.Id, item => item.Ordinal);
        return _selected.Select(item =>
        {
            if (!ordinals.TryGetValue(item.Id, out int ordinal))
            {
                throw new InvalidOperationException(
                    "The selected asset is not present in the current query.");
            }

            return item.SelectionEntry(ordinal);
        }).ToArray();
    }

    public void Clear()
    {
        _selected.Clear();
        NotifyDerivedProperties();
    }

    public void Reconcile(IReadOnlyList<LibraryCardViewModel> queryOrder)
    {
        ArgumentNullException.ThrowIfNull(queryOrder);
        HashSet<Guid> selectedIds = _selected.Select(item => item.Id).ToHashSet();
        _queryOrder.Clear();
        _queryOrder.AddRange(queryOrder);
        _selected.Clear();
        _selected.AddRange(_queryOrder.Where(item => selectedIds.Contains(item.Id)));
        NotifyDerivedProperties();
    }

    private void NotifyDerivedProperties()
    {
        OnPropertyChanged(nameof(SelectedIds));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsSingleSelection));
        OnPropertyChanged(nameof(IsBatchSelection));
        OnPropertyChanged(nameof(SingleSelectedItem));
    }
}
