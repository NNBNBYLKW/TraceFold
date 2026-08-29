using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class TaxonomyMaintenanceUndoPersistenceTests
{
    [Fact]
    public async Task Merge_undo_restores_term_ids_name_order_and_advances_versions()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture fixture =
            await database.SeedTaxonomyMaintenanceFixtureAsync();
        IReadOnlyList<Guid> originalInboxOrder =
            await database.ReadInboxAuthorIdsAsync(fixture.InboxId);
        IReadOnlyList<Guid> originalAssetOrder =
            await database.ReadAssetAuthorIdsAsync(fixture.AssetId);
        EfTaxonomyMaintenanceStore store = Store(database);
        TaxonomyMergePrepareResult prepared = await store.PrepareMergeAsync(
            fixture.AuthorMergeSelection,
            TestContext.Current.CancellationToken);
        TaxonomyMaintenanceStoreResult applied = await store.ApplyMergeAsync(
            prepared.Preparation!,
            TaxonomyName.Create("Merged Target"),
            TestContext.Current.CancellationToken);
        TaxonomyMergeUndoReceipt receipt = Assert.IsType<
            TaxonomyMergeUndoReceipt>(applied.UndoReceipt);

        TaxonomyMaintenanceUndoResult undone = await store.UndoAsync(
            receipt,
            TestContext.Current.CancellationToken);

        Assert.True(undone.Succeeded);
        Assert.Equal(2, undone.RestoredOwnerCount);
        Assert.Equal(2, undone.RestoredTermCount);
        Assert.Equal(
            originalInboxOrder,
            await database.ReadInboxAuthorIdsAsync(fixture.InboxId));
        Assert.Equal(
            originalAssetOrder,
            await database.ReadAssetAuthorIdsAsync(fixture.AssetId));
        Assert.True(await database.TaxonomyTermExistsAsync(fixture.SourceAuthorA));
        Assert.True(await database.TaxonomyTermExistsAsync(fixture.SourceAuthorB));
        Assert.Equal(
            "Target Author",
            await database.ReadTaxonomyDisplayNameAsync(fixture.TargetAuthor));
        Assert.Equal(
            fixture.InboxVersion + 2,
            await database.ReadInboxVersionAsync(fixture.InboxId));
        Assert.Equal(
            fixture.AssetVersion + 2,
            await database.ReadAssetVersionAsync(fixture.AssetId));
    }

    [Fact]
    public async Task Delete_undo_restores_stable_term_ids_and_names()
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
        TaxonomyMaintenanceStoreResult deleted = await store.DeleteUnusedAsync(
            prepared.Preparation!,
            TestContext.Current.CancellationToken);
        TaxonomyDeleteUndoReceipt receipt = Assert.IsType<
            TaxonomyDeleteUndoReceipt>(deleted.UndoReceipt);

        TaxonomyMaintenanceUndoResult undone = await store.UndoAsync(
            receipt,
            TestContext.Current.CancellationToken);

        Assert.True(undone.Succeeded);
        Assert.Equal(0, undone.RestoredOwnerCount);
        Assert.Equal(2, undone.RestoredTermCount);
        Assert.Equal(
            "Unused Tag A",
            await database.ReadTaxonomyDisplayNameAsync(fixture.UnusedTagIds[0]));
        Assert.Equal(
            "Unused Tag B",
            await database.ReadTaxonomyDisplayNameAsync(fixture.UnusedTagIds[1]));
    }

    [Fact]
    public async Task Changed_owner_version_rejects_the_entire_merge_undo()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture fixture =
            await database.SeedTaxonomyMaintenanceFixtureAsync();
        EfTaxonomyMaintenanceStore store = Store(database);
        TaxonomyMergePrepareResult prepared = await store.PrepareMergeAsync(
            fixture.AuthorMergeSelection,
            TestContext.Current.CancellationToken);
        TaxonomyMaintenanceStoreResult applied = await store.ApplyMergeAsync(
            prepared.Preparation!,
            finalName: null,
            TestContext.Current.CancellationToken);
        await database.SetFormalAssetVersionAsync(fixture.AssetId, 99);

        TaxonomyMaintenanceUndoResult undone = await store.UndoAsync(
            applied.UndoReceipt!,
            TestContext.Current.CancellationToken);

        Assert.False(undone.Succeeded);
        Assert.False(undone.ReceiptIsRetryable);
        Assert.Equal(TaxonomyMaintenanceErrorCodes.UndoConflict, undone.ErrorCode);
        Assert.False(await database.TaxonomyTermExistsAsync(fixture.SourceAuthorA));
        Assert.Equal(
            [fixture.UnrelatedAuthor, fixture.TargetAuthor],
            await database.ReadInboxAuthorIdsAsync(fixture.InboxId));
        Assert.Equal(99, await database.ReadAssetVersionAsync(fixture.AssetId));
    }

    [Fact]
    public async Task Occupied_source_name_rejects_delete_free_merge_undo()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture fixture =
            await database.SeedTaxonomyMaintenanceFixtureAsync();
        EfTaxonomyMaintenanceStore store = Store(database);
        TaxonomyMergePrepareResult prepared = await store.PrepareMergeAsync(
            fixture.AuthorMergeSelection,
            TestContext.Current.CancellationToken);
        TaxonomyMaintenanceStoreResult applied = await store.ApplyMergeAsync(
            prepared.Preparation!,
            finalName: null,
            TestContext.Current.CancellationToken);
        Guid occupyingTerm = await database.SeedTaxonomyAsync(
            TaxonomyKind.Author,
            "Source Author A");

        TaxonomyMaintenanceUndoResult undone = await store.UndoAsync(
            applied.UndoReceipt!,
            TestContext.Current.CancellationToken);

        Assert.False(undone.Succeeded);
        Assert.False(undone.ReceiptIsRetryable);
        Assert.Equal(TaxonomyMaintenanceErrorCodes.UndoConflict, undone.ErrorCode);
        Assert.False(await database.TaxonomyTermExistsAsync(fixture.SourceAuthorA));
        Assert.True(await database.TaxonomyTermExistsAsync(occupyingTerm));
    }

    [Fact]
    public async Task Transient_merge_undo_failure_rolls_back_and_is_retryable()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        TaxonomyMaintenanceFixture fixture =
            await database.SeedTaxonomyMaintenanceFixtureAsync();
        EfTaxonomyMaintenanceStore store = Store(database);
        TaxonomyMergePrepareResult prepared = await store.PrepareMergeAsync(
            fixture.AuthorMergeSelection,
            TestContext.Current.CancellationToken);
        TaxonomyMaintenanceStoreResult applied = await store.ApplyMergeAsync(
            prepared.Preparation!,
            TaxonomyName.Create("Merged Target"),
            TestContext.Current.CancellationToken);
        await database.CreateMetadataUpdateFailureTriggerAsync(fixture.InboxId);

        TaxonomyMaintenanceUndoResult undone = await store.UndoAsync(
            applied.UndoReceipt!,
            TestContext.Current.CancellationToken);

        Assert.False(undone.Succeeded);
        Assert.True(undone.ReceiptIsRetryable);
        Assert.Equal(
            TaxonomyMaintenanceErrorCodes.PersistenceFailed,
            undone.ErrorCode);
        Assert.False(await database.TaxonomyTermExistsAsync(fixture.SourceAuthorA));
        Assert.False(await database.TaxonomyTermExistsAsync(fixture.SourceAuthorB));
        Assert.Equal(
            "Merged Target",
            await database.ReadTaxonomyDisplayNameAsync(fixture.TargetAuthor));
        Assert.Equal(
            [fixture.UnrelatedAuthor, fixture.TargetAuthor],
            await database.ReadInboxAuthorIdsAsync(fixture.InboxId));
        Assert.Equal(
            [fixture.TargetAuthor, fixture.UnrelatedAuthor],
            await database.ReadAssetAuthorIdsAsync(fixture.AssetId));
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
