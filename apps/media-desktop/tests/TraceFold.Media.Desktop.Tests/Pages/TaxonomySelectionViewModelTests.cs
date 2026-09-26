using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Desktop.Pages;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

public sealed class TaxonomySelectionViewModelTests
{
    [Fact]
    public void Taxonomy_selection_reports_has_selection()
    {
        TaxonomyManagementTermDto term = Term("作者甲");
        var selection = new TaxonomySelectionViewModel();
        List<string?> changed = [];
        selection.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        Assert.False(selection.HasSelection);

        selection.ReplaceFromList([term], [term]);

        Assert.True(selection.HasSelection);
        Assert.Contains(nameof(TaxonomySelectionViewModel.HasSelection), changed);

        selection.Clear();

        Assert.False(selection.HasSelection);
    }

    [Fact]
    public void Reconcile_preserves_ids_in_new_query_order()
    {
        TaxonomyManagementTermDto first = Term("B");
        TaxonomyManagementTermDto second = Term("A");
        var selection = new TaxonomySelectionViewModel();
        selection.ReplaceFromList([first, second], [first, second]);

        selection.Reconcile([second, first]);

        Assert.Equal([second.Id, first.Id], selection.SelectedIds);
        Assert.Equal([second, first], selection.Capture());
    }

    [Fact]
    public void Replace_rejects_duplicate_query_or_selection_identities()
    {
        TaxonomyManagementTermDto term = Term("A");
        var selection = new TaxonomySelectionViewModel();

        Assert.Throws<InvalidOperationException>(() =>
            selection.ReplaceFromList([term], [term, term]));
        Assert.Throws<InvalidOperationException>(() =>
            selection.ReplaceFromList([term, term], [term]));
    }

    [Fact]
    public void Replace_rejects_an_item_outside_the_current_query()
    {
        TaxonomyManagementTermDto visible = Term("Visible");
        TaxonomyManagementTermDto stale = Term("Stale");
        var selection = new TaxonomySelectionViewModel();

        Assert.Throws<InvalidOperationException>(() =>
            selection.ReplaceFromList([stale], [visible]));
    }

    [Fact]
    public void Clear_resets_single_and_multiple_derived_state()
    {
        TaxonomyManagementTermDto first = Term("A");
        TaxonomyManagementTermDto second = Term("B");
        var selection = new TaxonomySelectionViewModel();
        selection.ReplaceFromList([first], [first, second]);
        Assert.True(selection.IsSingleSelection);
        Assert.Same(first, selection.SingleSelectedTerm);

        selection.ReplaceFromList([first, second], [first, second]);
        Assert.True(selection.IsMultipleSelection);

        selection.Clear();

        Assert.Empty(selection.SelectedIds);
        Assert.Equal(0, selection.SelectedCount);
        Assert.False(selection.IsSingleSelection);
        Assert.False(selection.IsMultipleSelection);
        Assert.Null(selection.SingleSelectedTerm);
    }

    [Fact]
    public void Delete_blockers_include_every_selected_referenced_term_in_query_order()
    {
        TaxonomyManagementTermDto first = Term("A", inboxReferences: 2);
        TaxonomyManagementTermDto unused = Term("B");
        TaxonomyManagementTermDto second = Term("C", libraryReferences: 3);
        var selection = new TaxonomySelectionViewModel();

        selection.ReplaceFromList(
            [second, unused, first],
            [first, unused, second]);

        Assert.Equal([first, second], selection.DeleteBlockers);
    }

    private static TaxonomyManagementTermDto Term(
        string displayName,
        int inboxReferences = 0,
        int libraryReferences = 0) =>
        new(
            Guid.NewGuid(),
            TaxonomyKind.Author,
            displayName,
            displayName.ToUpperInvariant(),
            DateTimeOffset.Parse("2026-08-29T00:00:00Z"),
            inboxReferences,
            libraryReferences);
}
