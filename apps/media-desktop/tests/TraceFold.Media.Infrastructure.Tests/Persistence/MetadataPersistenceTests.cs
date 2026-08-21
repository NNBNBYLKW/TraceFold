using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class MetadataPersistenceTests
{
    private static readonly DateTimeOffset Instant =
        DateTimeOffset.Parse("2026-08-22T10:00:00Z");

    [Fact]
    public async Task Inbox_metadata_round_trips_relations_and_version_across_contexts()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid inboxItemId = await CreateReadyInboxAsync(database);
        var taxonomy = new EfTaxonomyStore(database.CreateContextFactory());
        TaxonomyTermDto author1 = await taxonomy.CreateOrGetAsync(
            TaxonomyKind.Author, TaxonomyName.Create("Alice"), CancellationToken.None);
        TaxonomyTermDto author2 = await taxonomy.CreateOrGetAsync(
            TaxonomyKind.Author, TaxonomyName.Create("Bob"), CancellationToken.None);
        TaxonomyTermDto series = await taxonomy.CreateOrGetAsync(
            TaxonomyKind.Series, TaxonomyName.Create("旅行"), CancellationToken.None);
        TaxonomyTermDto tag = await taxonomy.CreateOrGetAsync(
            TaxonomyKind.Tag, TaxonomyName.Create("夜景"), CancellationToken.None);
        var service = new MetadataEditorService(
            new EfMetadataStore(database.CreateContextFactory()),
            taxonomy);

        MetadataSaveResult saved = await service.SaveAsync(
            new MetadataOwner(MetadataOwnerKind.InboxItem, inboxItemId),
            new MetadataUpdate(
                "杭州",
                [author1.Id, author2.Id],
                series.Id,
                [tag.Id],
                0),
            CancellationToken.None);
        MetadataDocument? loaded = await new EfMetadataStore(database.CreateContextFactory())
            .LoadAsync(
                new MetadataOwner(MetadataOwnerKind.InboxItem, inboxItemId),
                CancellationToken.None);

        Assert.True(saved.Succeeded);
        Assert.Equal(1, saved.CurrentVersion);
        Assert.NotNull(loaded);
        Assert.Equal("杭州", loaded.DisplayName);
        Assert.Equal([author1.Id, author2.Id], loaded.Authors.Select(item => item.Id));
        Assert.Equal(series.Id, loaded.Series!.Id);
        Assert.Equal([tag.Id], loaded.Tags.Select(item => item.Id));
        Assert.Equal(1, loaded.Version);
    }

    [Fact]
    public async Task Metadata_validation_rejects_wrong_taxonomy_kind_before_save()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid inboxItemId = await CreateReadyInboxAsync(database);
        var taxonomy = new EfTaxonomyStore(database.CreateContextFactory());
        TaxonomyTermDto author = await taxonomy.CreateOrGetAsync(
            TaxonomyKind.Author, TaxonomyName.Create("Alice"), CancellationToken.None);
        var service = new MetadataEditorService(
            new EfMetadataStore(database.CreateContextFactory()),
            taxonomy);

        MetadataSaveResult result = await service.SaveAsync(
            new MetadataOwner(MetadataOwnerKind.InboxItem, inboxItemId),
            new MetadataUpdate(null, [], null, [author.Id], 0),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("metadata.taxonomy_kind_mismatch", result.ErrorCode);
    }

    [Fact]
    public async Task Referenced_taxonomy_term_cannot_be_deleted()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid inboxItemId = await CreateReadyInboxAsync(database);
        var taxonomy = new EfTaxonomyStore(database.CreateContextFactory());
        TaxonomyTermDto author = await taxonomy.CreateOrGetAsync(
            TaxonomyKind.Author, TaxonomyName.Create("Alice"), CancellationToken.None);
        var metadata = new MetadataEditorService(
            new EfMetadataStore(database.CreateContextFactory()),
            taxonomy);
        await metadata.SaveAsync(
            new MetadataOwner(MetadataOwnerKind.InboxItem, inboxItemId),
            new MetadataUpdate(null, [author.Id], null, [], 0),
            CancellationToken.None);

        TaxonomyMutationResult result = await taxonomy.DeleteAsync(
            author.Id,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("taxonomy.in_use", result.ErrorCode);
    }

    [Fact]
    public async Task Library_search_matches_display_name_and_falls_back_to_actual_name()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid assetId = Guid.NewGuid();
        await database.ExecuteSqlAsync($"""
            INSERT INTO media_assets
                (id, media_kind, actual_filename, relative_path,
                 normalized_relative_path, display_name, size, sha256,
                 metadata_version, created_at_utc, updated_at_utc)
            VALUES
                ('{assetId}', 'image', 'photo.jpg', 'photo.jpg', 'PHOTO.JPG',
                 '杭州夜景', 3, '{new string('A', 64)}', 0,
                 '2026-08-22T10:00:00+00:00', '2026-08-22T10:00:00+00:00')
            """);
        var library = new LibraryQueryService(
            new EfLibraryQueryStore(database.CreateContextFactory()));

        IReadOnlyList<LibraryAssetDto> result = await library.SearchAsync(
            "杭州",
            CancellationToken.None);

        LibraryAssetDto asset = Assert.Single(result);
        Assert.Equal(assetId, asset.Id);
        Assert.Equal("photo.jpg", asset.ActualFilename);
        Assert.Equal("杭州夜景", asset.DisplayName);
    }

    private static async Task<Guid> CreateReadyInboxAsync(TestMediaDatabase database)
    {
        var store = new EfInboxScanStore(database.CreateContextFactory());
        ObservedFile source = new(
            "C:/inbox/photo.jpg",
            "photo.jpg",
            ".jpg",
            new FileFingerprint(3, Instant),
            Instant);
        var reconciliation = new ScanReconciliation(
            "C:/inbox",
            Instant,
            [
                new CandidateGroup(
                    "direct_media",
                    source.FullPath.ToUpperInvariant(),
                    MediaKind.Image,
                    CandidateDisposition.Direct,
                    [source],
                    null)
            ],
            new HashSet<string>(
                [Path.GetFullPath(source.FullPath).ToUpperInvariant()],
                StringComparer.OrdinalIgnoreCase));
        await store.ReconcileAsync(reconciliation, CancellationToken.None);
        await store.ReconcileAsync(
            reconciliation with { ObservedAtUtc = Instant.AddSeconds(2) },
            CancellationToken.None);
        return (await database.ReadSingleInboxAsync()).Id;
    }
}
