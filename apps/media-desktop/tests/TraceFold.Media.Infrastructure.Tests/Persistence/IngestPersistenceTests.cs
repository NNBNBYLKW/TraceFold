using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class IngestPersistenceTests
{
    private static readonly DateTimeOffset Instant =
        DateTimeOffset.Parse("2026-08-22T10:00:00Z");

    [Fact]
    public async Task CommitAssetAsync_atomically_writes_asset_metadata_and_completion()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid itemId = await CreateReadyInboxWithMetadataAsync(database);
        var store = new EfIngestStore(database.CreateContextFactory());
        IngestCandidate candidate = (await store.LoadCandidateAsync(
            itemId,
            TestContext.Current.CancellationToken))!;
        string hash = new string('A', 64);
        IngestOperationRecord operation = await store.CreateAsync(
            Preparation(candidate, hash),
            TestContext.Current.CancellationToken);
        var staged = new StagedFile("C:/work/payload", 3, hash);
        var committed = new CommittedFile(
            "C:/library/photo.jpg",
            "photo.jpg",
            3,
            hash);
        await store.MarkStagedAsync(
            operation.Id,
            staged,
            TestContext.Current.CancellationToken);
        await store.MarkFileCommittedAsync(
            operation.Id,
            committed,
            TestContext.Current.CancellationToken);

        Guid assetId = await store.CommitAssetAsync(
            operation.Id,
            committed,
            TestContext.Current.CancellationToken);
        PersistedIngestSnapshot snapshot = await database.ReadSingleIngestAsync();

        Assert.Equal(assetId, snapshot.AssetId);
        Assert.Equal("databasecommitted", snapshot.OperationStage);
        Assert.Equal("completed", snapshot.InboxState);
        Assert.Null(snapshot.ActivePathKey);
        Assert.Equal("display", snapshot.DisplayName);
        Assert.Equal(1, snapshot.AuthorCount);
        Assert.Equal(1, snapshot.TagCount);
        Assert.NotNull(snapshot.SeriesId);
    }

    [Fact]
    public async Task Repeated_create_returns_one_active_operation()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid itemId = await CreateReadyInboxWithMetadataAsync(database);
        var store = new EfIngestStore(database.CreateContextFactory());
        IngestCandidate candidate = (await store.LoadCandidateAsync(
            itemId,
            TestContext.Current.CancellationToken))!;
        IngestPreparation preparation = Preparation(candidate, new string('A', 64));

        IngestOperationRecord first = await store.CreateAsync(
            preparation,
            TestContext.Current.CancellationToken);
        IngestOperationRecord second = await store.CreateAsync(
            preparation,
            TestContext.Current.CancellationToken);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await database.CountIngestOperationsAsync());
    }

    [Fact]
    public async Task Failed_asset_insert_rolls_back_to_recoverable_file_committed_state()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid itemId = await CreateReadyInboxWithMetadataAsync(database);
        string hash = new string('A', 64);
        await InsertAssetAsync(database, Guid.NewGuid(), "existing.jpg", hash);
        var store = new EfIngestStore(database.CreateContextFactory());
        IngestCandidate candidate = (await store.LoadCandidateAsync(
            itemId,
            TestContext.Current.CancellationToken))!;
        IngestOperationRecord operation = await store.CreateAsync(
            Preparation(candidate, hash),
            TestContext.Current.CancellationToken);
        var committed = new CommittedFile(
            "C:/library/photo.jpg",
            "photo.jpg",
            3,
            hash);
        await store.MarkStagedAsync(
            operation.Id,
            new StagedFile("C:/work/payload", 3, hash),
            TestContext.Current.CancellationToken);
        await store.MarkFileCommittedAsync(
            operation.Id,
            committed,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<DbUpdateException>(() => store.CommitAssetAsync(
            operation.Id,
            committed,
            TestContext.Current.CancellationToken));
        PersistedIngestSnapshot snapshot = await database.ReadSingleIngestAsync();

        Assert.Equal("filecommitted", snapshot.OperationStage);
        Assert.Equal("ingesting", snapshot.InboxState);
        Assert.NotNull(snapshot.ActivePathKey);
    }

    [Fact]
    public async Task CleanupPending_and_error_survive_a_new_store_instance()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid itemId = await CreateReadyInboxWithMetadataAsync(database);
        var firstStore = new EfIngestStore(database.CreateContextFactory());
        IngestCandidate candidate = (await firstStore.LoadCandidateAsync(
            itemId,
            TestContext.Current.CancellationToken))!;
        string hash = new string('A', 64);
        IngestOperationRecord operation = await firstStore.CreateAsync(
            Preparation(candidate, hash),
            TestContext.Current.CancellationToken);
        var committed = new CommittedFile(
            "C:/library/photo.jpg",
            "photo.jpg",
            3,
            hash);
        await firstStore.MarkStagedAsync(
            operation.Id,
            new StagedFile("C:/work/payload", 3, hash),
            TestContext.Current.CancellationToken);
        await firstStore.MarkFileCommittedAsync(
            operation.Id,
            committed,
            TestContext.Current.CancellationToken);
        await firstStore.CommitAssetAsync(
            operation.Id,
            committed,
            TestContext.Current.CancellationToken);
        await firstStore.MarkCleanupPendingAsync(
            operation.Id,
            "ingest.cleanup_failed",
            TestContext.Current.CancellationToken);

        IngestOperationRecord? loaded = await new EfIngestStore(
                database.CreateContextFactory())
            .FindActiveAsync(itemId, TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(IngestOperationStage.CleanupPending, loaded.Stage);
        Assert.Equal("ingest.cleanup_failed", loaded.ErrorCode);
    }

    [Fact]
    public async Task Registered_hash_conflict_is_detected_before_staging()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        string hash = new string('B', 64);
        await InsertAssetAsync(database, Guid.NewGuid(), "existing.jpg", hash);
        var store = new EfIngestStore(database.CreateContextFactory());

        ConflictCheck result = await store.CheckConflictsAsync(
            "different.jpg",
            hash,
            TestContext.Current.CancellationToken);

        Assert.Equal(ConflictKind.RegisteredContent, result.Kind);
    }

    private static async Task<Guid> CreateReadyInboxWithMetadataAsync(
        TestMediaDatabase database)
    {
        string path = "C:/inbox/photo.jpg";
        var source = new ObservedFile(
            path,
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
                    path.ToUpperInvariant(),
                    MediaKind.Image,
                    CandidateDisposition.Direct,
                    [source],
                    null)
            ],
            new HashSet<string>(
                [Path.GetFullPath(path).ToUpperInvariant()],
                StringComparer.OrdinalIgnoreCase));
        var scanStore = new EfInboxScanStore(database.CreateContextFactory());
        await scanStore.ReconcileAsync(
            reconciliation,
            TestContext.Current.CancellationToken);
        await scanStore.ReconcileAsync(
            reconciliation with { ObservedAtUtc = Instant.AddSeconds(2) },
            TestContext.Current.CancellationToken);
        Guid itemId = (await database.ReadSingleInboxAsync()).Id;
        var taxonomy = new EfTaxonomyStore(database.CreateContextFactory());
        TaxonomyTermDto author = await taxonomy.CreateOrGetAsync(
            TaxonomyKind.Author,
            TaxonomyName.Create("author"),
            TestContext.Current.CancellationToken);
        TaxonomyTermDto series = await taxonomy.CreateOrGetAsync(
            TaxonomyKind.Series,
            TaxonomyName.Create("series"),
            TestContext.Current.CancellationToken);
        TaxonomyTermDto tag = await taxonomy.CreateOrGetAsync(
            TaxonomyKind.Tag,
            TaxonomyName.Create("tag"),
            TestContext.Current.CancellationToken);
        var metadata = new MetadataEditorService(
            new EfMetadataStore(database.CreateContextFactory()),
            taxonomy);
        await metadata.SaveAsync(
            new MetadataOwner(MetadataOwnerKind.InboxItem, itemId),
            new MetadataUpdate("display", [author.Id], series.Id, [tag.Id], 0),
            TestContext.Current.CancellationToken);
        return itemId;
    }

    private static IngestPreparation Preparation(
        IngestCandidate candidate,
        string hash) =>
        new(
            candidate.InboxItemId,
            candidate.MediaKind,
            candidate.Sources[0].FullPath,
            candidate.Sources[0].Size,
            candidate.Sources[0].LastWriteTimeUtc,
            hash,
            candidate.ActualFilename,
            candidate.Metadata.DisplayName,
            candidate.Metadata.AuthorIds,
            candidate.Metadata.SeriesId,
            candidate.Metadata.TagIds,
            candidate.MetadataVersion);

    private static Task InsertAssetAsync(
        TestMediaDatabase database,
        Guid id,
        string filename,
        string hash) =>
        database.ExecuteSqlAsync($"""
            INSERT INTO media_assets
                (id, media_kind, actual_filename, relative_path,
                 normalized_relative_path, size, sha256, metadata_version,
                 created_at_utc, updated_at_utc)
            VALUES
                ('{id.ToString().ToUpperInvariant()}', 'image', '{filename}', '{filename}',
                 '{filename.ToUpperInvariant()}', 3, '{hash}', 0,
                 '2026-08-22T10:00:00+00:00', '2026-08-22T10:00:00+00:00')
            """);
}
