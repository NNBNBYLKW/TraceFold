using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class TaxonomyDeletePersistenceTests
{
    [Fact]
    public async Task Delete_unused_removes_every_selected_term_and_returns_one_receipt()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture fixture =
            await database.SeedTaxonomyMaintenanceFixtureAsync();
        EfTaxonomyMaintenanceStore store = Store(database);
        TaxonomyDeletePrepareResult prepared = await store.PrepareDeleteAsync(
            TaxonomyDeleteSelection.Create(
                TaxonomyKind.Tag,
                fixture.UnusedTagIds),
            TestContext.Current.CancellationToken);

        TaxonomyMaintenanceStoreResult result = await store.DeleteUnusedAsync(
            prepared.Preparation!,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(fixture.UnusedTagIds.Count, result.DeletedTermCount);
        TaxonomyDeleteUndoReceipt receipt = Assert.IsType<
            TaxonomyDeleteUndoReceipt>(result.UndoReceipt);
        Assert.Equal(fixture.UnusedTagIds, receipt.Terms.Select(term => term.Id));
        Assert.Empty(result.ChangedOwners);
        foreach (Guid termId in fixture.UnusedTagIds)
        {
            Assert.False(await database.TaxonomyTermExistsAsync(termId));
        }
    }

    [Fact]
    public async Task Delete_refuses_the_entire_mixed_referenced_selection()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture fixture =
            await database.SeedTaxonomyMaintenanceFixtureAsync();
        EfTaxonomyMaintenanceStore store = Store(database);
        TaxonomyDeletePrepareResult prepared = await store.PrepareDeleteAsync(
            TaxonomyDeleteSelection.Create(
                TaxonomyKind.Author,
                [fixture.SourceAuthorA, fixture.UnusedAuthor]),
            TestContext.Current.CancellationToken);

        TaxonomyMaintenanceStoreResult result = await store.DeleteUnusedAsync(
            prepared.Preparation!,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(TaxonomyMaintenanceErrorCodes.DeleteInUse, result.ErrorCode);
        Assert.True(await database.TaxonomyTermExistsAsync(fixture.SourceAuthorA));
        Assert.True(await database.TaxonomyTermExistsAsync(fixture.UnusedAuthor));
    }

    [Fact]
    public async Task Late_reference_rejects_every_prepared_unused_term()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture fixture =
            await database.SeedTaxonomyMaintenanceFixtureAsync();
        EfTaxonomyMaintenanceStore store = Store(database);
        TaxonomyDeletePrepareResult prepared = await store.PrepareDeleteAsync(
            TaxonomyDeleteSelection.Create(
                TaxonomyKind.Tag,
                fixture.UnusedTagIds),
            TestContext.Current.CancellationToken);
        Guid lateOwner = await database.SeedInboxItemAsync(
            InboxItemState.ReadyToTag,
            metadataVersion: 0,
            displayName: "Late delete owner");
        await database.AddInboxTagReferenceAsync(
            lateOwner,
            fixture.UnusedTagIds[0],
            ordinal: 0);

        TaxonomyMaintenanceStoreResult result = await store.DeleteUnusedAsync(
            prepared.Preparation!,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(TaxonomyMaintenanceErrorCodes.DeleteInUse, result.ErrorCode);
        foreach (Guid termId in fixture.UnusedTagIds)
        {
            Assert.True(await database.TaxonomyTermExistsAsync(termId));
        }
    }

    [Fact]
    public async Task Renamed_or_missing_term_rejects_stale_delete_snapshot()
    {
        await using TestMediaDatabase renamedDatabase = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture renamed =
            await renamedDatabase.SeedTaxonomyMaintenanceFixtureAsync();
        EfTaxonomyMaintenanceStore renamedStore = Store(renamedDatabase);
        TaxonomyDeletePrepareResult renamedPreparation =
            await renamedStore.PrepareDeleteAsync(
                TaxonomyDeleteSelection.Create(
                    TaxonomyKind.Tag,
                    renamed.UnusedTagIds),
                TestContext.Current.CancellationToken);
        await renamedDatabase.RenameTaxonomyAsync(
            renamed.UnusedTagIds[0],
            "Renamed Unused Tag");

        TaxonomyMaintenanceStoreResult renamedResult =
            await renamedStore.DeleteUnusedAsync(
                renamedPreparation.Preparation!,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            TaxonomyMaintenanceErrorCodes.PreviewStale,
            renamedResult.ErrorCode);
        foreach (Guid termId in renamed.UnusedTagIds)
        {
            Assert.True(await renamedDatabase.TaxonomyTermExistsAsync(termId));
        }

        await using TestMediaDatabase missingDatabase = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture missing =
            await missingDatabase.SeedTaxonomyMaintenanceFixtureAsync();
        EfTaxonomyMaintenanceStore missingStore = Store(missingDatabase);
        TaxonomyDeletePrepareResult missingPreparation =
            await missingStore.PrepareDeleteAsync(
                TaxonomyDeleteSelection.Create(
                    TaxonomyKind.Tag,
                    missing.UnusedTagIds),
                TestContext.Current.CancellationToken);
        await missingDatabase.DeleteTaxonomyAsync(missing.UnusedTagIds[0]);

        TaxonomyMaintenanceStoreResult missingResult =
            await missingStore.DeleteUnusedAsync(
                missingPreparation.Preparation!,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            TaxonomyMaintenanceErrorCodes.PreviewStale,
            missingResult.ErrorCode);
        Assert.True(await missingDatabase.TaxonomyTermExistsAsync(
            missing.UnusedTagIds[1]));
    }

    [Fact]
    public async Task Delete_failure_rolls_back_every_selected_term()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture fixture =
            await database.SeedTaxonomyMaintenanceFixtureAsync();
        EfTaxonomyMaintenanceStore store = Store(database);
        TaxonomyDeletePrepareResult prepared = await store.PrepareDeleteAsync(
            TaxonomyDeleteSelection.Create(
                TaxonomyKind.Tag,
                fixture.UnusedTagIds),
            TestContext.Current.CancellationToken);
        await database.CreateTaxonomyDeleteFailureTriggerAsync(
            fixture.UnusedTagIds[1]);

        TaxonomyMaintenanceStoreResult result = await store.DeleteUnusedAsync(
            prepared.Preparation!,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(
            TaxonomyMaintenanceErrorCodes.PersistenceFailed,
            result.ErrorCode);
        foreach (Guid termId in fixture.UnusedTagIds)
        {
            Assert.True(await database.TaxonomyTermExistsAsync(termId));
        }
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
