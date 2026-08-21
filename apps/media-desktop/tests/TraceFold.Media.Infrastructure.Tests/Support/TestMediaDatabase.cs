using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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

    public IDbContextFactory<MediaDesktopDbContext> CreateContextFactory() =>
        new TestContextFactory(_options);

    public async Task MigrateAsync(string? targetMigration = null)
    {
        await using var context = new MediaDesktopDbContext(_options);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(targetMigration);
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
