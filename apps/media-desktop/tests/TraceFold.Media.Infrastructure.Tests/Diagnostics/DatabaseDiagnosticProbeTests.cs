using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Diagnostics;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Infrastructure.Diagnostics;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Diagnostics;

public sealed class DatabaseDiagnosticProbeTests
{
    private static readonly string[] CurrentMigrations =
    [
        "20260821121109_InitialDesktopSettings",
        "20260822090000_Stage2MediaLibrary"
    ];

    [Fact]
    public async Task Check_reports_migration_mismatch_without_requesting_a_write()
    {
        var reader = new RecordingDatabaseDiagnosticReader(new DatabaseDiagnosticRead(
            CanOpen: true,
            AppliedMigrations: [CurrentMigrations[0]],
            AvailableMigrations: CurrentMigrations,
            QuickCheckRows: ["ok"],
            DatabasePath: "T:\\private\\media-desktop.db"));

        SystemDiagnosticItem item = Assert.Single(await
            new DatabaseDiagnosticProbe(reader).CheckAsync(
                TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Unavailable, item.Severity);
        Assert.Equal("diagnostics.database.migration_mismatch", item.ErrorCode);
        Assert.Equal(1, reader.ReadCallCount);
        Assert.Equal(0, reader.WriteCallCount);
    }

    [Theory]
    [InlineData("*** in database main ***")]
    [InlineData("row 42 missing from index")]
    [InlineData("OK")]
    [InlineData(" ok")]
    public async Task Check_requires_quick_check_to_return_ordinal_lowercase_ok(
        string result)
    {
        SystemDiagnosticItem item = await CheckQuickCheckAsync([result]);

        Assert.Equal(SystemDiagnosticSeverity.Unavailable, item.Severity);
        Assert.Equal("diagnostics.database.quick_check_failed", item.ErrorCode);
    }

    [Fact]
    public async Task Check_rejects_zero_or_multiple_quick_check_rows()
    {
        SystemDiagnosticItem empty = await CheckQuickCheckAsync([]);
        SystemDiagnosticItem multiple = await CheckQuickCheckAsync(["ok", "ok"]);

        Assert.Equal("diagnostics.database.quick_check_failed", empty.ErrorCode);
        Assert.Equal("diagnostics.database.quick_check_failed", multiple.ErrorCode);
    }

    [Fact]
    public async Task Check_maps_an_unopenable_database_to_a_stable_error()
    {
        var reader = new RecordingDatabaseDiagnosticReader(new DatabaseDiagnosticRead(
            CanOpen: false,
            AppliedMigrations: [],
            AvailableMigrations: CurrentMigrations,
            QuickCheckRows: [],
            DatabasePath: "T:\\private\\media-desktop.db"));

        SystemDiagnosticItem item = Assert.Single(await
            new DatabaseDiagnosticProbe(reader).CheckAsync(
                TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Unavailable, item.Severity);
        Assert.Equal("diagnostics.database.unavailable", item.ErrorCode);
    }

    [Fact]
    public async Task Check_maps_reader_failures_without_disclosing_exception_details()
    {
        var reader = new RecordingDatabaseDiagnosticReader(
            new InvalidOperationException(
                "T:\\private\\media-desktop.db for user-secret"));

        SystemDiagnosticItem item = Assert.Single(await
            new DatabaseDiagnosticProbe(reader).CheckAsync(
                TestContext.Current.CancellationToken));

        Assert.Equal("diagnostics.database.unavailable", item.ErrorCode);
        Assert.DoesNotContain("user-secret", item.Conclusion, StringComparison.Ordinal);
        Assert.DoesNotContain(
            item.Facts,
            fact => fact.Value.Contains("user-secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Check_reports_healthy_shareable_facts_and_keeps_the_path_local()
    {
        const string databasePath = "T:\\private\\media-desktop.db";
        var reader = new RecordingDatabaseDiagnosticReader(new DatabaseDiagnosticRead(
            CanOpen: true,
            AppliedMigrations: CurrentMigrations,
            AvailableMigrations: CurrentMigrations,
            QuickCheckRows: ["ok"],
            DatabasePath: databasePath));

        var probe = new DatabaseDiagnosticProbe(reader);
        SystemDiagnosticItem item = Assert.Single(await probe.CheckAsync(
            TestContext.Current.CancellationToken));

        Assert.Equal("database.sqlite", item.Id);
        Assert.Equal(SystemDiagnosticSeverity.Healthy, item.Severity);
        Assert.Null(item.ErrorCode);
        Assert.Contains(item.Facts, fact =>
            fact.Key == "database.migration_count"
            && fact.Value == "2"
            && fact.Sensitivity == SystemDiagnosticFactSensitivity.Shareable);
        Assert.Contains(item.Facts, fact =>
            fact.Key == "database.quick_check"
            && fact.Value == "ok"
            && fact.Sensitivity == SystemDiagnosticFactSensitivity.Shareable);
        Assert.DoesNotContain(item.Facts, fact =>
            fact.Value == databasePath
            && fact.Sensitivity == SystemDiagnosticFactSensitivity.Shareable);
        Assert.Contains(item.Facts, fact =>
            fact.Key == "database.path"
            && fact.Value == databasePath
            && fact.Sensitivity == SystemDiagnosticFactSensitivity.LocalOnly);
        Assert.Equal(SystemDiagnosticCategory.Database, probe.Descriptor.Category);
        Assert.Equal("diagnostics.database.timeout", probe.Descriptor.TimeoutErrorCode);
    }

    [Fact]
    public async Task Check_propagates_cancellation_for_the_orchestrator()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var reader = new RecordingDatabaseDiagnosticReader(
            new OperationCanceledException(cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new DatabaseDiagnosticProbe(reader).CheckAsync(cancellation.Token));
    }

    [Fact]
    public async Task Ef_reader_checks_a_real_database_without_changing_file_or_rows()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        await database.SeedInboxItemAsync(InboxItemState.ReadyToTag, metadataVersion: 1);
        IDbContextFactory<MediaDesktopDbContext> factory =
            database.CreateContextFactory();
        string databasePath = await ReadDatabasePathAsync(factory);
        FileSnapshot before = Snapshot(databasePath);
        long inboxCountBefore = await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM inbox_items");
        long draftCountBefore = await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM metadata_drafts");

        SystemDiagnosticItem item = Assert.Single(await
            new DatabaseDiagnosticProbe(new EfDatabaseDiagnosticReader(factory))
                .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Healthy, item.Severity);
        Assert.Equal(before, Snapshot(databasePath));
        Assert.Equal(inboxCountBefore, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM inbox_items"));
        Assert.Equal(draftCountBefore, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM metadata_drafts"));
    }

    [Fact]
    public async Task Ef_reader_reports_an_old_database_without_advancing_its_migration()
    {
        const string oldMigration = "20260826090000_Stage5BLibraryQuery";
        await using TestMediaDatabase database =
            await TestMediaDatabase.CreateAtMigrationAsync(oldMigration);
        IDbContextFactory<MediaDesktopDbContext> factory =
            database.CreateContextFactory();
        string[] before = await ReadAppliedMigrationsAsync(factory);

        SystemDiagnosticItem item = Assert.Single(await
            new DatabaseDiagnosticProbe(new EfDatabaseDiagnosticReader(factory))
                .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Unavailable, item.Severity);
        Assert.Equal("diagnostics.database.migration_mismatch", item.ErrorCode);
        Assert.Equal(before, await ReadAppliedMigrationsAsync(factory));
        Assert.Equal(oldMigration, Assert.Single(before.TakeLast(1)));
    }

    private static async Task<SystemDiagnosticItem> CheckQuickCheckAsync(
        IReadOnlyList<string> rows)
    {
        var reader = new RecordingDatabaseDiagnosticReader(new DatabaseDiagnosticRead(
            CanOpen: true,
            AppliedMigrations: CurrentMigrations,
            AvailableMigrations: CurrentMigrations,
            QuickCheckRows: rows,
            DatabasePath: null));
        return Assert.Single(await new DatabaseDiagnosticProbe(reader).CheckAsync(
            TestContext.Current.CancellationToken));
    }

    private static async Task<string> ReadDatabasePathAsync(
        IDbContextFactory<MediaDesktopDbContext> factory)
    {
        await using MediaDesktopDbContext context =
            await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        return context.Database.GetDbConnection().DataSource;
    }

    private static async Task<string[]> ReadAppliedMigrationsAsync(
        IDbContextFactory<MediaDesktopDbContext> factory)
    {
        await using MediaDesktopDbContext context =
            await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        return [.. await context.Database.GetAppliedMigrationsAsync(
            TestContext.Current.CancellationToken)];
    }

    private static FileSnapshot Snapshot(string path)
    {
        var info = new FileInfo(path);
        return new(info.Length, Convert.ToHexString(SHA256.HashData(
            File.ReadAllBytes(path))));
    }

    private sealed record FileSnapshot(long Length, string Sha256);

    private sealed class RecordingDatabaseDiagnosticReader : IDatabaseDiagnosticReader
    {
        private readonly DatabaseDiagnosticRead? _read;
        private readonly Exception? _failure;

        public RecordingDatabaseDiagnosticReader(DatabaseDiagnosticRead read)
        {
            _read = read;
        }

        public RecordingDatabaseDiagnosticReader(Exception failure)
        {
            _failure = failure;
        }

        public int ReadCallCount { get; private set; }

        public int WriteCallCount { get; private set; }

        public Task<DatabaseDiagnosticRead> ReadAsync(
            CancellationToken cancellationToken)
        {
            ReadCallCount++;
            if (_failure is not null)
            {
                return Task.FromException<DatabaseDiagnosticRead>(_failure);
            }

            return Task.FromResult(_read!);
        }
    }
}
