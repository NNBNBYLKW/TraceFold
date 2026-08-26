using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class BatchMetadataPersistenceTests
{
    [Fact]
    public async Task Apply_creates_or_reuses_taxonomy_and_commits_every_draft_once()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid[] ids = await database.SeedEditableInboxItemsAsync(3);
        var store = new EfBatchMetadataStore(database.CreateContextFactory());

        BatchMetadataResult result = await store.ApplyAsync(
            AddNewAuthor(ids, "作者甲"), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.ChangedCount);
        Assert.Equal(3, await database.CountDraftsWithAuthorAsync("作者甲"));
        Assert.Equal(1, await database.CountTaxonomyTermsAsync(
            TaxonomyKind.Author, "作者甲"));
        Assert.All(await database.LoadMetadataVersionsAsync(ids), version =>
            Assert.Equal(1, version));
    }

    [Fact]
    public async Task Normalized_new_name_reuses_an_existing_taxonomy_term()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid[] ids = await database.SeedEditableInboxItemsAsync(2);
        await database.SeedTaxonomyAsync(TaxonomyKind.Author, "Ａ作者");
        var store = new EfBatchMetadataStore(database.CreateContextFactory());

        BatchMetadataResult result = await store.ApplyAsync(
            AddNewAuthor(ids, "A作者"), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(1, await database.CountTaxonomyTermsAsync(
            TaxonomyKind.Author, "A作者"));
        Assert.Equal(2, await database.CountDraftsWithAuthorAsync("A作者"));
    }

    [Fact]
    public async Task One_stale_version_rolls_back_every_item_and_new_term()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid[] ids = await database.SeedEditableInboxItemsAsync(3);
        await database.SetDraftVersionAsync(ids[1], 4);
        var store = new EfBatchMetadataStore(database.CreateContextFactory());

        BatchMetadataResult result = await store.ApplyAsync(
            AddNewAuthor(ids, "不会落库", expectedVersion: 0),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("batch.metadata.version_conflict", result.ErrorCode);
        Assert.Equal([0L, 4L, 0L], await database.LoadMetadataVersionsAsync(ids));
        Assert.Equal(0, await database.CountTaxonomyTermsAsync(
            TaxonomyKind.Author, "不会落库"));
    }

    [Fact]
    public async Task Trigger_failure_after_first_write_chunk_rolls_back_all_chunks()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid[] ids = await database.SeedEditableInboxItemsAsync(401);
        await database.CreateMetadataUpdateFailureTriggerAsync(ids[250]);
        var store = new EfBatchMetadataStore(database.CreateContextFactory());

        BatchMetadataResult result = await store.ApplyAsync(
            AddNewAuthor(ids, "批量作者"), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("batch.metadata.persistence_failed", result.ErrorCode);
        Assert.All(await database.LoadMetadataVersionsAsync(ids), version =>
            Assert.Equal(0, version));
        Assert.Equal(0, await database.CountTaxonomyTermsAsync(
            TaxonomyKind.Author, "批量作者"));
    }

    [Fact]
    public async Task Six_hundred_items_use_chunked_reads_without_partial_commit()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid[] ids = await database.SeedEditableInboxItemsAsync(600);
        Guid tagId = await database.SeedTaxonomyAsync(TaxonomyKind.Tag, "风景");
        var store = new EfBatchMetadataStore(database.CreateContextFactory());

        BatchMetadataResult result = await store.ApplyAsync(
            AddExistingTag(ids, tagId), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(600, result.ChangedCount);
        Assert.All(await database.LoadMetadataVersionsAsync(ids), version =>
            Assert.Equal(1, version));
    }

    [Fact]
    public async Task Prepare_returns_only_selected_author_and_tag_coverage()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid[] ids = await database.SeedEditableInboxItemsAsync(3);
        Guid authorA = await database.SeedTaxonomyAsync(TaxonomyKind.Author, "作者甲");
        Guid authorB = await database.SeedTaxonomyAsync(TaxonomyKind.Author, "作者乙");
        Guid tag = await database.SeedTaxonomyAsync(TaxonomyKind.Tag, "风景");
        var metadata = new EfMetadataStore(database.CreateContextFactory());
        await metadata.TrySaveAsync(
            new MetadataOwner(MetadataOwnerKind.InboxItem, ids[0]),
            MetadataSelection.Create(null, [authorA, authorB], null, [tag]),
            0,
            TestContext.Current.CancellationToken);
        await metadata.TrySaveAsync(
            new MetadataOwner(MetadataOwnerKind.InboxItem, ids[1]),
            MetadataSelection.Create(null, [authorA], null, []),
            0,
            TestContext.Current.CancellationToken);
        var selection = new[]
        {
            new InboxSelectionEntry(ids[0], 1, 0),
            new InboxSelectionEntry(ids[1], 1, 1),
            new InboxSelectionEntry(ids[2], 0, 2)
        };
        var store = new EfBatchMetadataStore(database.CreateContextFactory());

        BatchMetadataEditorDto editor = Assert.IsType<BatchMetadataEditorDto>(
            await store.PrepareAsync(selection, TestContext.Current.CancellationToken));

        Assert.Equal(3, editor.SelectedCount);
        Assert.Equal(
            editor.Authors.Select(item => item.Term.DisplayName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
            editor.Authors.Select(item => item.Term.DisplayName));
        Dictionary<string, int> authorCoverage = editor.Authors.ToDictionary(
            item => item.Term.DisplayName,
            item => item.SelectedCount);
        Assert.Equal(2, authorCoverage["作者甲"]);
        Assert.Equal(1, authorCoverage["作者乙"]);
        BatchTermCoverageDto tagCoverage = Assert.Single(editor.Tags);
        Assert.Equal("风景", tagCoverage.Term.DisplayName);
        Assert.Equal(1, tagCoverage.SelectedCount);
    }

    [Fact]
    public async Task Prepare_rejects_missing_stale_or_disallowed_items()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid[] ids = await database.SeedEditableInboxItemsAsync(1);
        Guid waiting = await database.SeedInboxItemAsync(
            TraceFold.Media.Domain.Inbox.InboxItemState.WaitingForStability,
            metadataVersion: 0);
        var store = new EfBatchMetadataStore(database.CreateContextFactory());

        Assert.Null(await store.PrepareAsync(
            [new(ids[0], 1, 0)], TestContext.Current.CancellationToken));
        Assert.Null(await store.PrepareAsync(
            [new(Guid.NewGuid(), 0, 0)], TestContext.Current.CancellationToken));
        Assert.Null(await store.PrepareAsync(
            [new(waiting, 0, 0)], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task No_op_remove_keeps_every_metadata_version_unchanged()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid[] ids = await database.SeedEditableInboxItemsAsync(3);
        Guid absent = await database.SeedTaxonomyAsync(TaxonomyKind.Author, "未使用");
        var store = new EfBatchMetadataStore(database.CreateContextFactory());

        BatchMetadataResult result = await store.ApplyAsync(
            RemoveExistingAuthor(ids, absent),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.ChangedCount);
        Assert.Equal(3, result.UnchangedCount);
        Assert.All(await database.LoadMetadataVersionsAsync(ids), version =>
            Assert.Equal(0, version));
    }

    private static BatchMetadataWrite AddNewAuthor(
        IReadOnlyList<Guid> ids,
        string name,
        long expectedVersion = 0) =>
        new(
            Select(ids, expectedVersion),
            BatchMetadataMode.Add,
            BatchMetadataFieldOperation.Add,
            [new(TaxonomyKind.Author, null, TaxonomyName.Create(name))],
            BatchMetadataFieldOperation.Unchanged,
            null,
            BatchMetadataFieldOperation.Unchanged,
            []);

    private static BatchMetadataWrite AddExistingTag(
        IReadOnlyList<Guid> ids,
        Guid tagId) =>
        new(
            Select(ids, 0),
            BatchMetadataMode.Add,
            BatchMetadataFieldOperation.Unchanged,
            [],
            BatchMetadataFieldOperation.Unchanged,
            null,
            BatchMetadataFieldOperation.Add,
            [new(TaxonomyKind.Tag, tagId, null)]);

    private static BatchMetadataWrite RemoveExistingAuthor(
        IReadOnlyList<Guid> ids,
        Guid authorId) =>
        new(
            Select(ids, 0),
            BatchMetadataMode.Remove,
            BatchMetadataFieldOperation.Remove,
            [new(TaxonomyKind.Author, authorId, null)],
            BatchMetadataFieldOperation.Unchanged,
            null,
            BatchMetadataFieldOperation.Unchanged,
            []);

    private static IReadOnlyList<InboxSelectionEntry> Select(
        IReadOnlyList<Guid> ids,
        long expectedVersion) =>
        ids.Select((id, ordinal) =>
            new InboxSelectionEntry(id, expectedVersion, ordinal)).ToArray();
}
