using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class LibraryQueryPersistenceTests
{
    [Fact]
    public async Task Authors_and_tags_require_every_selected_term()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        LibraryFixture fixture = await database.SeedLibraryFilterFixtureAsync();
        var store = new EfLibraryQueryStore(database.CreateContextFactory());

        LibraryQuery query = LibraryQuery.Create(
            null,
            LibraryMediaFilter.All,
            [fixture.AuthorA, fixture.AuthorB],
            null,
            [fixture.TagA, fixture.TagB],
            false,
            false,
            false,
            LibraryDisplayNameFilter.All,
            LibrarySortField.CreatedAt,
            LibrarySortDirection.Descending);

        LibraryAssetDto asset = Assert.Single(await store.SearchAsync(
            query,
            TestContext.Current.CancellationToken));
        Assert.Equal(fixture.AssetWithEveryTerm, asset.Id);
        Assert.Equal(7, asset.MetadataVersion);
    }

    [Fact]
    public async Task Explicit_name_equal_to_filename_is_still_customized()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid id = await database.SeedFormalAssetAsync(
            "same.jpg",
            displayName: "same.jpg",
            metadataVersion: 3);
        var store = new EfLibraryQueryStore(database.CreateContextFactory());

        IReadOnlyList<LibraryAssetDto> result = await store.SearchAsync(
            CreateQuery(
                displayName: LibraryDisplayNameFilter.Customized,
                sortField: LibrarySortField.DisplayName),
            TestContext.Current.CancellationToken);

        Assert.Contains(result, item =>
            item.Id == id
            && item.HasCustomDisplayName
            && item.MetadataVersion == 3);
    }

    [Fact]
    public async Task Missing_filters_use_not_exists()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid author = await database.SeedTaxonomyAsync(TaxonomyKind.Author, "Author");
        Guid series = await database.SeedTaxonomyAsync(TaxonomyKind.Series, "Series");
        Guid tag = await database.SeedTaxonomyAsync(TaxonomyKind.Tag, "Tag");
        Guid missingEverywhere = await database.SeedFormalAssetAsync(
            "missing-everywhere.jpg", null, 0);
        await database.SeedFormalAssetAsync(
            "has-author.jpg", null, 0, authorIds: [author]);
        await database.SeedFormalAssetAsync(
            "has-series.jpg", null, 0, seriesId: series);
        await database.SeedFormalAssetAsync(
            "has-tag.jpg", null, 0, tagIds: [tag]);
        var store = new EfLibraryQueryStore(database.CreateContextFactory());

        LibraryQuery query = LibraryQuery.Create(
            null,
            LibraryMediaFilter.All,
            [],
            null,
            [],
            true,
            true,
            true,
            LibraryDisplayNameFilter.All,
            LibrarySortField.CreatedAt,
            LibrarySortDirection.Descending);

        LibraryAssetDto result = Assert.Single(await store.SearchAsync(
            query,
            TestContext.Current.CancellationToken));
        Assert.Equal(missingEverywhere, result.Id);
    }

    [Fact]
    public async Task Search_escapes_percent_and_underscore()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid literal = await database.SeedFormalAssetAsync(
            "literal%_name.jpg", null, 0);
        await database.SeedFormalAssetAsync("literalXXname.jpg", null, 0);
        var store = new EfLibraryQueryStore(database.CreateContextFactory());

        IReadOnlyList<LibraryAssetDto> result = await store.SearchAsync(
            CreateQuery(searchText: "%_"),
            TestContext.Current.CancellationToken);

        Assert.Equal(literal, Assert.Single(result).Id);
    }

    [Theory]
    [InlineData(LibrarySortField.CreatedAt)]
    [InlineData(LibrarySortField.DisplayName)]
    [InlineData(LibrarySortField.ActualFilename)]
    [InlineData(LibrarySortField.MediaKind)]
    public async Task All_four_sort_fields_have_stable_id_tiebreaker(
        LibrarySortField sortField)
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid first = await database.SeedFormalAssetAsync("same.jpg", "Same", 0);
        Guid second = await database.SeedFormalAssetAsync("same.jpg", "Same", 0);
        var store = new EfLibraryQueryStore(database.CreateContextFactory());

        IReadOnlyList<LibraryAssetDto> result = await store.SearchAsync(
            CreateQuery(sortField: sortField),
            TestContext.Current.CancellationToken);

        Guid[] expected = [first, second];
        Array.Sort(expected, (left, right) => StringComparer.Ordinal.Compare(
            left.ToString(),
            right.ToString()));
        Assert.Equal(expected, result.Select(item => item.Id));
    }

    [Fact]
    public async Task Six_hundred_assets_return_in_deterministic_order()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid[] inserted = await database.SeedFormalAssetsAsync(600);
        var store = new EfLibraryQueryStore(database.CreateContextFactory());
        LibraryQuery query = CreateQuery(sortField: LibrarySortField.CreatedAt);

        IReadOnlyList<LibraryAssetDto> first = await store.SearchAsync(
            query,
            TestContext.Current.CancellationToken);
        IReadOnlyList<LibraryAssetDto> second = await store.SearchAsync(
            query,
            TestContext.Current.CancellationToken);

        Assert.Equal(600, first.Count);
        Assert.Equal(inserted.Reverse(), first.Select(item => item.Id));
        Assert.Equal(first.Select(item => item.Id), second.Select(item => item.Id));
    }

    [Fact]
    public async Task Sort_fields_honor_requested_direction()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid older = await database.SeedFormalAssetAsync(
            "z-file.jpg", "Alpha", 0, MediaKind.Image);
        Guid newer = await database.SeedFormalAssetAsync(
            "a-file.mp4", "Zulu", 0, MediaKind.Video);
        await database.ExecuteSqlAsync(
            $"UPDATE media_assets SET created_at_utc = '2026-01-01T00:00:00+00:00' WHERE id = '{older}'",
            $"UPDATE media_assets SET created_at_utc = '2026-02-01T00:00:00+00:00' WHERE id = '{newer}'");
        var store = new EfLibraryQueryStore(database.CreateContextFactory());

        IReadOnlyList<LibraryAssetDto> createdDescending = await store.SearchAsync(
            CreateQuery(
                sortField: LibrarySortField.CreatedAt,
                sortDirection: LibrarySortDirection.Descending),
            TestContext.Current.CancellationToken);
        IReadOnlyList<LibraryAssetDto> displayAscending = await store.SearchAsync(
            CreateQuery(sortField: LibrarySortField.DisplayName),
            TestContext.Current.CancellationToken);
        IReadOnlyList<LibraryAssetDto> filenameDescending = await store.SearchAsync(
            CreateQuery(
                sortField: LibrarySortField.ActualFilename,
                sortDirection: LibrarySortDirection.Descending),
            TestContext.Current.CancellationToken);
        IReadOnlyList<LibraryAssetDto> kindAscending = await store.SearchAsync(
            CreateQuery(sortField: LibrarySortField.MediaKind),
            TestContext.Current.CancellationToken);

        Assert.Equal(newer, createdDescending[0].Id);
        Assert.Equal(older, displayAscending[0].Id);
        Assert.Equal(older, filenameDescending[0].Id);
        Assert.Equal(older, kindAscending[0].Id);
    }

    [Fact]
    public async Task Single_value_filters_intersect_with_search()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid seriesA = await database.SeedTaxonomyAsync(TaxonomyKind.Series, "Series A");
        Guid seriesB = await database.SeedTaxonomyAsync(TaxonomyKind.Series, "Series B");
        Guid target = await database.SeedFormalAssetAsync(
            "target.mp4", "Needle", 0, MediaKind.Video, seriesId: seriesA);
        await database.SeedFormalAssetAsync(
            "image.jpg", "Needle", 0, MediaKind.Image, seriesId: seriesA);
        await database.SeedFormalAssetAsync(
            "other.mp4", "Needle", 0, MediaKind.Video, seriesId: seriesB);
        var store = new EfLibraryQueryStore(database.CreateContextFactory());

        LibraryQuery query = LibraryQuery.Create(
            "needle",
            LibraryMediaFilter.Video,
            [],
            seriesA,
            [],
            false,
            false,
            false,
            LibraryDisplayNameFilter.Customized,
            LibrarySortField.DisplayName,
            LibrarySortDirection.Ascending);

        LibraryAssetDto result = Assert.Single(await store.SearchAsync(
            query,
            TestContext.Current.CancellationToken));
        Assert.Equal(target, result.Id);
    }

    private static LibraryQuery CreateQuery(
        string? searchText = null,
        LibraryDisplayNameFilter displayName = LibraryDisplayNameFilter.All,
        LibrarySortField sortField = LibrarySortField.CreatedAt,
        LibrarySortDirection sortDirection = LibrarySortDirection.Ascending) =>
        LibraryQuery.Create(
            searchText,
            LibraryMediaFilter.All,
            [],
            null,
            [],
            false,
            false,
            false,
            displayName,
            sortField,
            sortDirection);
}
