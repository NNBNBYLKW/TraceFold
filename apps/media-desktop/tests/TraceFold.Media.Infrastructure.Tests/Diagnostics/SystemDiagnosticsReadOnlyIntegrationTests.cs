using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TraceFold.Media.Application;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Diagnostics;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Startup;
using TraceFold.Media.Infrastructure;
using TraceFold.Media.Infrastructure.Diagnostics;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Diagnostics;

public sealed class SystemDiagnosticsReadOnlyIntegrationTests
{
    [Fact]
    public async Task Complete_diagnostics_run_preserves_database_settings_and_files()
    {
        using TestDirectory root = TestDirectory.Create();
        string dataRoot = Path.Combine(root.Path, "data");
        string inbox = Path.Combine(root.Path, "inbox");
        string library = Path.Combine(root.Path, "library");
        string work = Path.Combine(root.Path, "work");
        string tools = Path.Combine(root.Path, "tools");
        Directory.CreateDirectory(inbox);
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(work);
        Directory.CreateDirectory(tools);
        File.WriteAllBytes(Path.Combine(tools, "ffmpeg.exe"), [1]);
        File.WriteAllBytes(Path.Combine(tools, "ffprobe.exe"), [2]);
        string mediaPath = Path.Combine(inbox, "private-media-sentinel.webp");
        File.WriteAllBytes(mediaPath, [10, 20, 30, 40]);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostApplicationLifetime,
            TestHostApplicationLifetime>();
        services.AddMediaApplication();
        services.AddMediaInfrastructure(dataRoot, tools);
        services.AddSingleton<IExecutableVersionReader,
            DeterministicExecutableVersionReader>();
        await using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        await provider.GetRequiredService<IDatabaseInitializer>()
            .InitializeAsync(TestContext.Current.CancellationToken);
        var folders = new LibraryFolders(inbox, library, work);
        IDirectorySettingsStore settings =
            provider.GetRequiredService<IDirectorySettingsStore>();
        await settings.SaveAsync(folders, TestContext.Current.CancellationToken);
        IDbContextFactory<MediaDesktopDbContext> contextFactory =
            provider.GetRequiredService<IDbContextFactory<MediaDesktopDbContext>>();
        await SeedBackgroundStateAsync(contextFactory);

        DatabaseSnapshot databaseBefore = await SnapshotDatabaseAsync(
            contextFactory);
        LibraryFolders? settingsBefore = await settings.LoadAsync(
            TestContext.Current.CancellationToken);
        IReadOnlyDictionary<string, FileSnapshot> filesBefore =
            SnapshotFiles(root.Path);

        SystemDiagnosticsSnapshot result = await provider
            .GetRequiredService<ISystemDiagnosticsService>()
            .CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            Enum.GetValues<SystemDiagnosticCategory>(),
            result.Items.Select(item => item.Category)
                .Distinct()
                .Order()
                .ToArray());
        Assert.Equal(databaseBefore, await SnapshotDatabaseAsync(contextFactory));
        Assert.Equal(settingsBefore, await settings.LoadAsync(
            TestContext.Current.CancellationToken));
        Assert.True(filesBefore.SequenceEqual(SnapshotFiles(root.Path)));
        Assert.Empty(Directory.GetFiles(
            root.Path,
            ".tracefold-write-probe-*.tmp",
            SearchOption.AllDirectories));
        Assert.DoesNotContain(
            databaseBefore.TableCounts.Keys,
            table => table.Contains("diagnostic", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            Directory.GetFiles(root.Path, "*", SearchOption.AllDirectories),
            path => Path.GetFileName(path).Contains(
                "diagnostic",
                StringComparison.OrdinalIgnoreCase));
    }

