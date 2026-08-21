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

    public ValueTask DisposeAsync()
    {
        _directory.Dispose();
        return ValueTask.CompletedTask;
    }
}
