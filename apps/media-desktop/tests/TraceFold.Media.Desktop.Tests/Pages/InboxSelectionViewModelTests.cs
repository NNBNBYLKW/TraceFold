using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Desktop.Pages;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

public sealed class InboxSelectionViewModelTests
{
    [Fact]
    public void Replace_uses_query_order_even_when_grid_reports_click_order()
    {
        InboxCardViewModel a = Card("a.jpg", version: 1);
        InboxCardViewModel b = Card("b.jpg", version: 2);
        InboxCardViewModel c = Card("c.jpg", version: 3);
        var selection = new InboxSelectionViewModel();

        selection.ReplaceFromGrid([c, a], [a, b, c]);

        Assert.Equal([a.Id, c.Id], selection.SelectedIds);
        Assert.Equal([0, 2], selection.Capture().Select(item => item.Ordinal));
        Assert.Equal(2, selection.SelectedCount);
        Assert.True(selection.IsBatchSelection);
        Assert.False(selection.IsSingleSelection);
        Assert.Null(selection.SingleSelectedItem);
    }

    [Fact]
    public void Reconcile_intersects_ids_and_refreshes_versions_from_new_cards()
    {
        InboxCardViewModel oldA = Card("a.jpg", version: 1);
        InboxCardViewModel oldB = Card("b.jpg", version: 2);
        var selection = new InboxSelectionViewModel();
        selection.ReplaceFromGrid([oldA, oldB], [oldA, oldB]);
        InboxCardViewModel newA = Card(oldA.Id, "a.jpg", version: 4);

        selection.Reconcile([newA]);

        InboxSelectionEntry remaining = Assert.Single(selection.Capture());
        Assert.Equal(oldA.Id, remaining.InboxItemId);
        Assert.Equal(4, remaining.ExpectedMetadataVersion);
        Assert.True(selection.IsSingleSelection);
        Assert.Same(newA, selection.SingleSelectedItem);
    }

    [Fact]
    public void Retain_keeps_only_unfinished_ids_in_query_order()
    {
        InboxCardViewModel a = Card("a.jpg");
        InboxCardViewModel b = Card("b.jpg");
        InboxCardViewModel c = Card("c.jpg");
        InboxSelectionViewModel selection = Selected(a, b, c);

        selection.Retain([c.Id, b.Id]);

        Assert.Equal([b.Id, c.Id], selection.SelectedIds);
    }

    [Fact]
    public void Clear_resets_all_derived_selection_state()
    {
        InboxCardViewModel item = Card("a.jpg");
        InboxSelectionViewModel selection = Selected(item);
        var changed = new List<string?>();
        selection.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        selection.Clear();

        Assert.Empty(selection.SelectedIds);
        Assert.Equal(0, selection.SelectedCount);
        Assert.False(selection.IsSingleSelection);
        Assert.False(selection.IsBatchSelection);
        Assert.Null(selection.SingleSelectedItem);
        Assert.Contains(nameof(selection.SelectedCount), changed);
        Assert.Contains(nameof(selection.SingleSelectedItem), changed);
    }

    [Fact]
    public void Capture_rejects_duplicate_ids_in_query_order()
    {
        InboxCardViewModel first = Card("a.jpg");
        InboxCardViewModel duplicate = Card(first.Id, "a-copy.jpg", version: 2);
        var selection = new InboxSelectionViewModel();
        selection.ReplaceFromGrid([first], [first, duplicate]);

        Assert.Throws<InvalidOperationException>(() => selection.Capture());
    }

    private static InboxSelectionViewModel Selected(
        params InboxCardViewModel[] cards)
    {
        var selection = new InboxSelectionViewModel();
        selection.ReplaceFromGrid(cards, cards);
        return selection;
    }

    private static InboxCardViewModel Card(
        string filename,
        long version = 0) =>
        Card(Guid.NewGuid(), filename, version);

    private static InboxCardViewModel Card(
        Guid id,
        string filename,
        long version) =>
        new(new InboxCardDto(
            id,
            filename,
            filename,
            MediaKind.Image,
            InboxItemState.ReadyToTag,
            "待入库",
            true,
            MetadataVersion: version,
            AllowedActions: InboxAction.EditMetadata | InboxAction.Ingest));
}
