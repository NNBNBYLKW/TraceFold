using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Desktop.Pages;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

public sealed class LibrarySelectionViewModelTests
{
    [Fact]
    public void Selection_reports_has_selection_and_notifies()
    {
        LibraryCardViewModel card = Card("photo.jpg", 4);
        var selection = new LibrarySelectionViewModel();
        List<string?> changed = [];
        selection.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        Assert.False(selection.HasSelection);

        selection.ReplaceFromGrid([card], [card]);

        Assert.True(selection.HasSelection);
        Assert.Contains(nameof(LibrarySelectionViewModel.HasSelection), changed);

        selection.Clear();

        Assert.False(selection.HasSelection);
    }

    [Fact]
    public void Capture_uses_current_query_order_after_sort()
    {
        LibraryCardViewModel first = Card("first.jpg", version: 4);
        LibraryCardViewModel second = Card("second.jpg", version: 7);
        var selection = new LibrarySelectionViewModel();
        selection.ReplaceFromGrid([first, second], [second, first]);

        IReadOnlyList<LibrarySelectionEntry> snapshot = selection.Capture();

        Assert.Equal(second.Id, snapshot[0].AssetId);
        Assert.Equal(0, snapshot[0].Ordinal);
        Assert.Equal(7, snapshot[0].ExpectedMetadataVersion);
    }

    [Fact]
    public void Reconcile_keeps_ids_in_new_query_order_and_refreshes_versions()
    {
        LibraryCardViewModel oldFirst = Card("first.jpg", version: 1);
        LibraryCardViewModel oldSecond = Card("second.jpg", version: 2);
        var selection = new LibrarySelectionViewModel();
        selection.ReplaceFromGrid([oldFirst, oldSecond], [oldFirst, oldSecond]);
        LibraryCardViewModel refreshedSecond = Card(
            oldSecond.Id,
            "second.jpg",
            version: 9);

        selection.Reconcile([refreshedSecond]);

        LibrarySelectionEntry entry = Assert.Single(selection.Capture());
        Assert.Equal(oldSecond.Id, entry.AssetId);
        Assert.Equal(9, entry.ExpectedMetadataVersion);
        Assert.Same(refreshedSecond, selection.SingleSelectedItem);
    }

    private static LibraryCardViewModel Card(
        string filename,
        long version) =>
        Card(Guid.NewGuid(), filename, version);

    private static LibraryCardViewModel Card(
        Guid id,
        string filename,
        long version) =>
        new(new LibraryAssetDto(
            id,
            filename,
            filename,
            MediaKind.Image,
            DateTimeOffset.Parse("2026-08-26T00:00:00Z"),
            version,
            false));
}
