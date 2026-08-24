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
            IngestExecutionPlan.Create(candidate.ActualFilename),
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
            IngestExecutionPlan.Create(candidate.ActualFilename),
            TestContext.Current.CancellationToken);
        IngestOperationRecord second = await store.CreateAsync(
            preparation,
            IngestExecutionPlan.Create(candidate.ActualFilename),
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
        await InsertAssetAsync(
            database,
            Guid.NewGuid(),
            "photo.jpg",
            new string('B', 64));
        var store = new EfIngestStore(database.CreateContextFactory());
        IngestCandidate candidate = (await store.LoadCandidateAsync(
            itemId,
            TestContext.Current.CancellationToken))!;
        IngestOperationRecord operation = await store.CreateAsync(
            Preparation(candidate, hash),
            IngestExecutionPlan.Create(candidate.ActualFilename),
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
            IngestExecutionPlan.Create(candidate.ActualFilename),
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
    public async Task Cleanup_retry_due_time_and_startup_reactivation_are_persisted()
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
            IngestExecutionPlan.Create(candidate.ActualFilename),
            TestContext.Current.CancellationToken);
        var committed = new CommittedFile(
            "C:/library/photo.jpg", "photo.jpg", 3, hash);
        await store.MarkStagedAsync(
            operation.Id,
            new StagedFile("C:/work/payload", 3, hash),
            TestContext.Current.CancellationToken);
        await store.MarkFileCommittedAsync(
            operation.Id,
            committed,
            TestContext.Current.CancellationToken);
        await store.CommitAssetAsync(
            operation.Id,
            committed,
            TestContext.Current.CancellationToken);
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-24T09:00:00Z");
        DateTimeOffset firstDue = now.AddSeconds(30);
        await store.ScheduleCleanupRetryAsync(
            operation.Id,
            "ingest.cleanup_failed",
            1,
            firstDue,
            TestContext.Current.CancellationToken);

        Assert.Empty(await store.LoadDueCleanupAsync(
            now,
            TestContext.Current.CancellationToken));
        Assert.Equal(firstDue, await store.FindNextCleanupDueAsync(
            TestContext.Current.CancellationToken));
        IngestOperationRecord due = Assert.Single(await store.LoadDueCleanupAsync(
            firstDue,
            TestContext.Current.CancellationToken));
        Assert.Equal(1, due.CleanupAttemptCount);
        await store.ResetCleanupRetryAsync(
            operation.Id,
            TestContext.Current.CancellationToken);
        Assert.Null(await store.FindNextCleanupDueAsync(
            TestContext.Current.CancellationToken));
        await store.ScheduleCleanupRetryAsync(
            operation.Id,
            "ingest.cleanup_failed",
            5,
            null,
            TestContext.Current.CancellationToken);
        Assert.Empty(await store.LoadDueCleanupAsync(
            now.AddDays(1),
            TestContext.Current.CancellationToken));

        DateTimeOffset restart = now.AddDays(2);
        await new EfIngestStore(database.CreateContextFactory())
            .ReactivateCleanupForStartupAsync(
                restart,
                TestContext.Current.CancellationToken);

        IngestOperationRecord reactivated = Assert.Single(
            await store.LoadDueCleanupAsync(
                restart,
                TestContext.Current.CancellationToken));
        Assert.Equal(5, reactivated.CleanupAttemptCount);
        Assert.Equal(restart, reactivated.NextCleanupAttemptAtUtc);
    }

    [Fact]
    public async Task Create_persists_ordered_cleanup_manifest_and_entry_completion()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid itemId = await CreateReadyInboxWithMetadataAsync(database);
        var store = new EfIngestStore(database.CreateContextFactory());
        IngestCandidate candidate = (await store.LoadCandidateAsync(
            itemId,
            TestContext.Current.CancellationToken))!;
        string hash = new('A', 64);
        var input = new PreparedIngestInput(
            IngestCleanupKind.ProcessingOutput,
            "C:/work/output.mp4",
            3,
            Instant,
            hash,
            [
                new IngestCleanupFile(
                    IngestCleanupKind.InboxSource, "C:/inbox/a.mp4", 3, hash),
                new IngestCleanupFile(
                    IngestCleanupKind.InboxSource, "C:/inbox/a.m4a", 2, new string('B', 64)),
                new IngestCleanupFile(
                    IngestCleanupKind.ProcessingOutput, "C:/work/output.mp4", 3, hash)
            ]);
        IngestOperationRecord operation = await store.CreateAsync(
            IngestPreparation.From(candidate, input),
            IngestExecutionPlan.Create(candidate.ActualFilename),
            TestContext.Current.CancellationToken);

        IReadOnlyList<IngestCleanupEntryRecord> entries =
            await store.LoadCleanupEntriesAsync(
                operation.Id,
                TestContext.Current.CancellationToken);
        await store.MarkCleanupEntryCompletedAsync(
            operation.Id,
            entries[1].Id,
            TestContext.Current.CancellationToken);
        IReadOnlyList<IngestCleanupEntryRecord> reloaded =
            await store.LoadCleanupEntriesAsync(
                operation.Id,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            [IngestCleanupKind.InboxSource, IngestCleanupKind.InboxSource,
             IngestCleanupKind.ProcessingOutput],
            reloaded.Select(entry => entry.File.Kind));
        Assert.False(reloaded[0].Completed);
        Assert.True(reloaded[1].Completed);
        Assert.False(reloaded[2].Completed);
    }

    [Fact]
    public async Task Replacement_create_persists_plan_old_asset_and_backup_cleanup()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid itemId = await CreateReadyInboxWithMetadataAsync(database);
        Guid assetId = Guid.NewGuid();
        string oldHash = new('B', 64);
        await InsertAssetAsync(database, assetId, "photo.jpg", oldHash);
        ConflictCaseRecord conflict = await OpenConflictAsync(
            database, itemId, assetId, isRegistered: true, oldHash);
        var store = new EfIngestStore(database.CreateContextFactory());
        IngestCandidate candidate = (await store.LoadCandidateAsync(
            itemId, TestContext.Current.CancellationToken))!;
        LibraryFileSnapshot target = Target("photo.jpg", oldHash);
        IngestExecutionPlan plan = IngestExecutionPlan.ReplaceRegistered(
            conflict.Id, assetId, 0, target);

        IngestOperationRecord operation = await store.CreateAsync(
            Preparation(candidate, new string('A', 64)),
            plan,
            TestContext.Current.CancellationToken);
        var backup = new ReplacementBackup(
            $"C:/library/.tracefold/recovery/{operation.Id:N}/original.jpg",
            $".tracefold/recovery/{operation.Id:N}/original.jpg",
            3,
            oldHash);
        await store.MarkStagedAsync(
            operation.Id,
            new StagedFile("C:/work/payload", 3, new string('A', 64)),
            TestContext.Current.CancellationToken);
        await store.MarkBackupCreatedAsync(
            operation.Id,
            backup,
            TestContext.Current.CancellationToken);
        IngestOperationRecord loaded = (await store.FindActiveAsync(
            itemId,
            TestContext.Current.CancellationToken))!;
        IReadOnlyList<IngestCleanupEntryRecord> cleanup =
            await store.LoadCleanupEntriesAsync(
                operation.Id,
                TestContext.Current.CancellationToken);

        Assert.Equal(IngestDisposition.ReplaceRegistered, loaded.Disposition);
        Assert.Equal(conflict.Id, loaded.ConflictCaseId);
        Assert.Equal(assetId, loaded.ReplacementAssetId);
        Assert.Equal(IngestBackupState.Created, loaded.BackupState);
        Assert.Equal(backup.RelativePath, loaded.BackupRelativePath);
        Assert.Equal(
            IngestCleanupKind.ReplacementBackup,
            cleanup[^1].File.Kind);
        Assert.True(await database.ScalarAsync<long>(
            "SELECT length(old_asset_snapshot_json) FROM ingest_operations") > 0);
    }

    [Fact]
    public async Task Registered_replacement_preserves_identity_and_resolves_case_atomically()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid itemId = await CreateReadyInboxWithMetadataAsync(database);
        Guid assetId = Guid.NewGuid();
        string oldHash = new('B', 64);
        string newHash = new('A', 64);
        await InsertAssetAsync(database, assetId, "photo.jpg", oldHash);
        await LinkAssetToDraftTaxonomyAsync(database, assetId);
        ConflictCaseRecord conflict = await OpenConflictAsync(
            database, itemId, assetId, isRegistered: true, oldHash);
        var store = new EfIngestStore(database.CreateContextFactory());
        IngestCandidate candidate = (await store.LoadCandidateAsync(
            itemId, TestContext.Current.CancellationToken))!;
        IngestOperationRecord operation = await store.CreateAsync(
            Preparation(candidate, newHash),
            IngestExecutionPlan.ReplaceRegistered(
                conflict.Id,
                assetId,
                0,
                Target("photo.jpg", oldHash)),
            TestContext.Current.CancellationToken);
        var committed = new CommittedFile(
            "C:/library/photo.jpg", "photo.jpg", 3, newHash);
        await store.MarkStagedAsync(
            operation.Id,
            new StagedFile("C:/work/payload", 3, newHash),
            TestContext.Current.CancellationToken);
        await store.MarkBackupCreatedAsync(
            operation.Id,
            new ReplacementBackup(
                $"C:/library/.tracefold/recovery/{operation.Id:N}/original.jpg",
                $".tracefold/recovery/{operation.Id:N}/original.jpg",
                3,
                oldHash),
            TestContext.Current.CancellationToken);
        await store.MarkFileCommittedAsync(
            operation.Id,
            committed,
            TestContext.Current.CancellationToken);

        Assert.True(await store.ReplacementDatabaseMatchesExpectedAsync(
            operation.Id,
            TestContext.Current.CancellationToken));
        Guid first = await store.CommitAssetAsync(
            operation.Id,
            committed,
            TestContext.Current.CancellationToken);
        Guid replay = await store.CommitAssetAsync(
            operation.Id,
            committed,
            TestContext.Current.CancellationToken);

        Assert.Equal(assetId, first);
        Assert.Equal(first, replay);
        Assert.Equal("photo.jpg", await database.ScalarAsync<string>(
            "SELECT actual_filename FROM media_assets"));
        Assert.Equal("photo.jpg", await database.ScalarAsync<string>(
            "SELECT relative_path FROM media_assets"));
        Assert.Equal(newHash, await database.ScalarAsync<string>(
            "SELECT sha256 FROM media_assets"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT metadata_version FROM media_assets"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT count(*) FROM media_asset_authors"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT count(*) FROM media_asset_tags"));
        Assert.Equal("resolved", await database.ScalarAsync<string>(
            "SELECT status FROM conflict_cases"));
        Assert.Equal(
            operation.Id.ToString().ToUpperInvariant(),
            (await database.ScalarAsync<string>(
                "SELECT operation_id FROM conflict_cases")).ToUpperInvariant());
        Assert.False(await store.ReplacementDatabaseMatchesExpectedAsync(
            operation.Id,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unmanaged_replacement_creates_one_new_asset_at_selected_path()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid itemId = await CreateReadyInboxWithMetadataAsync(database);
        string oldHash = new('B', 64);
        string newHash = new('A', 64);
        ConflictCaseRecord conflict = await OpenConflictAsync(
            database,
            itemId,
            assetId: null,
            isRegistered: false,
            oldHash);
        var store = new EfIngestStore(database.CreateContextFactory());
        IngestCandidate candidate = (await store.LoadCandidateAsync(
            itemId, TestContext.Current.CancellationToken))!;
        IngestOperationRecord operation = await store.CreateAsync(
            Preparation(candidate, newHash),
            IngestExecutionPlan.ReplaceUnregistered(
                conflict.Id,
                Target("photo.jpg", oldHash)),
            TestContext.Current.CancellationToken);
        var committed = new CommittedFile(
            "C:/library/photo.jpg", "photo.jpg", 3, newHash);
        await store.MarkStagedAsync(
            operation.Id,
            new StagedFile("C:/work/payload", 3, newHash),
            TestContext.Current.CancellationToken);
        await store.MarkBackupCreatedAsync(
            operation.Id,
            new ReplacementBackup(
                $"C:/library/.tracefold/recovery/{operation.Id:N}/original.jpg",
                $".tracefold/recovery/{operation.Id:N}/original.jpg",
                3,
                oldHash),
            TestContext.Current.CancellationToken);
        await store.MarkFileCommittedAsync(
            operation.Id,
            committed,
            TestContext.Current.CancellationToken);

        Guid assetId = await store.CommitAssetAsync(
            operation.Id,
            committed,
            TestContext.Current.CancellationToken);

        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT count(*) FROM media_assets"));
        Assert.Equal(
            assetId.ToString().ToUpperInvariant(),
            (await database.ScalarAsync<string>(
                "SELECT id FROM media_assets")).ToUpperInvariant());
        Assert.Equal("photo.jpg", await database.ScalarAsync<string>(
            "SELECT actual_filename FROM media_assets"));
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

    private static async Task<ConflictCaseRecord> OpenConflictAsync(
        TestMediaDatabase database,
        Guid itemId,
        Guid? assetId,
        bool isRegistered,
        string oldHash)
    {
        Guid matchId = Guid.NewGuid();
        var match = new ConflictMatchRecord(
            matchId,
            ConflictReason.Name,
            isRegistered,
            assetId,
            isRegistered ? 0 : null,
            "photo.jpg",
            "photo.jpg",
            null,
            MediaKind.Image,
            3,
            Instant,
            oldHash,
            new ConflictMetadataSnapshot(null, [], null, []),
            new ConflictTechnicalMetadata("jpg", null, null, null));
        var cases = new EfConflictCaseStore(
            database.CreateContextFactory(),
            TimeProvider.System);
        ConflictCaseRecord conflict = await cases.ReplaceOpenAsync(
            itemId,
            new ConflictInputSnapshot(
                "C:/inbox/photo.jpg",
                3,
                Instant,
                new string('A', 64),
                "photo.jpg",
                1),
            [match],
            TestContext.Current.CancellationToken);
        await cases.ChooseAsync(
            conflict.Id,
            ConflictResolution.Replace,
            matchId,
            TestContext.Current.CancellationToken);
        return (await cases.LoadAsync(
            conflict.Id,
            TestContext.Current.CancellationToken))!;
    }

    private static LibraryFileSnapshot Target(
        string filename,
        string hash) =>
        new(
            $"C:/library/{filename}",
            filename,
            3,
            Instant,
            hash);

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

    private static async Task LinkAssetToDraftTaxonomyAsync(
        TestMediaDatabase database,
        Guid assetId)
    {
        string authorId = await database.ScalarAsync<string>(
            "SELECT author_id FROM inbox_draft_authors");
        string tagId = await database.ScalarAsync<string>(
            "SELECT tag_id FROM inbox_draft_tags");
        await database.ExecuteSqlAsync($"""
            INSERT INTO media_asset_authors (media_asset_id, author_id, ordinal)
            VALUES ('{assetId.ToString().ToUpperInvariant()}', '{authorId}', 0);
            INSERT INTO media_asset_tags (media_asset_id, tag_id, ordinal)
            VALUES ('{assetId.ToString().ToUpperInvariant()}', '{tagId}', 0);
            """);
    }
}