    private static async Task SeedBackgroundStateAsync(
        IDbContextFactory<MediaDesktopDbContext> contextFactory)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(
                TestContext.Current.CancellationToken);
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-30T00:00:00Z");
        Guid processingInboxId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        Guid jobId = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO inbox_items
                (id, state, media_kind, actual_filename, grouping_rule,
                 required_capability, is_retryable, created_at_utc, updated_at_utc)
            VALUES
                ({processingInboxId}, {"processingqueued"}, {"video"},
                 {"private-processing.mp4"}, {"merge-mp4-m4a"},
                 {"merge-mp4-m4a"}, {false}, {now}, {now})
            """, TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO processing_plans
                (id, inbox_item_id, processor_key, processor_version,
                 processor_display_name, capability, output_filename,
                 work_directory_relative_path, step_keys_json, created_at_utc)
            VALUES
                ({planId}, {processingInboxId}, {"merge-mp4-m4a"}, {"1"},
                 {"合并"}, {"merge-mp4-m4a"}, {"private-output.mp4"},
                 {jobId.ToString("N")}, {"[]"}, {now})
            """, TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO processing_jobs
                (id, plan_id, inbox_item_id, active_inbox_item_id, attempt,
                 status, progress_percent, is_retryable, correlation_id,
                 queued_at_utc, updated_at_utc)
            VALUES
                ({jobId}, {planId}, {processingInboxId}, {processingInboxId}, 1,
                 {"queued"}, 0, {false}, {Guid.NewGuid().ToString("N")},
                 {now}, {now})
            """, TestContext.Current.CancellationToken);

        Guid ingestInboxId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO inbox_items
                (id, state, media_kind, actual_filename, grouping_rule,
                 is_retryable, created_at_utc, updated_at_utc)
            VALUES
                ({ingestInboxId}, {"recoveryblocked"}, {"image"},
                 {"private-ingest.webp"}, {"direct"}, {false}, {now}, {now})
            """, TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ingest_operations
                (id, inbox_item_id, active_inbox_item_id, stage, disposition,
                 source_path, source_size, source_last_write_time_utc,
                 source_sha256, target_filename, backup_state,
                 cleanup_attempt_count, metadata_snapshot_json,
                 created_at_utc, updated_at_utc)
            VALUES
                ({operationId}, {ingestInboxId}, {ingestInboxId},
                 {"recoveryblocked"}, {"create"},
                 {$"T:\\private\\{operationId:N}.webp"}, 4, {now},
                 {operationId.ToString("N").PadRight(64, 'A')},
                 {"private-ingest.webp"}, {"notrequired"}, 0, {"{}"},
                 {now}, {now})
            """, TestContext.Current.CancellationToken);
    }

    private static async Task<DatabaseSnapshot> SnapshotDatabaseAsync(
        IDbContextFactory<MediaDesktopDbContext> contextFactory)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(
                TestContext.Current.CancellationToken);
        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        string[] tables;
        await using (DbCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT name FROM sqlite_master
                WHERE type = 'table'
                  AND name NOT LIKE 'sqlite_%'
                  AND name <> '__EFMigrationsHistory'
                ORDER BY name
                """;
            await using DbDataReader reader = await command.ExecuteReaderAsync(
                TestContext.Current.CancellationToken);
            var names = new List<string>();
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                names.Add(reader.GetString(0));
            }

            tables = [.. names];
        }

        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (string table in tables)
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM \"{table.Replace("\"", "\"\"")}\"";
            counts[table] = Convert.ToInt64(await command.ExecuteScalarAsync(
                TestContext.Current.CancellationToken));
        }

        string[] processingStates = await ReadStringsAsync(
            connection,
            "SELECT status FROM processing_jobs ORDER BY id");
        string[] ingestStates = await ReadStringsAsync(
            connection,
            "SELECT stage FROM ingest_operations ORDER BY id");
        return new DatabaseSnapshot(counts, processingStates, ingestStates);
    }

    private static async Task<string[]> ReadStringsAsync(
        DbConnection connection,
        string sql)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync(
            TestContext.Current.CancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return [.. values];
    }

    private static IReadOnlyDictionary<string, FileSnapshot> SnapshotFiles(
        string root)
    {
        var result = new SortedDictionary<string, FileSnapshot>(StringComparer.Ordinal);
        foreach (string path in Directory.GetFiles(
                     root,
                     "*",
                     SearchOption.AllDirectories))
        {
            // WAL and SHM contain SQLite connection bookkeeping; the logical
            // database snapshot above checks the data exposed to the app.
            if (path.EndsWith("-wal", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("-shm", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            result[Path.GetRelativePath(root, path)] = new FileSnapshot(
                stream.Length,
                Convert.ToHexString(SHA256.HashData(stream)));
        }

        return result;
    }

    private sealed class DatabaseSnapshot : IEquatable<DatabaseSnapshot>
    {
        public DatabaseSnapshot(
            IReadOnlyDictionary<string, long> tableCounts,
            IReadOnlyList<string> processingStates,
            IReadOnlyList<string> ingestStates)
        {
            TableCounts = tableCounts;
            ProcessingStates = processingStates;
            IngestStates = ingestStates;
        }

        public IReadOnlyDictionary<string, long> TableCounts { get; }

        public IReadOnlyList<string> ProcessingStates { get; }

        public IReadOnlyList<string> IngestStates { get; }

        public bool Equals(DatabaseSnapshot? other) =>
            other is not null
            && TableCounts.SequenceEqual(other.TableCounts)
            && ProcessingStates.SequenceEqual(other.ProcessingStates)
            && IngestStates.SequenceEqual(other.IngestStates);

        public override bool Equals(object? obj) => Equals(obj as DatabaseSnapshot);

        public override int GetHashCode() => 0;
    }

    private sealed record FileSnapshot(long Size, string Sha256);

    private sealed class DeterministicExecutableVersionReader
        : IExecutableVersionReader
    {
        public Task<ExecutableVersionRead> ReadAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(new[] { "-version" }, arguments);
            string tool = Path.GetFileNameWithoutExtension(executable);
            return Task.FromResult(new ExecutableVersionRead(
                0,
                $"{tool} version 7.1",
                TimedOut: false));
        }
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
