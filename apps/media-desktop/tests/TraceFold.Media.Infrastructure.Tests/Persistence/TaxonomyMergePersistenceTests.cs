using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class TaxonomyMergePersistenceTests
{
    [Fact]
    public async Task Merge_moves_both_owner_types_deduplicates_and_versions_once()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture fixture =
            await database.SeedTaxonomyMaintenanceFixtureAsync();
        EfTaxonomyMaintenanceStore store = Store(database);
        TaxonomyMergePrepareResult prepared = await store.PrepareMergeAsync(
            fixture.AuthorMergeSelection,
            TestContext.Current.CancellationToken);

        TaxonomyMaintenanceStoreResult result = await store.ApplyMergeAsync(
            prepared.Preparation!,
            TaxonomyName.Create("Merged Author"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.IsType<TaxonomyMergeUndoReceipt>(result.UndoReceipt);
        Assert.Equal(2, result.DeletedTermCount);
        Assert.Equal(2, result.ChangedOwners.Count);
        Assert.Equal(
            [fixture.UnrelatedAuthor, fixture.TargetAuthor],
            await database.ReadInboxAuthorIdsAsync(fixture.InboxId));
        Assert.Equal(
            [fixture.TargetAuthor, fixture.UnrelatedAuthor],
            await database.ReadAssetAuthorIdsAsync(fixture.AssetId));
        Assert.Equal(
            fixture.InboxVersion + 1,
            await database.ReadInboxVersionAsync(fixture.InboxId));
        Assert.Equal(
            fixture.AssetVersion + 1,
            await database.ReadAssetVersionAsync(fixture.AssetId));
        Assert.False(await database.TaxonomyTermExistsAsync(fixture.SourceAuthorA));
        Assert.False(await database.TaxonomyTermExistsAsync(fixture.SourceAuthorB));
        Assert.Equal(
            "Merged Author",
            await database.ReadTaxonomyDisplayNameAsync(fixture.TargetAuthor));
    }

    [Fact]
    public async Task Merge_applies_tag_order_and_series_replacement()
    {
        await using TestMediaDatabase tagDatabase = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture tags =
            await tagDatabase.SeedTaxonomyMaintenanceFixtureAsync();
        EfTaxonomyMaintenanceStore tagStore = Store(tagDatabase);
        TaxonomyMergePrepareResult tagPrepared = await tagStore.PrepareMergeAsync(
            tags.TagMergeSelection,
            TestContext.Current.CancellationToken);

        TaxonomyMaintenanceStoreResult tagResult = await tagStore.ApplyMergeAsync(
            tagPrepared.Preparation!,
            finalName: null,
            TestContext.Current.CancellationToken);

        Assert.True(tagResult.Succeeded);
        Assert.Equal(
            [tags.UnrelatedTag, tags.TargetTag],
            await tagDatabase.ReadInboxTagIdsAsync(tags.InboxId));
        Assert.Equal(
            [tags.TargetTag, tags.UnrelatedTag],
            await tagDatabase.ReadAssetTagIdsAsync(tags.AssetId));

        await using TestMediaDatabase seriesDatabase = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture series =
            await seriesDatabase.SeedTaxonomyMaintenanceFixtureAsync();
        EfTaxonomyMaintenanceStore seriesStore = Store(seriesDatabase);
        TaxonomyMergePrepareResult seriesPrepared =
            await seriesStore.PrepareMergeAsync(
                series.SeriesMergeSelection,
                TestContext.Current.CancellationToken);

        TaxonomyMaintenanceStoreResult seriesResult =
            await seriesStore.ApplyMergeAsync(
                seriesPrepared.Preparation!,
                finalName: null,
                TestContext.Current.CancellationToken);

        Assert.True(seriesResult.Succeeded);
        Assert.Equal(
            series.TargetSeries,
            await seriesDatabase.ReadInboxSeriesIdAsync(series.InboxId));
        Assert.Equal(
            series.TargetSeries,
            await seriesDatabase.ReadAssetSeriesIdAsync(series.AssetId));
    }

    [Fact]
    public async Task Merge_can_reuse_a_source_name_and_change_no_owner_versions()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture fixture =
            await database.SeedTaxonomyMaintenanceFixtureAsync();
        EfTaxonomyMaintenanceStore store = Store(database);
        TaxonomyMergeSelection selection = TaxonomyMergeSelection.Create(
            TaxonomyKind.Author,
            [fixture.UnusedAuthor],
            fixture.TargetAuthor);
        TaxonomyMergePrepareResult prepared = await store.PrepareMergeAsync(
            selection,
            TestContext.Current.CancellationToken);

        TaxonomyMaintenanceStoreResult result = await store.ApplyMergeAsync(
            prepared.Preparation!,
            TaxonomyName.Create("Unused Author"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Empty(result.ChangedOwners);
        Assert.Equal(
            fixture.InboxVersion,
            await database.ReadInboxVersionAsync(fixture.InboxId));
        Assert.Equal(
            fixture.AssetVersion,
            await database.ReadAssetVersionAsync(fixture.AssetId));
        Assert.Equal(
            "Unused Author",
            await database.ReadTaxonomyDisplayNameAsync(fixture.TargetAuthor));
    }

    [Fact]
    public async Task Third_term_name_collision_rolls_back_every_change()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture fixture =
            await database.SeedTaxonomyMaintenanceFixtureAsync();
        EfTaxonomyMaintenanceStore store = Store(database);
        TaxonomyMergePrepareResult prepared = await store.PrepareMergeAsync(
            fixture.AuthorMergeSelection,
            TestContext.Current.CancellationToken);

        TaxonomyMaintenanceStoreResult result = await store.ApplyMergeAsync(
            prepared.Preparation!,
            TaxonomyName.Create("Unrelated Author"),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(TaxonomyMaintenanceErrorCodes.NameConflict, result.ErrorCode);
        Assert.True(await database.TaxonomyTermExistsAsync(fixture.SourceAuthorA));
        Assert.True(await database.TaxonomyTermExistsAsync(fixture.SourceAuthorB));
        Assert.Equal(
            fixture.InboxVersion,
            await database.ReadInboxVersionAsync(fixture.InboxId));
        Assert.Equal(
            [fixture.UnrelatedAuthor, fixture.SourceAuthorB, fixture.SourceAuthorA],
            await database.ReadInboxAuthorIdsAsync(fixture.InboxId));
    }

    [Fact]
    public async Task Renamed_term_or_changed_owner_version_rejects_stale_preview()
    {
        await using TestMediaDatabase renamedDatabase = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture renamed =
            await renamedDatabase.SeedTaxonomyMaintenanceFixtureAsync();
        EfTaxonomyMaintenanceStore renamedStore = Store(renamedDatabase);
        TaxonomyMergePrepareResult renamedPreparation =
            await renamedStore.PrepareMergeAsync(
                renamed.AuthorMergeSelection,
                TestContext.Current.CancellationToken);
        await renamedDatabase.RenameTaxonomyAsync(
            renamed.SourceAuthorA,
            "Renamed Source");

        TaxonomyMaintenanceStoreResult renamedResult =
            await renamedStore.ApplyMergeAsync(
                renamedPreparation.Preparation!,
                finalName: null,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            TaxonomyMaintenanceErrorCodes.PreviewStale,
            renamedResult.ErrorCode);
        Assert.True(await renamedDatabase.TaxonomyTermExistsAsync(
            renamed.SourceAuthorA));

        await using TestMediaDatabase versionDatabase = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture versioned =
            await versionDatabase.SeedTaxonomyMaintenanceFixtureAsync();
        EfTaxonomyMaintenanceStore versionStore = Store(versionDatabase);
        TaxonomyMergePrepareResult versionPreparation =
            await versionStore.PrepareMergeAsync(
                versioned.AuthorMergeSelection,
                TestContext.Current.CancellationToken);
        await versionDatabase.SetDraftVersionAsync(versioned.InboxId, 99);

        TaxonomyMaintenanceStoreResult versionResult =
            await versionStore.ApplyMergeAsync(
                versionPreparation.Preparation!,
                finalName: null,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            TaxonomyMaintenanceErrorCodes.PreviewStale,
            versionResult.ErrorCode);
        Assert.True(await versionDatabase.TaxonomyTermExistsAsync(
            versioned.SourceAuthorA));
    }

    [Fact]
    public async Task Late_source_reference_rejects_the_whole_merge()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture fixture =
            await database.SeedTaxonomyMaintenanceFixtureAsync();
        EfTaxonomyMaintenanceStore store = Store(database);
        TaxonomyMergePrepareResult prepared = await store.PrepareMergeAsync(
            fixture.AuthorMergeSelection,
            TestContext.Current.CancellationToken);
        Guid lateOwner = await database.SeedInboxItemAsync(
            InboxItemState.ReadyToTag,
            metadataVersion: 0,
            displayName: "Late owner");
        await database.AddInboxAuthorReferenceAsync(
            lateOwner,
            fixture.SourceAuthorA,
            ordinal: 0);

        TaxonomyMaintenanceStoreResult result = await store.ApplyMergeAsync(
            prepared.Preparation!,
            finalName: null,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(TaxonomyMaintenanceErrorCodes.PreviewStale, result.ErrorCode);
        Assert.True(await database.TaxonomyTermExistsAsync(fixture.SourceAuthorA));
        Assert.Equal(
            [fixture.SourceAuthorA],
            await database.ReadInboxAuthorIdsAsync(lateOwner));
    }

    [Fact]
    public async Task Later_write_failure_rolls_back_terms_links_and_versions()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture fixture =
            await database.SeedTaxonomyMaintenanceFixtureAsync();
        EfTaxonomyMaintenanceStore store = Store(database);
        TaxonomyMergePrepareResult prepared = await store.PrepareMergeAsync(
            fixture.AuthorMergeSelection,
            TestContext.Current.CancellationToken);
        await database.CreateTaxonomyRenameFailureTriggerAsync(
            fixture.TargetAuthor);

        TaxonomyMaintenanceStoreResult result = await store.ApplyMergeAsync(
            prepared.Preparation!,
            TaxonomyName.Create("Trigger Rename"),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(
            TaxonomyMaintenanceErrorCodes.PersistenceFailed,
            result.ErrorCode);
        Assert.True(await database.TaxonomyTermExistsAsync(fixture.SourceAuthorA));
        Assert.True(await database.TaxonomyTermExistsAsync(fixture.SourceAuthorB));
        Assert.Equal(
            [fixture.UnrelatedAuthor, fixture.SourceAuthorB, fixture.SourceAuthorA],
            await database.ReadInboxAuthorIdsAsync(fixture.InboxId));
        Assert.Equal(
            fixture.InboxVersion,
            await database.ReadInboxVersionAsync(fixture.InboxId));
        Assert.Equal(
            fixture.AssetVersion,
            await database.ReadAssetVersionAsync(fixture.AssetId));
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
