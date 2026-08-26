using TraceFold.Media.Application.Library;
using Xunit;

namespace TraceFold.Media.Application.Tests.Library;

public sealed class LibraryQueryContractsTests
{
    [Fact]
    public void Query_rejects_missing_authors_with_selected_authors()
    {
        Assert.Throws<ArgumentException>(() => LibraryQuery.Create(
            searchText: null,
            LibraryMediaFilter.All,
            [Guid.NewGuid()],
            seriesId: null,
            tagIds: [],
            missingAuthors: true,
            missingSeries: false,
            missingTags: false,
            LibraryDisplayNameFilter.All,
            LibrarySortField.CreatedAt,
            LibrarySortDirection.Descending));
    }

    [Fact]
    public void Query_trims_search_and_deduplicates_ids_without_reordering()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        LibraryQuery query = LibraryQuery.Create(
            "  杭州  ",
            LibraryMediaFilter.Image,
            [first, second, first],
            seriesId: null,
            tagIds: [],
            missingAuthors: false,
            missingSeries: false,
            missingTags: false,
            LibraryDisplayNameFilter.Customized,
            LibrarySortField.DisplayName,
            LibrarySortDirection.Ascending);

        Assert.Equal("杭州", query.SearchText);
        Assert.Equal([first, second], query.AuthorIds);
    }
}
