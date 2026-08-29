using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class TaxonomyManagementQueryPersistenceTests
{
    [Fact]
    public async Task Query_splits_inbox_and_library_counts_and_filters_unused()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyManagementFixture fixture =
            await database.SeedTaxonomyManagementFixtureAsync();
        var store = new EfTaxonomyManagementQueryStore(
            database.CreateContextFactory());

        IReadOnlyList<TaxonomyManagementTermDto> referenced = await store.QueryAsync(
            Query(
                TaxonomyKind.Author,
                TaxonomyReferenceFilter.Referenced,
                TaxonomySortField.ReferenceCount,
                TaxonomySortDirection.Descending),
            TestContext.Current.CancellationToken);
        IReadOnlyList<TaxonomyManagementTermDto> unused = await store.QueryAsync(
            Query(TaxonomyKind.Author, TaxonomyReferenceFilter.Unused),
            TestContext.Current.CancellationToken);

        TaxonomyManagementTermDto shared = Assert.Single(
            referenced,
            term => term.Id == fixture.SharedAuthorId);
        Assert.Equal(1, shared.InboxReferenceCount);
        Assert.Equal(1, shared.LibraryReferenceCount);
        Assert.Equal(2, shared.ReferenceCount);
        Assert.Equal(fixture.UnusedAuthorId, Assert.Single(unused).Id);
    }

    [Theory]
    [InlineData("%", "100% Real")]
    [InlineData("_", "Under_score")]
    [InlineData("\\", "Back\\slash")]
    public async Task Query_treats_like_metacharacters_as_literal_text(
        string searchText,
        string expectedName)
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        await database.SeedTaxonomyManagementFixtureAsync();
        var store = new EfTaxonomyManagementQueryStore(
            database.CreateContextFactory());

        IReadOnlyList<TaxonomyManagementTermDto> result = await store.QueryAsync(
            TaxonomyManagementQuery.Create(
                TaxonomyKind.Tag,
                searchText,
                TaxonomyReferenceFilter.All,
                TaxonomySortField.DisplayName,
                TaxonomySortDirection.Ascending),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedName, Assert.Single(result).DisplayName);
    }

    [Fact]
    public async Task Query_counts_author_series_and_tag_reference_sources()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyManagementFixture fixture =
            await database.SeedTaxonomyManagementFixtureAsync();
        var store = new EfTaxonomyManagementQueryStore(
            database.CreateContextFactory());

        TaxonomyManagementTermDto author = Assert.Single(
            await store.QueryAsync(
                Search(TaxonomyKind.Author, "Shared Author"),
                TestContext.Current.CancellationToken));
        TaxonomyManagementTermDto series = Assert.Single(
            await store.QueryAsync(
                Search(TaxonomyKind.Series, "Shared Series"),
                TestContext.Current.CancellationToken));
        TaxonomyManagementTermDto tag = Assert.Single(
            await store.QueryAsync(
                Search(TaxonomyKind.Tag, "Shared Tag"),
                TestContext.Current.CancellationToken));

        Assert.Equal(fixture.SharedAuthorId, author.Id);
        Assert.Equal((1, 1), (author.InboxReferenceCount, author.LibraryReferenceCount));
        Assert.Equal(fixture.SharedSeriesId, series.Id);
        Assert.Equal((1, 1), (series.InboxReferenceCount, series.LibraryReferenceCount));
        Assert.Equal(fixture.SharedTagId, tag.Id);
        Assert.Equal((1, 1), (tag.InboxReferenceCount, tag.LibraryReferenceCount));
    }

    [Fact]
    public async Task Reference_sort_uses_name_then_id_as_stable_tie_breakers()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        await database.SeedTaxonomyManagementFixtureAsync();
        var store = new EfTaxonomyManagementQueryStore(
            database.CreateContextFactory());

        IReadOnlyList<TaxonomyManagementTermDto> result = await store.QueryAsync(
            Query(
                TaxonomyKind.Author,
                TaxonomyReferenceFilter.All,
                TaxonomySortField.ReferenceCount,
                TaxonomySortDirection.Descending),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["Shared Author", "Alpha Tie", "Beta Tie", "Formal Only", "Unused Author"],
            result.Select(term => term.DisplayName));
    }

    [Fact]
    public async Task Name_sort_honors_descending_direction()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        await database.SeedTaxonomyManagementFixtureAsync();
        var store = new EfTaxonomyManagementQueryStore(
            database.CreateContextFactory());

        IReadOnlyList<TaxonomyManagementTermDto> result = await store.QueryAsync(
            Query(
                TaxonomyKind.Author,
                TaxonomyReferenceFilter.All,
                TaxonomySortField.DisplayName,
                TaxonomySortDirection.Descending),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["Unused Author", "Shared Author", "Formal Only", "Beta Tie", "Alpha Tie"],
            result.Select(term => term.DisplayName));
    }

    private static async Task<TestMediaDatabase> CreateDatabaseAsync()
    {
        TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        return database;
    }

    private static TaxonomyManagementQuery Query(
        TaxonomyKind kind,
        TaxonomyReferenceFilter referenceFilter,
        TaxonomySortField sortField = TaxonomySortField.DisplayName,
        TaxonomySortDirection sortDirection = TaxonomySortDirection.Ascending) =>
        TaxonomyManagementQuery.Create(
            kind,
            null,
            referenceFilter,
            sortField,
            sortDirection);

    private static TaxonomyManagementQuery Search(
        TaxonomyKind kind,
        string searchText) =>
        TaxonomyManagementQuery.Create(
            kind,
            searchText,
            TaxonomyReferenceFilter.All,
            TaxonomySortField.DisplayName,
            TaxonomySortDirection.Ascending);
}
