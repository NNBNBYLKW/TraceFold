using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Infrastructure.Persistence;

namespace TraceFold.Media.Infrastructure.Tests.Support;

internal sealed record SavedSettings(
    string InboxDirectory,
    string LibraryDirectory,
    string WorkDirectory,
    bool AutoScanEnabled,
    DateTimeOffset? LastScanAtUtc,
    string? LastScanSummary);

internal sealed record PersistedInboxSnapshot(
    Guid Id,
    string State,
    string? RequiredCapability,
    long DraftVersion,
    long PrimarySourceSize,
    int PrimaryStableObservationCount,
    string? PrimarySha256,
    bool PrimaryIsPresent,
    int SourceCount);

internal sealed record PersistedIngestSnapshot(
    Guid? AssetId,
    string OperationStage,
    string InboxState,
    string? ActivePathKey,
    string? DisplayName,
    Guid? SeriesId,
    int AuthorCount,
    int TagCount);

internal sealed record LibraryFixture(
    Guid AuthorA,
    Guid AuthorB,
    Guid TagA,
    Guid TagB,
    Guid AssetWithEveryTerm);

internal sealed class TestMediaDatabase : IAsyncDisposable
{
    private readonly TestDirectory _directory;
    private readonly DbContextOptions<MediaDesktopDbContext> _options;

    private TestMediaDatabase(TestDirectory directory)
    {
        _directory = directory;
        string databasePath = Path.Combine(directory.Path, "media-desktop.db");
        _options = new DbContextOptionsBuilder<MediaDesktopDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
    }

    public static Task<TestMediaDatabase> CreateAsync()
    {
        var database = new TestMediaDatabase(TestDirectory.Create());
        return Task.FromResult(database);
    }

    public static async Task<TestMediaDatabase> CreateAtMigrationAsync(
        string targetMigration)
    {
        TestMediaDatabase database = await CreateAsync();
        await database.MigrateAsync(targetMigration);
        return database;
    }

    public IDbContextFactory<MediaDesktopDbContext> CreateContextFactory() =>
        new TestContextFactory(_options);

    public async Task MigrateAsync(string? targetMigration = null)
    {
        await using var context = new MediaDesktopDbContext(_options);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(targetMigration);
    }

