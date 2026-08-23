using Microsoft.Data.Sqlite;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class Stage4MigrationTests
{
    private const string Stage3Migration =
        "20260823120000_Stage3ProcessingPipeline";
    private const string SharedHash =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string OldHash =
        "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Fact]
    public async Task Stage4_allows_duplicate_hashes_but_keeps_paths_unique()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync(Stage3Migration);
        await database.MigrateAsync();

        await InsertAssetAsync(database, "one.jpg", SharedHash);
        await InsertAssetAsync(database, "two.jpg", SharedHash);

        Assert.Equal(2, await database.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM media_assets WHERE sha256 = '{SharedHash}'"));
        await Assert.ThrowsAsync<SqliteException>(() =>
            InsertAssetAsync(database, "one.jpg", OldHash));
    }

    [Fact]
    public async Task Stage4_adds_conflict_backup_and_retry_columns()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();

        IReadOnlySet<string> tables = await database.ReadTableNamesAsync();
        Assert.Contains("conflict_cases", tables);
        Assert.Contains("conflict_matches", tables);

        IReadOnlySet<string> operations =
            await database.ReadColumnNamesAsync("ingest_operations");
        Assert.Contains("disposition", operations);
        Assert.Contains("asset_id", operations);
        Assert.Contains("backup_state", operations);
        Assert.Contains("next_cleanup_attempt_at_utc", operations);

        IReadOnlySet<string> cleanup =
            await database.ReadColumnNamesAsync("ingest_cleanup_entries");
        Assert.Contains("last_attempt_at_utc", cleanup);
        Assert.Contains("last_error_code", cleanup);
    }

    [Fact]
    public async Task Stage4_preserves_stage3_assets_taxonomy_processing_and_cleanup_rows()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync(Stage3Migration);
        SeedIds ids = await SeedStage3GraphAsync(database);

        await database.MigrateAsync();

        Assert.Equal(ids.AssetId.ToString(), await database.ScalarAsync<string>(
            "SELECT id FROM media_assets"));
        Assert.Equal("archive/photo.jpg", await database.ScalarAsync<string>(
            "SELECT relative_path FROM media_assets"));
        Assert.Equal("Original display", await database.ScalarAsync<string>(
            "SELECT display_name FROM media_assets"));
        Assert.Equal(ids.SeriesId.ToString(), await database.ScalarAsync<string>(
            "SELECT series_id FROM media_assets"));
        Assert.Equal(ids.AuthorId.ToString(), await database.ScalarAsync<string>(
            "SELECT author_id FROM media_asset_authors WHERE ordinal = 0"));
        Assert.Equal(ids.TagId.ToString(), await database.ScalarAsync<string>(
            "SELECT tag_id FROM media_asset_tags WHERE ordinal = 0"));
        Assert.Equal(ids.PlanId.ToString(), await database.ScalarAsync<string>(
            "SELECT id FROM processing_plans"));
        Assert.Equal(ids.JobId.ToString(), await database.ScalarAsync<string>(
            "SELECT id FROM processing_jobs"));
        Assert.Equal("processed/paired.mp4", await database.ScalarAsync<string>(
            "SELECT relative_path FROM processed_outputs"));
        Assert.Equal("T:/inbox/paired.mp4", await database.ScalarAsync<string>(
            "SELECT controlled_path FROM ingest_cleanup_entries"));
        Assert.Equal(1, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM ingest_cleanup_entries"));
        Assert.Equal("create", await database.ScalarAsync<string>(
            "SELECT disposition FROM ingest_operations"));
        Assert.Equal("notrequired", await database.ScalarAsync<string>(
            "SELECT backup_state FROM ingest_operations"));
        Assert.Equal(0, await database.ScalarAsync<long>(
            "SELECT cleanup_attempt_count FROM ingest_operations"));
        Assert.Equal(1, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM ingest_operations WHERE asset_id IS NULL"));
    }

    private static Task InsertAssetAsync(
        TestMediaDatabase database,
        string relativePath,
        string sha256) =>
        database.ExecuteSqlAsync($"""
            INSERT INTO media_assets
                (id, media_kind, actual_filename, relative_path,
                 normalized_relative_path, size, sha256, metadata_version,
                 created_at_utc, updated_at_utc)
            VALUES
                ('{Guid.NewGuid()}', 'image', '{relativePath}', '{relativePath}',
                 '{relativePath.ToUpperInvariant()}', 3, '{sha256}', 0,
                 '2026-08-23T00:00:00+00:00',
                 '2026-08-23T00:00:00+00:00')
            """);

    private static async Task<SeedIds> SeedStage3GraphAsync(
        TestMediaDatabase database)
    {
        var ids = new SeedIds(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());

        await database.ExecuteSqlAsync(
            $"""
            INSERT INTO inbox_items
                (id, state, media_kind, actual_filename, grouping_rule,
                 is_retryable, created_at_utc, updated_at_utc)
            VALUES
                ('{ids.InboxItemId}', 'completed', 'video', 'paired.mp4',
                 'mp4_m4a', 0, '2026-08-23T00:00:00+00:00',
                 '2026-08-23T00:00:00+00:00')
            """,
            TaxonomySql(ids.AuthorId, "author", "Alice"),
            TaxonomySql(ids.SeriesId, "series", "Series One"),
            TaxonomySql(ids.TagId, "tag", "Nature"),
            $"""
            INSERT INTO source_files
                (id, inbox_item_id, normalized_path, active_path_key, filename,
                 extension, size, last_write_time_utc, first_observed_at_utc,
                 stable_observation_count, sha256, is_present, is_primary)
            VALUES
                ('{ids.SourceId}', '{ids.InboxItemId}', 'T:/INBOX/PAIRED.MP4',
                 NULL, 'paired.mp4', '.mp4', 5,
                 '2026-08-23T00:00:00+00:00',
                 '2026-08-23T00:00:00+00:00', 2, '{OldHash}', 0, 1)
            """,
            $"""
            INSERT INTO metadata_drafts
                (inbox_item_id, display_name, series_id, version, updated_at_utc)
            VALUES
                ('{ids.InboxItemId}', 'Draft display', '{ids.SeriesId}', 4,
                 '2026-08-23T00:00:00+00:00')
            """,
            $"""
            INSERT INTO media_assets
                (id, media_kind, actual_filename, relative_path,
                 normalized_relative_path, display_name, size, sha256,
                 series_id, metadata_version, created_at_utc, updated_at_utc)
            VALUES
                ('{ids.AssetId}', 'video', 'photo.jpg', 'archive/photo.jpg',
                 'ARCHIVE/PHOTO.JPG', 'Original display', 5, '{OldHash}',
                 '{ids.SeriesId}', 8, '2026-08-23T00:00:00+00:00',
                 '2026-08-23T00:00:00+00:00')
            """,
            $"""
            INSERT INTO media_asset_authors (media_asset_id, author_id, ordinal)
            VALUES ('{ids.AssetId}', '{ids.AuthorId}', 0)
            """,
            $"""
            INSERT INTO media_asset_tags (media_asset_id, tag_id, ordinal)
            VALUES ('{ids.AssetId}', '{ids.TagId}', 0)
            """,
            $"""
            INSERT INTO ingest_operations
                (id, inbox_item_id, active_inbox_item_id, stage, source_path,
                 source_size, source_last_write_time_utc, source_sha256,
                 target_filename, metadata_snapshot_json, created_at_utc,
                 updated_at_utc)
            VALUES
                ('{ids.OperationId}', '{ids.InboxItemId}', NULL, 'completed',
                 'T:/inbox/paired.mp4', 5, '2026-08-23T00:00:00+00:00',
                 '{OldHash}', 'paired.mp4', '[]',
                 '2026-08-23T00:00:00+00:00',
                 '2026-08-23T00:00:00+00:00')
            """,
            $"""
            INSERT INTO processing_plans
                (id, inbox_item_id, processor_key, processor_version,
                 processor_display_name, capability, output_filename,
                 work_directory_relative_path, step_keys_json, created_at_utc)
            VALUES
                ('{ids.PlanId}', '{ids.InboxItemId}', 'mp4_m4a_merge', '1',
                 'Merge', 'mp4_m4a_merge', 'paired.mp4',
                 'processing/{ids.InboxItemId}', '[""merge""]',
                 '2026-08-23T00:00:00+00:00')
            """,
            $"""
            INSERT INTO processing_jobs
                (id, plan_id, inbox_item_id, active_inbox_item_id, attempt,
                 status, progress_percent, is_retryable, correlation_id,
                 queued_at_utc, started_at_utc, finished_at_utc, updated_at_utc)
            VALUES
                ('{ids.JobId}', '{ids.PlanId}', '{ids.InboxItemId}', NULL, 1,
                 'completed', 100, 0, 'stage4-migration',
                 '2026-08-23T00:00:00+00:00',
                 '2026-08-23T00:00:00+00:00',
                 '2026-08-23T00:00:00+00:00',
                 '2026-08-23T00:00:00+00:00')
            """,
            $"""
            INSERT INTO processed_outputs
                (job_id, inbox_item_id, active_inbox_item_id, relative_path,
                 size, last_write_time_utc, sha256, duration_ticks,
                 has_video, has_audio, is_valid)
            VALUES
                ('{ids.JobId}', '{ids.InboxItemId}', NULL,
                 'processed/paired.mp4', 5,
                 '2026-08-23T00:00:00+00:00', '{OldHash}', 100, 1, 1, 0)
            """,
            $"""
            INSERT INTO ingest_cleanup_entries
                (id, operation_id, ordinal, cleanup_kind, controlled_path,
                 size, sha256, completed_at_utc)
            VALUES
                ('{ids.CleanupEntryId}', '{ids.OperationId}', 0,
                 'inboxsource', 'T:/inbox/paired.mp4', 5, '{OldHash}',
                 '2026-08-23T00:00:00+00:00')
            """);

        return ids;
    }

    private static string TaxonomySql(Guid id, string kind, string name) => $"""
        INSERT INTO taxonomy_terms
            (id, kind, display_name, normalized_name, created_at_utc, updated_at_utc)
        VALUES
            ('{id}', '{kind}', '{name}', '{name.ToUpperInvariant()}',
             '2026-08-23T00:00:00+00:00',
             '2026-08-23T00:00:00+00:00')
        """;

    private sealed record SeedIds(
        Guid InboxItemId,
        Guid SourceId,
        Guid AuthorId,
        Guid SeriesId,
        Guid TagId,
        Guid AssetId,
        Guid OperationId,
        Guid PlanId,
        Guid JobId,
        Guid CleanupEntryId);
}
