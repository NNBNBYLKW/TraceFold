using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Application.Tests.Metadata;

public sealed class TaxonomyManagementQueryServiceTests
{
    [Fact]
    public void Query_trims_search_and_preserves_valid_options()
    {
        TaxonomyManagementQuery query = TaxonomyManagementQuery.Create(
            TaxonomyKind.Author,
            "  Alice  ",
            TaxonomyReferenceFilter.Referenced,
            TaxonomySortField.ReferenceCount,
            TaxonomySortDirection.Descending);

        Assert.Equal(TaxonomyKind.Author, query.Kind);
        Assert.Equal("Alice", query.SearchText);
        Assert.Equal(TaxonomyReferenceFilter.Referenced, query.ReferenceFilter);
        Assert.Equal(TaxonomySortField.ReferenceCount, query.SortField);
        Assert.Equal(TaxonomySortDirection.Descending, query.SortDirection);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Query_normalizes_blank_search_to_null(string? searchText)
    {
        TaxonomyManagementQuery query = TaxonomyManagementQuery.Create(
            TaxonomyKind.Tag,
            searchText,
            TaxonomyReferenceFilter.All,
            TaxonomySortField.DisplayName,
            TaxonomySortDirection.Ascending);

        Assert.Null(query.SearchText);
    }

    [Fact]
    public void Query_rejects_unknown_enum_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(kind: (TaxonomyKind)99));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(referenceFilter: (TaxonomyReferenceFilter)99));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(sortField: (TaxonomySortField)99));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(sortDirection: (TaxonomySortDirection)99));
    }

    [Fact]
    public async Task Service_forwards_the_same_immutable_query()
    {
        var store = new RecordingManagementQueryStore();
        var service = new TaxonomyManagementQueryService(store);
        TaxonomyManagementQuery query = TaxonomyManagementQuery.Create(
            TaxonomyKind.Tag,
            "夜景",
            TaxonomyReferenceFilter.Unused,
            TaxonomySortField.DisplayName,
            TaxonomySortDirection.Ascending);

        IReadOnlyList<TaxonomyManagementTermDto> result = await service.QueryAsync(
            query,
            TestContext.Current.CancellationToken);

        Assert.Same(query, store.LastQuery);
        Assert.Same(store.Result, result);
    }

    [Fact]
    public void Term_sums_inbox_and_library_references()
    {
        var term = new TaxonomyManagementTermDto(
            Guid.NewGuid(),
            TaxonomyKind.Series,
            "系列",
            "系列",
            DateTimeOffset.UtcNow,
            InboxReferenceCount: 2,
            LibraryReferenceCount: 3);

        Assert.Equal(5, term.ReferenceCount);
    }

    private static TaxonomyManagementQuery Create(
        TaxonomyKind kind = TaxonomyKind.Author,
        TaxonomyReferenceFilter referenceFilter = TaxonomyReferenceFilter.All,
        TaxonomySortField sortField = TaxonomySortField.DisplayName,
        TaxonomySortDirection sortDirection = TaxonomySortDirection.Ascending) =>
        TaxonomyManagementQuery.Create(
            kind,
            null,
            referenceFilter,
            sortField,
            sortDirection);

    private sealed class RecordingManagementQueryStore : ITaxonomyManagementQueryStore
    {
        public IReadOnlyList<TaxonomyManagementTermDto> Result { get; } =
            Array.Empty<TaxonomyManagementTermDto>();

        public TaxonomyManagementQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<TaxonomyManagementTermDto>> QueryAsync(
            TaxonomyManagementQuery query,
            CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(Result);
        }
    }
}
