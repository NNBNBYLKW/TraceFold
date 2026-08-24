using Microsoft.Data.Sqlite;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class Stage2MigrationTests
{
    [Fact]
    public async Task Stage2_migration_preserves_stage1_directories_and_defaults_auto_scan_off()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync("20260821121109_InitialDesktopSettings");
        await database.InsertStage1SettingsAsync("C:/inbox", "C:/library", "C:/work");

        await database.MigrateAsync();

        SavedSettings row = await database.ReadSettingsAsync();
        Assert.Equal("C:/inbox", row.InboxDirectory);
        Assert.Equal("C:/library", row.LibraryDirectory);
        Assert.Equal("C:/work", row.WorkDirectory);
        Assert.False(row.AutoScanEnabled);
        Assert.Null(row.LastScanAtUtc);
        Assert.Null(row.LastScanSummary);
    }

    [Fact]
    public async Task Stage2_migration_creates_all_media_library_tables()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();

        await database.MigrateAsync();

        IReadOnlySet<string> tables = await database.ReadTableNamesAsync();
        string[] expected =
        [
            "app_settings",
            "inbox_items",
            "source_files",
            "metadata_drafts",
            "taxonomy_terms",
            "inbox_draft_authors",
            "inbox_draft_tags",
            "media_assets",
            "media_asset_authors",
            "media_asset_tags",
            "ingest_operations"
        ];
        Assert.All(expected, table => Assert.Contains(table, tables));
    }

    [Fact]
    public async Task Active_source_path_can_belong_to_only_one_record()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid inboxItemId = Guid.NewGuid();
        await database.ExecuteSqlAsync(
            InboxItemSql(inboxItemId),
            SourceFileSql(Guid.NewGuid(), inboxItemId, "C:/inbox/photo.jpg"));

        await Assert.ThrowsAsync<SqliteException>(() => database.ExecuteSqlAsync(
            SourceFileSql(Guid.NewGuid(), inboxItemId, "C:/inbox/photo.jpg")));
    }

    [Fact]
    public async Task Taxonomy_kind_and_normalized_name_are_unique_together()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        await database.ExecuteSqlAsync(TaxonomySql(Guid.NewGuid(), "author", "Alice", "ALICE"));

        await Assert.ThrowsAsync<SqliteException>(() => database.ExecuteSqlAsync(
            TaxonomySql(Guid.NewGuid(), "author", "Ａlice", "ALICE")));
    }

    [Fact]
    public async Task Formal_relative_path_is_unique()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        await database.ExecuteSqlAsync(MediaAssetSql(
            Guid.NewGuid(),
            "photo.jpg",
            "PHOTO.JPG",
            new string('1', 64)));

        await Assert.ThrowsAsync<SqliteException>(() => database.ExecuteSqlAsync(MediaAssetSql(
            Guid.NewGuid(),
            "photo.jpg",
            "PHOTO.JPG",
            new string('2', 64))));
    }

    [Fact]
    public async Task Formal_sha256_is_unique()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync("20260823120000_Stage3ProcessingPipeline");
        string sha256 = new('A', 64);
        await database.ExecuteSqlAsync(MediaAssetSql(
            Guid.NewGuid(),
            "first.jpg",
            "FIRST.JPG",
            sha256));

        await Assert.ThrowsAsync<SqliteException>(() => database.ExecuteSqlAsync(MediaAssetSql(
            Guid.NewGuid(),
            "second.jpg",
            "SECOND.JPG",
            sha256)));
    }

    [Fact]
    public async Task Inbox_item_can_have_only_one_active_ingest_operation()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid inboxItemId = Guid.NewGuid();
        await database.ExecuteSqlAsync(
            InboxItemSql(inboxItemId),
            IngestOperationSql(Guid.NewGuid(), inboxItemId));

        await Assert.ThrowsAsync<SqliteException>(() => database.ExecuteSqlAsync(
            IngestOperationSql(Guid.NewGuid(), inboxItemId)));
    }

    private static string InboxItemSql(Guid id) => $"""
        INSERT INTO inbox_items
            (id, state, media_kind, actual_filename, grouping_rule,
             is_retryable, created_at_utc, updated_at_utc)
        VALUES
            ('{id}', 'readytotag', 'image', 'photo.jpg', 'direct_media',
             0, '2026-08-22T00:00:00+00:00', '2026-08-22T00:00:00+00:00')
        """;

    private static string SourceFileSql(Guid id, Guid inboxItemId, string activePath) => $"""
        INSERT INTO source_files
            (id, inbox_item_id, normalized_path, active_path_key, filename, extension,
             size, last_write_time_utc, first_observed_at_utc,
             stable_observation_count, is_present, is_primary)
        VALUES
            ('{id}', '{inboxItemId}', '{activePath.ToUpperInvariant()}',
             '{activePath.ToUpperInvariant()}', 'photo.jpg', '.jpg', 3,
             '2026-08-22T00:00:00+00:00', '2026-08-22T00:00:00+00:00', 2, 1, 1)
        """;

    private static string TaxonomySql(
        Guid id,
        string kind,
        string displayName,
        string normalizedName) => $"""
        INSERT INTO taxonomy_terms
            (id, kind, display_name, normalized_name, created_at_utc, updated_at_utc)
        VALUES
            ('{id}', '{kind}', '{displayName}', '{normalizedName}',
             '2026-08-22T00:00:00+00:00', '2026-08-22T00:00:00+00:00')
        """;

    private static string MediaAssetSql(
        Guid id,
        string relativePath,
        string normalizedRelativePath,
        string sha256) => $"""
        INSERT INTO media_assets
            (id, media_kind, actual_filename, relative_path, normalized_relative_path,
             size, sha256, metadata_version, created_at_utc, updated_at_utc)
        VALUES
            ('{id}', 'image', '{relativePath}', '{relativePath}',
             '{normalizedRelativePath}', 3, '{sha256}', 0,
             '2026-08-22T00:00:00+00:00', '2026-08-22T00:00:00+00:00')
        """;

    private static string IngestOperationSql(Guid id, Guid inboxItemId) => $"""
        INSERT INTO ingest_operations
            (id, inbox_item_id, active_inbox_item_id, stage, source_path, source_size,
             source_last_write_time_utc, source_sha256, target_filename,
             metadata_snapshot_json, created_at_utc, updated_at_utc)
        VALUES
            ('{id}', '{inboxItemId}', '{inboxItemId}', 'created', 'C:/inbox/photo.jpg', 3,
             '2026-08-22T00:00:00+00:00', '{new string('A', 64)}', 'photo.jpg', '[]',
             '2026-08-22T00:00:00+00:00', '2026-08-22T00:00:00+00:00')
        """;
}
