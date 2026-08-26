using CommunityToolkit.Mvvm.ComponentModel;
using TraceFold.Media.Application.Intake;

namespace TraceFold.Media.Desktop.Pages;

public sealed class InboxSelectionViewModel : ObservableObject
{
    private readonly List<InboxCardViewModel> _selected = [];
    private readonly List<InboxCardViewModel> _queryOrder = [];

    public IReadOnlyList<Guid> SelectedIds =>
        _selected.Select(item => item.Id).ToArray();

    public int SelectedCount => _selected.Count;

    public bool IsSingleSelection => _selected.Count == 1;

    public bool IsBatchSelection => _selected.Count >= 2;

    public InboxCardViewModel? SingleSelectedItem =>
        _selected.Count == 1 ? _selected[0] : null;

    public void ReplaceFromGrid(
        IEnumerable<InboxCardViewModel> selected,
        IReadOnlyList<InboxCardViewModel> queryOrder)
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

    public IReadOnlyList<InboxSelectionEntry> Capture()
    {
        if (_queryOrder.GroupBy(item => item.Id).Any(group => group.Count() > 1)
            || _selected.GroupBy(item => item.Id).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException(
                "The inbox selection contains duplicate item identities.");
        }

        Dictionary<Guid, int> ordinals = _queryOrder
            .Select((item, ordinal) => (item.Id, Ordinal: ordinal))
            .ToDictionary(item => item.Id, item => item.Ordinal);
        return _selected.Select(item =>
        {
            if (!ordinals.TryGetValue(item.Id, out int ordinal))
            {
                throw new InvalidOperationException(
                    "The selected item is not present in the current query.");
            }

            return item.SelectionEntry(ordinal);
        }).ToArray();
    }

    public void Clear()
    {
        _selected.Clear();
        NotifyDerivedProperties();
    }

    public void Reconcile(IReadOnlyList<InboxCardViewModel> queryOrder)
    {
        ArgumentNullException.ThrowIfNull(queryOrder);
        HashSet<Guid> selectedIds = _selected.Select(item => item.Id).ToHashSet();
        _queryOrder.Clear();
        _queryOrder.AddRange(queryOrder);
        _selected.Clear();
        _selected.AddRange(_queryOrder.Where(item => selectedIds.Contains(item.Id)));
        NotifyDerivedProperties();
    }

    public void Retain(IReadOnlyCollection<Guid> inboxItemIds)
    {
        ArgumentNullException.ThrowIfNull(inboxItemIds);
        HashSet<Guid> retained = inboxItemIds.ToHashSet();
        HashSet<Guid> selectedIds = _selected.Select(item => item.Id).ToHashSet();
        _selected.Clear();
        _selected.AddRange(_queryOrder.Where(item =>
            retained.Contains(item.Id) && selectedIds.Contains(item.Id)));
        NotifyDerivedProperties();
    }

    private void NotifyDerivedProperties()
    {
        OnPropertyChanged(nameof(SelectedIds));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(IsSingleSelection));
        OnPropertyChanged(nameof(IsBatchSelection));
        OnPropertyChanged(nameof(SingleSelectedItem));
    }
}