    public async Task<Guid> SeedInboxItemAsync(
        InboxItemState state,
        long metadataVersion,
        MediaKind mediaKind = MediaKind.Image,
        string? displayName = null)
    {
        Guid id = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-24T00:00:00Z");
        string filename = mediaKind == MediaKind.Image
            ? $"{id:N}.jpg"
            : $"{id:N}.mp4";
        string? capability = state == InboxItemState.RequiresProcessing
            ? "merge-mp4-m4a"
            : null;
        await using var context = new MediaDesktopDbContext(_options);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO inbox_items
                (id, state, media_kind, actual_filename, grouping_rule,
                 required_capability, is_retryable, created_at_utc, updated_at_utc)
            VALUES
                ({id}, {state.ToString().ToLowerInvariant()},
                 {mediaKind.ToString().ToLowerInvariant()}, {filename}, {"direct"},
                 {capability}, {false}, {now}, {now})
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metadata_drafts
                (inbox_item_id, display_name, series_id, version, updated_at_utc)
            VALUES ({id}, {displayName}, {(Guid?)null}, {metadataVersion}, {now})
            """);
        return id;
    }

    public async Task<Guid[]> SeedEditableInboxItemsAsync(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var ids = new Guid[count];
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-24T00:00:00Z");
        await using var context = new MediaDesktopDbContext(_options);
        await using var transaction = await context.Database.BeginTransactionAsync();
        for (int index = 0; index < count; index++)
        {
            Guid id = Guid.NewGuid();
            ids[index] = id;
            string filename = $"{id:N}.jpg";
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO inbox_items
                    (id, state, media_kind, actual_filename, grouping_rule,
                     required_capability, is_retryable, created_at_utc, updated_at_utc)
                VALUES
                    ({id}, {"readytotag"}, {"image"}, {filename}, {"direct"},
                     {(string?)null}, {false}, {now}, {now})
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO metadata_drafts
                    (inbox_item_id, display_name, series_id, version, updated_at_utc)
                VALUES ({id}, {(string?)null}, {(Guid?)null}, {0}, {now})
                """);
        }

        await transaction.CommitAsync();
        return ids;
    }

    public async Task<IReadOnlyList<long>> LoadMetadataVersionsAsync(
        IReadOnlyList<Guid> inboxItemIds)
    {
        await using var context = new MediaDesktopDbContext(_options);
        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        var versions = new List<long>(inboxItemIds.Count);
        foreach (Guid id in inboxItemIds)
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT version FROM metadata_drafts WHERE upper(inbox_item_id) = upper($id)";
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "$id";
            parameter.Value = id.ToString();
            command.Parameters.Add(parameter);
            object? value = await command.ExecuteScalarAsync();
            versions.Add(Convert.ToInt64(value));
        }

        return versions;
    }

    public async Task<Guid> SeedTaxonomyAsync(
        TaxonomyKind kind,
        string displayName)
    {
        Guid id = Guid.NewGuid();
        TaxonomyName name = TaxonomyName.Create(displayName);
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-24T00:00:00Z");
        await using var context = new MediaDesktopDbContext(_options);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO taxonomy_terms
                (id, kind, display_name, normalized_name, created_at_utc, updated_at_utc)
            VALUES
                ({id}, {kind.ToString().ToLowerInvariant()}, {name.Display},
                 {name.Normalized}, {now}, {now})
            """);
        return id;
    }

    public async Task<LibraryFixture> SeedLibraryFilterFixtureAsync()
    {
        Guid authorA = await SeedTaxonomyAsync(TaxonomyKind.Author, "Author A");
        Guid authorB = await SeedTaxonomyAsync(TaxonomyKind.Author, "Author B");
        Guid tagA = await SeedTaxonomyAsync(TaxonomyKind.Tag, "Tag A");
        Guid tagB = await SeedTaxonomyAsync(TaxonomyKind.Tag, "Tag B");

        Guid everyTerm = await SeedFormalAssetAsync(
            "every.jpg",
            displayName: null,
            metadataVersion: 7,
            authorIds: [authorA, authorB],
            tagIds: [tagA, tagB]);
        await SeedFormalAssetAsync(
            "missing-author.jpg",
            displayName: null,
            metadataVersion: 0,
            authorIds: [authorA],
            tagIds: [tagA, tagB]);
        await SeedFormalAssetAsync(
            "missing-tag.jpg",
            displayName: null,
            metadataVersion: 0,
            authorIds: [authorA, authorB],
            tagIds: [tagA]);

        return new LibraryFixture(authorA, authorB, tagA, tagB, everyTerm);
    }

    public async Task<Guid> SeedFormalAssetAsync(
        string actualFilename,
        string? displayName,
        long metadataVersion,
        MediaKind mediaKind = MediaKind.Image,
        IReadOnlyList<Guid>? authorIds = null,
        Guid? seriesId = null,
        IReadOnlyList<Guid>? tagIds = null)
    {
        Guid id = Guid.NewGuid();
        await using var context = new MediaDesktopDbContext(_options);
        await using var transaction = await context.Database.BeginTransactionAsync();
        await InsertFormalAssetAsync(
            context,
            id,
            actualFilename,
            displayName,
            metadataVersion,
            mediaKind,
            authorIds ?? [],
            seriesId,
            tagIds ?? []);
        await transaction.CommitAsync();
        return id;
    }

    public async Task<Guid[]> SeedFormalAssetsAsync(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var ids = new Guid[count];
        await using var context = new MediaDesktopDbContext(_options);
        await using var transaction = await context.Database.BeginTransactionAsync();
        for (int index = 0; index < count; index++)
        {
            Guid id = Guid.NewGuid();
            ids[index] = id;
            await InsertFormalAssetAsync(
                context,
                id,
                $"asset-{count - index:D4}.jpg",
                displayName: null,
                metadataVersion: index,
                MediaKind.Image,
                [],
                seriesId: null,
                []);
        }

        await transaction.CommitAsync();
        return ids;
    }

    public async Task<int> CountTaxonomyTermsAsync(
        TaxonomyKind kind,
        string displayName)
    {
        TaxonomyName name = TaxonomyName.Create(displayName);
        await using var context = new MediaDesktopDbContext(_options);
        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM taxonomy_terms
            WHERE kind = $kind AND normalized_name = $normalized
            """;
        AddParameter(command, "$kind", kind.ToString().ToLowerInvariant());
        AddParameter(command, "$normalized", name.Normalized);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task<int> CountDraftsWithAuthorAsync(string displayName)
    {
        TaxonomyName name = TaxonomyName.Create(displayName);
        await using var context = new MediaDesktopDbContext(_options);
        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(DISTINCT links.inbox_item_id)
            FROM inbox_draft_authors links
            JOIN taxonomy_terms terms ON terms.id = links.author_id
            WHERE terms.kind = 'author' AND terms.normalized_name = $normalized
            """;
        AddParameter(command, "$normalized", name.Normalized);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task CreateMetadataUpdateFailureTriggerAsync(Guid inboxItemId)
    {
        await using var context = new MediaDesktopDbContext(_options);
        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TRIGGER test_fail_batch_metadata
            BEFORE UPDATE ON metadata_drafts
            WHEN upper(NEW.inbox_item_id) = upper('{inboxItemId:D}')
            BEGIN
                SELECT RAISE(ABORT, 'test batch failure');
            END
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    public async Task InsertStage1SettingsAsync(
        string inboxDirectory,
        string libraryDirectory,
        string workDirectory)
    {
        await using var context = new MediaDesktopDbContext(_options);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app_settings
                (Id, InboxDirectory, LibraryDirectory, WorkDirectory, UpdatedAtUtc)
            VALUES
                (1, {inboxDirectory}, {libraryDirectory}, {workDirectory}, {DateTimeOffset.UtcNow})
            """);
    }

    public async Task<SavedSettings> ReadSettingsAsync()
    {
        await using var context = new MediaDesktopDbContext(_options);
        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT InboxDirectory, LibraryDirectory, WorkDirectory,
                   AutoScanEnabled, LastScanAtUtc, LastScanSummary
            FROM app_settings
            WHERE Id = 1
            """;
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("The Stage 1 settings row is missing.");
        }

        DateTimeOffset? lastScanAtUtc = reader.IsDBNull(4)
            ? null
            : DateTimeOffset.Parse(reader.GetString(4));
        return new SavedSettings(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            Convert.ToBoolean(reader.GetInt64(3)),
            lastScanAtUtc,
            reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    public async Task<IReadOnlySet<string>> ReadTableNamesAsync()
    {
        await using var context = new MediaDesktopDbContext(_options);
        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
            """;
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    public async Task<IReadOnlySet<string>> ReadColumnNamesAsync(string table)
    {
        string trustedTable = table switch
        {
            "ingest_operations" => "ingest_operations",
            "ingest_cleanup_entries" => "ingest_cleanup_entries",
            "conflict_cases" => "conflict_cases",
            "conflict_matches" => "conflict_matches",
            "app_settings" => "app_settings",
            _ => throw new ArgumentOutOfRangeException(
                nameof(table),
                table,
                "The test table is not approved for schema inspection.")
        };

        await using var context = new MediaDesktopDbContext(_options);
        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({trustedTable})";
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var context = new MediaDesktopDbContext(_options);
        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = await command.ExecuteScalarAsync();
        if (value is null or DBNull)
        {
            throw new InvalidOperationException("The scalar query returned no value.");
        }

        if (value is T typed)
        {
            return typed;
        }

        return (T)Convert.ChangeType(value, typeof(T));
    }

    public async Task ExecuteSqlAsync(params string[] statements)
    {
        await using var context = new MediaDesktopDbContext(_options);
        foreach (string statement in statements)
        {
            await context.Database.ExecuteSqlRawAsync(statement);
        }
    }

    public async Task<PersistedInboxSnapshot> ReadSingleInboxAsync()
    {
        await using var context = new MediaDesktopDbContext(_options);
        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.id, i.state, i.required_capability, d.version,
                   s.size, s.stable_observation_count, s.sha256, s.is_present,
                   (SELECT COUNT(*) FROM source_files all_sources
                    WHERE all_sources.inbox_item_id = i.id)
            FROM inbox_items i
            JOIN metadata_drafts d ON d.inbox_item_id = i.id
            JOIN source_files s ON s.inbox_item_id = i.id AND s.is_primary = 1
            """;
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Expected exactly one persisted inbox item.");
        }

        var snapshot = new PersistedInboxSnapshot(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetBoolean(7),
            reader.GetInt32(8));
        if (await reader.ReadAsync())
        {
            throw new InvalidOperationException("Expected exactly one persisted inbox item.");
        }

        return snapshot;
    }

    public async Task SetDraftVersionAsync(Guid inboxItemId, long version)
    {
        await using var context = new MediaDesktopDbContext(_options);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE metadata_drafts
            SET version = {version}
            WHERE inbox_item_id = {inboxItemId}
            """);
    }

    public async Task<int> CountInboxItemsAsync()
    {
        await using var context = new MediaDesktopDbContext(_options);
        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM inbox_items";
        object? value = await command.ExecuteScalarAsync();
        return Convert.ToInt32(value);
    }

    public async Task<int> CountIngestOperationsAsync()
    {
        await using var context = new MediaDesktopDbContext(_options);
        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ingest_operations";
        object? value = await command.ExecuteScalarAsync();
        return Convert.ToInt32(value);
    }

    private static async Task InsertFormalAssetAsync(
        MediaDesktopDbContext context,
        Guid id,
        string actualFilename,
        string? displayName,
        long metadataVersion,
        MediaKind mediaKind,
        IReadOnlyList<Guid> authorIds,
        Guid? seriesId,
        IReadOnlyList<Guid> tagIds)
    {
        string relativePath = $"{id:N}/{actualFilename}";
        string normalizedRelativePath = relativePath.ToUpperInvariant();
        string sha256 = id.ToString("N").PadRight(64, 'A');
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-24T00:00:00Z");
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO media_assets
                (id, media_kind, actual_filename, relative_path,
                 normalized_relative_path, display_name, size, sha256,
                 series_id, metadata_version, created_at_utc, updated_at_utc)
            VALUES
                ({id}, {mediaKind.ToString().ToLowerInvariant()}, {actualFilename},
                 {relativePath}, {normalizedRelativePath}, {displayName}, 1,
                 {sha256}, {seriesId}, {metadataVersion}, {now}, {now})
            """);

        for (int index = 0; index < authorIds.Count; index++)
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO media_asset_authors
                    (media_asset_id, author_id, ordinal)
                VALUES ({id}, {authorIds[index]}, {index})
                """);
        }

        for (int index = 0; index < tagIds.Count; index++)
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO media_asset_tags
                    (media_asset_id, tag_id, ordinal)
                VALUES ({id}, {tagIds[index]}, {index})
                """);
        }
    }

    public async Task<string?> ReadInboxStateAsync(Guid inboxItemId)
    {
        await using var context = new MediaDesktopDbContext(_options);
        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT state FROM inbox_items WHERE upper(id) = upper($id)";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "$id";
        parameter.Value = inboxItemId.ToString();
        command.Parameters.Add(parameter);
        object? value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    public async Task<PersistedIngestSnapshot> ReadSingleIngestAsync()
    {
        await using var context = new MediaDesktopDbContext(_options);
        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.id, o.stage, i.state, s.active_path_key,
                   a.display_name, a.series_id,
                   (SELECT COUNT(*) FROM media_asset_authors aa
                    WHERE aa.media_asset_id = a.id),
                   (SELECT COUNT(*) FROM media_asset_tags at
                    WHERE at.media_asset_id = a.id)
            FROM ingest_operations o
            JOIN inbox_items i ON i.id = o.inbox_item_id
            JOIN source_files s ON s.inbox_item_id = i.id AND s.is_primary = 1
            LEFT JOIN media_assets a ON a.id = o.asset_id
            """;
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Expected one ingest operation.");
        }

        return new PersistedIngestSnapshot(
            reader.IsDBNull(0) ? null : Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
            reader.GetInt32(6),
            reader.GetInt32(7));
    }

    public ValueTask DisposeAsync()
    {
        _directory.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class TestContextFactory(
        DbContextOptions<MediaDesktopDbContext> options)
        : IDbContextFactory<MediaDesktopDbContext>
    {
        public MediaDesktopDbContext CreateDbContext() => new(options);

        public ValueTask<MediaDesktopDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new MediaDesktopDbContext(options));
    }
}
