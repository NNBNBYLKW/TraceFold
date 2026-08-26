using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class LibraryBatchEditPersistenceTests
{
    [Fact]
    public async Task Metadata_and_names_commit_together_once()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        LibraryBatchFixture fixture = await database.SeedLibraryBatchFixtureAsync(3);

        LibraryBatchStoreResult result = await CreateStore(database).ApplyAsync(
            fixture.Selection,
            Draft(
                ReplaceAuthors("Alice"),
                Definition(new MetadataTemplateNamingRule(
                    "{authors}-{number}"))),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        IReadOnlyList<PersistedLibraryAssetSnapshot> rows =
            await database.ReadFormalAssetsAsync(fixture.AssetIds);
        Assert.Equal(
            ["Alice-1", "Alice-2", "Alice-3"],
            rows.Select(row => row.DisplayName));
        Assert.All(rows, row =>
        {
            Assert.Equal(1, row.MetadataVersion);
            Assert.Equal(["Alice"], row.AuthorNames);
        });
        Assert.Equal(3, result.UndoReceipt!.Items.Count);
    }

    [Fact]
    public async Task Trigger_failure_rolls_back_every_asset_and_new_taxonomy_term()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        LibraryBatchFixture fixture = await database.SeedLibraryBatchFixtureAsync(3);
        await database.CreateFormalAssetUpdateFailureTriggerAsync(
            fixture.AssetIds[1]);

        LibraryBatchStoreResult result = await CreateStore(database).ApplyAsync(
            fixture.Selection,
            Draft(
                AddAuthor("Transient"),
                Definition(new PrefixNamingRule("X-"))),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("library.batch.persistence_failed", result.ErrorCode);
        Assert.All(
            await database.ReadFormalAssetsAsync(fixture.AssetIds),
            row =>
            {
                Assert.Null(row.DisplayName);
                Assert.Equal(0, row.MetadataVersion);
                Assert.Empty(row.AuthorNames);
            });
        Assert.Equal(0, await database.CountTaxonomyTermsAsync(
            TaxonomyKind.Author,
            "Transient"));
    }

    [Fact]
    public async Task Stale_or_missing_one_of_many_rejects_the_whole_batch()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        LibraryBatchFixture fixture = await database.SeedLibraryBatchFixtureAsync(3);
        await database.SetFormalAssetVersionAsync(fixture.AssetIds[1], 9);
        EfLibraryBatchEditStore store = CreateStore(database);

        LibraryBatchStoreResult stale = await store.ApplyAsync(
            fixture.Selection,
            Draft(null, Definition(new PrefixNamingRule("X-"))),
            TestContext.Current.CancellationToken);
        LibrarySelectionEntry[] missingSelection = fixture.Selection
            .Select((item, index) => index switch
            {
                1 => item with { ExpectedMetadataVersion = 9 },
                2 => item with { AssetId = Guid.NewGuid() },
                _ => item
            })
            .ToArray();
        LibraryBatchStoreResult missing = await store.ApplyAsync(
            missingSelection,
            Draft(null, Definition(new PrefixNamingRule("X-"))),
            TestContext.Current.CancellationToken);

        Assert.Equal("library.batch.version_conflict", stale.ErrorCode);
        Assert.Equal("library.batch.selection_missing", missing.ErrorCode);
        IReadOnlyList<PersistedLibraryAssetSnapshot> rows =
            await database.ReadFormalAssetsAsync(fixture.AssetIds);
        Assert.All(rows, row => Assert.Null(row.DisplayName));
        Assert.Equal([0L, 9L, 0L], rows.Select(row => row.MetadataVersion));
    }

    [Fact]
    public async Task Invalid_taxonomy_or_generated_name_never_changes_assets()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        LibraryBatchFixture fixture = await database.SeedLibraryBatchFixtureAsync(2);
        EfLibraryBatchEditStore store = CreateStore(database);
        BatchMetadataDraft invalidTaxonomy = BatchMetadataDraft.Create(
            BatchMetadataMode.Add,
            new(
                BatchMetadataFieldOperation.Add,
                [BatchTaxonomyReference.Existing(
                    TaxonomyKind.Author,
                    Guid.NewGuid())]),
            BatchMetadataSeriesEdit.Unchanged,
            BatchMetadataSetEdit.Unchanged);

        LibraryBatchStoreResult taxonomy = await store.ApplyAsync(
            fixture.Selection,
            Draft(invalidTaxonomy, null),
            TestContext.Current.CancellationToken);
        LibraryBatchStoreResult tooLong = await store.ApplyAsync(
            fixture.Selection,
            Draft(
                null,
                Definition(new PrefixNamingRule(new string('X', 1025)))),
            TestContext.Current.CancellationToken);

        Assert.Equal("library.batch.taxonomy_missing", taxonomy.ErrorCode);
        Assert.Equal("library.batch.preview_invalid", tooLong.ErrorCode);
        Assert.All(
            await database.ReadFormalAssetsAsync(fixture.AssetIds),
            row =>
            {
                Assert.Null(row.DisplayName);
                Assert.Equal(0, row.MetadataVersion);
            });
    }

    [Fact]
    public async Task Duplicate_names_commit_with_warnings()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        LibraryBatchFixture fixture = await database.SeedLibraryBatchFixtureAsync(2);

        LibraryBatchStoreResult result = await CreateStore(database).ApplyAsync(
            fixture.Selection,
            Draft(
                null,
                Definition(new MetadataTemplateNamingRule("same"))),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.WarningCount);
        Assert.Equal(
            ["same", "same"],
            (await database.ReadFormalAssetsAsync(fixture.AssetIds))
                .Select(row => row.DisplayName));
    }

    [Fact]
    public async Task No_op_assets_do_not_increment_their_versions()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid author = await database.SeedTaxonomyAsync(
            TaxonomyKind.Author,
            "Existing");
        Guid unchanged = await database.SeedFormalAssetAsync(
            "unchanged.jpg",
            null,
            0,
            authorIds: [author]);
        Guid changed = await database.SeedFormalAssetAsync(
            "changed.jpg",
            null,
            0);
        LibrarySelectionEntry[] selection =
        [
            new(unchanged, 0, 0),
            new(changed, 0, 1)
        ];
        BatchMetadataDraft metadata = BatchMetadataDraft.Create(
            BatchMetadataMode.Add,
            new(
                BatchMetadataFieldOperation.Add,
                [BatchTaxonomyReference.Existing(
                    TaxonomyKind.Author,
                    author)]),
            BatchMetadataSeriesEdit.Unchanged,
            BatchMetadataSetEdit.Unchanged);

        LibraryBatchStoreResult result = await CreateStore(database).ApplyAsync(
            selection,
            Draft(metadata, null),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.ChangedCount);
        Assert.Equal(1, result.UnchangedCount);
        Assert.Equal(
            [0L, 1L],
            (await database.ReadFormalAssetsAsync([unchanged, changed]))
                .Select(row => row.MetadataVersion));
    }

    [Fact]
    public async Task Six_hundred_assets_are_chunked_inside_one_transaction()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        LibraryBatchFixture fixture =
            await database.SeedLibraryBatchFixtureAsync(600);

        LibraryBatchStoreResult result = await CreateStore(database).ApplyAsync(
            fixture.Selection,
            Draft(null, Definition(new PrefixNamingRule("X-"))),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(600, result.ChangedCount);
        Assert.All(
            await database.ReadFormalAssetsAsync(fixture.AssetIds),
            row =>
            {
                Assert.StartsWith("X-", row.DisplayName, StringComparison.Ordinal);
                Assert.Equal(1, row.MetadataVersion);
            });
    }

    [Fact]
    public async Task Prepare_returns_ordered_immutable_editor_snapshot()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        LibraryBatchFixture fixture = await database.SeedLibraryBatchFixtureAsync(2);
        await database.SeedFormalAssetAsync("outside.jpg", "Outside", 0);
        LibrarySelectionEntry[] reversed = fixture.Selection.Reverse().Select(
            (entry, ordinal) => entry with { Ordinal = ordinal }).ToArray();

        LibraryBatchPrepareResult result = await CreateStore(database).PrepareAsync(
            reversed,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(
            reversed.Select(item => item.AssetId),
            result.Editor!.Assets.Select(item => item.Selection.AssetId));
        Assert.Contains("Outside", result.Editor.UnselectedDisplayNames);
        Assert.DoesNotContain(
            result.Editor.Assets[0].ActualFilename,
            result.Editor.UnselectedDisplayNames);
    }

    [Fact]
    public async Task Undo_restores_names_terms_and_versions_atomically()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid oldAuthor = await database.SeedTaxonomyAsync(
            TaxonomyKind.Author,
            "Old author");
        Guid oldSeries = await database.SeedTaxonomyAsync(
            TaxonomyKind.Series,
            "Old series");
        Guid oldTag = await database.SeedTaxonomyAsync(
            TaxonomyKind.Tag,
            "Old tag");
        Guid first = await database.SeedFormalAssetAsync(
            "first.jpg",
            "First old",
            0,
            authorIds: [oldAuthor],
            seriesId: oldSeries,
            tagIds: [oldTag]);
        Guid second = await database.SeedFormalAssetAsync(
            "second.jpg",
            null,
            0,
            authorIds: [oldAuthor],
            seriesId: oldSeries,
            tagIds: [oldTag]);
        Guid[] assetIds = [first, second];
        LibrarySelectionEntry[] selection =
        [
            new(first, 0, 0),
            new(second, 0, 1)
        ];
        IReadOnlyList<PersistedLibraryAssetSnapshot> before =
            await database.ReadFormalAssetsAsync(assetIds);
        EfLibraryBatchEditStore store = CreateStore(database);
        LibraryBatchStoreResult applied = await store.ApplyAsync(
            selection,
            Draft(
                ReplaceAuthors("Created during batch"),
                Definition(new MetadataTemplateNamingRule(
                    "{authors}-{number}"))),
            TestContext.Current.CancellationToken);
        Assert.NotNull(applied.UndoReceipt);

        LibraryBatchUndoResult undone = await store.UndoAsync(
            applied.UndoReceipt,
            TestContext.Current.CancellationToken);

        Assert.True(undone.Succeeded);
        Assert.Equal(2, undone.RestoredCount);
        IReadOnlyList<PersistedLibraryAssetSnapshot> restored =
            await database.ReadFormalAssetsAsync(assetIds);
        Assert.Equal(
            before.Select(item => item.DisplayName),
            restored.Select(item => item.DisplayName));
        Assert.Equal(
            before.Select(item => string.Join('|', item.AuthorNames)),
            restored.Select(item => string.Join('|', item.AuthorNames)));
        Assert.Equal(
            before.Select(item => item.SeriesId),
            restored.Select(item => item.SeriesId));
        Assert.Equal(
            before.Select(item => string.Join('|', item.TagNames)),
            restored.Select(item => string.Join('|', item.TagNames)));
        Assert.All(restored, item => Assert.Equal(2, item.MetadataVersion));
        Assert.Equal(1, await database.CountTaxonomyTermsAsync(
            TaxonomyKind.Author,
            "Created during batch"));
    }

    [Fact]
    public async Task One_changed_post_version_rejects_the_entire_undo()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        LibraryBatchFixture fixture = await database.SeedLibraryBatchFixtureAsync(3);
        EfLibraryBatchEditStore store = CreateStore(database);
        LibraryBatchStoreResult applied = await store.ApplyAsync(
            fixture.Selection,
            Draft(null, Definition(new PrefixNamingRule("Applied-"))),
            TestContext.Current.CancellationToken);
        Assert.NotNull(applied.UndoReceipt);
        await database.SetFormalAssetVersionAsync(fixture.AssetIds[1], 99);

        LibraryBatchUndoResult undone = await store.UndoAsync(
            applied.UndoReceipt,
            TestContext.Current.CancellationToken);

        Assert.False(undone.Succeeded);
        Assert.Equal(
            "library.batch.undo.version_conflict",
            undone.ErrorCode);
        IReadOnlyList<PersistedLibraryAssetSnapshot> current =
            await database.ReadFormalAssetsAsync(fixture.AssetIds);
        Assert.All(current, item => Assert.StartsWith(
            "Applied-",
            item.DisplayName,
            StringComparison.Ordinal));
        Assert.Equal([1L, 99L, 1L], current.Select(item => item.MetadataVersion));
    }

    private static EfLibraryBatchEditStore CreateStore(
        TestMediaDatabase database) =>
        new(database.CreateContextFactory(), new NamingRuleEngine());

    private static LibraryBatchEditDraft Draft(
        BatchMetadataDraft? metadata,
        NamingDefinition? naming) =>
        LibraryBatchEditDraft.Create(metadata, naming);

    private static BatchMetadataDraft ReplaceAuthors(string name) =>
        BatchMetadataDraft.Create(
            BatchMetadataMode.Replace,
            new(
                BatchMetadataFieldOperation.Replace,
                [BatchTaxonomyReference.New(TaxonomyKind.Author, name)]),
            BatchMetadataSeriesEdit.Unchanged,
            BatchMetadataSetEdit.Unchanged);

    private static BatchMetadataDraft AddAuthor(string name) =>
        BatchMetadataDraft.Create(
            BatchMetadataMode.Add,
            new(
                BatchMetadataFieldOperation.Add,
                [BatchTaxonomyReference.New(TaxonomyKind.Author, name)]),
            BatchMetadataSeriesEdit.Unchanged,
            BatchMetadataSetEdit.Unchanged);

    private static NamingDefinition Definition(params INamingRule[] rules) =>
        NamingDefinition.Create(NamingSource.ActualFilename, rules);
}
