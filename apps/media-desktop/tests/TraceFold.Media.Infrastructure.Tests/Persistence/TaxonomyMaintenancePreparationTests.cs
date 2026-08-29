using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class TaxonomyMaintenancePreparationTests
{
    [Fact]
    public async Task Prepare_merge_loads_distinct_inbox_and_library_owners_in_ordinal_order()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture fixture =
            await database.SeedTaxonomyMaintenanceFixtureAsync();
        var store = new EfTaxonomyMaintenanceStore(
            database.CreateContextFactory(),
            TimeProvider.System);

        TaxonomyMergePrepareResult result = await store.PrepareMergeAsync(
            fixture.AuthorMergeSelection,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        TaxonomyMergePreparation preparation = result.Preparation!;
        Assert.Equal(2, preparation.Sources.Count);
        Assert.Equal(2, preparation.Owners.Count);
        TaxonomyOwnerSnapshot inbox = Assert.Single(preparation.Owners, owner =>
            owner.Owner.Kind == MetadataOwnerKind.InboxItem);
        TaxonomyOwnerSnapshot library = Assert.Single(preparation.Owners, owner =>
            owner.Owner.Kind == MetadataOwnerKind.MediaAsset);
        Assert.Equal(
            [fixture.UnrelatedAuthor, fixture.SourceAuthorB, fixture.SourceAuthorA],
            inbox.Assignment.AuthorIds);
        Assert.Equal(
            [fixture.TargetAuthor, fixture.SourceAuthorA, fixture.UnrelatedAuthor],
            library.Assignment.AuthorIds);
        Assert.Equal(
            preparation.Owners.Select(owner => owner.Owner).Distinct().Count(),
            preparation.Owners.Count);
    }

    [Fact]
    public async Task Prepare_merge_loads_all_same_kind_name_owners()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture fixture =
            await database.SeedTaxonomyMaintenanceFixtureAsync();
        var store = Store(database);

        TaxonomyMergePrepareResult result = await store.PrepareMergeAsync(
            fixture.AuthorMergeSelection,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(5, result.Preparation!.SameKindNames.Count);
        Assert.Contains(result.Preparation.SameKindNames, owner =>
            owner.TermId == fixture.UnusedAuthor);
    }

    [Fact]
    public async Task Prepare_merge_rejects_missing_and_cross_kind_terms()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture fixture =
            await database.SeedTaxonomyMaintenanceFixtureAsync();
        var store = Store(database);

        TaxonomyMergePrepareResult missing = await store.PrepareMergeAsync(
            TaxonomyMergeSelection.Create(
                TaxonomyKind.Author,
                [Guid.NewGuid()],
                fixture.TargetAuthor),
            TestContext.Current.CancellationToken);
        TaxonomyMergePrepareResult crossKind = await store.PrepareMergeAsync(
            TaxonomyMergeSelection.Create(
                TaxonomyKind.Author,
                [fixture.SourceTagA],
                fixture.TargetAuthor),
            TestContext.Current.CancellationToken);

        Assert.False(missing.Succeeded);
        Assert.Equal(TaxonomyMaintenanceErrorCodes.NotFound, missing.ErrorCode);
        Assert.False(crossKind.Succeeded);
        Assert.Equal(TaxonomyMaintenanceErrorCodes.KindMismatch, crossKind.ErrorCode);
    }

    [Fact]
    public async Task Prepare_series_and_tags_load_both_owner_types_once()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture fixture =
            await database.SeedTaxonomyMaintenanceFixtureAsync();
        var store = Store(database);

        TaxonomyMergePrepareResult series = await store.PrepareMergeAsync(
            fixture.SeriesMergeSelection,
            TestContext.Current.CancellationToken);
        TaxonomyMergePrepareResult tags = await store.PrepareMergeAsync(
            fixture.TagMergeSelection,
            TestContext.Current.CancellationToken);

        Assert.True(series.Succeeded);
        Assert.Equal(2, series.Preparation!.Owners.Count);
        Assert.True(tags.Succeeded);
        Assert.Equal(2, tags.Preparation!.Owners.Count);
    }

    [Fact]
    public async Task Prepare_delete_returns_every_referenced_blocker()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture fixture =
            await database.SeedTaxonomyMaintenanceFixtureAsync();
        var store = Store(database);
        TaxonomyDeleteSelection selection = TaxonomyDeleteSelection.Create(
            TaxonomyKind.Author,
            [fixture.TargetAuthor, fixture.SourceAuthorA, fixture.UnusedAuthor]);

        TaxonomyDeletePrepareResult result = await store.PrepareDeleteAsync(
            selection,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.False(result.Preparation!.CanDelete);
        Assert.Equal(2, result.Preparation.Blockers.Count);
        Assert.Contains(result.Preparation.Blockers, blocker =>
            blocker.Term.Id == fixture.TargetAuthor
            && blocker.InboxReferenceCount == 0
            && blocker.LibraryReferenceCount == 1);
        Assert.Contains(result.Preparation.Blockers, blocker =>
            blocker.Term.Id == fixture.SourceAuthorA
            && blocker.InboxReferenceCount == 1
            && blocker.LibraryReferenceCount == 1);
    }

    [Fact]
    public async Task Prepare_delete_allows_an_exact_unused_set()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture fixture =
            await database.SeedTaxonomyMaintenanceFixtureAsync();
        var store = Store(database);

        TaxonomyDeletePrepareResult result = await store.PrepareDeleteAsync(
            TaxonomyDeleteSelection.Create(
                TaxonomyKind.Tag,
                fixture.UnusedTagIds),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.True(result.Preparation!.CanDelete);
        Assert.Equal(fixture.UnusedTagIds, result.Preparation.Terms.Select(term => term.Id));
        Assert.Empty(result.Preparation.Blockers);
    }

    private static async Task<TestMediaDatabase> CreateDatabaseAsync()
    {
        TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        return database;
    }

    private static EfTaxonomyMaintenanceStore Store(TestMediaDatabase database) =>
        new(database.CreateContextFactory(), TimeProvider.System);
}
