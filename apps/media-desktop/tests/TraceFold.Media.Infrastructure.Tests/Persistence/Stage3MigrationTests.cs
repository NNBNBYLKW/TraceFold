using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class Stage3MigrationTests
{
    [Fact]
    public async Task Stage3_migration_preserves_stage2_rows_and_adds_processing_tables()
    {
        await using TestMediaDatabase database =
            await TestMediaDatabase.CreateAtMigrationAsync(
                "20260822090000_Stage2MediaLibrary");
        Guid itemId = Guid.NewGuid();
        await database.ExecuteSqlAsync(InboxItemSql(itemId));

        await database.MigrateAsync();

        Assert.Equal(
            InboxItemState.RequiresProcessing.ToString().ToLowerInvariant(),
            await database.ReadInboxStateAsync(itemId));
        IReadOnlySet<string> tables = await database.ReadTableNamesAsync();
        Assert.Contains("processing_plans", tables);
        Assert.Contains("processing_plan_inputs", tables);
        Assert.Contains("processing_jobs", tables);
        Assert.Contains("processed_outputs", tables);
        Assert.Contains("ingest_cleanup_entries", tables);
    }

    private static string InboxItemSql(Guid id) => $"""
        INSERT INTO inbox_items
            (id, state, media_kind, actual_filename, grouping_rule,
             required_capability, is_retryable, created_at_utc, updated_at_utc)
        VALUES
            ('{id}', 'requiresprocessing', 'video', 'paired.mp4', 'mp4_m4a',
             'mp4_m4a_merge', 0,
             '2026-08-23T00:00:00+00:00', '2026-08-23T00:00:00+00:00')
        """;
}
