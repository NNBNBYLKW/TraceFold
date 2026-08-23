using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class EfInboxQueryStoreProcessingTests
{
    [Fact]
    public async Task Details_project_running_step_progress_and_stop_action()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        using var folders = TestMediaFolders.Create();
        await new EfDirectorySettingsStore(database.CreateContextFactory()).SaveAsync(
            folders.Value,
            TestContext.Current.CancellationToken);
        string sourcePath = folders.WriteInbox("clip.mp4", [1, 2, 3]);
        Guid itemId = await RegisterInboxAsync(database, sourcePath);
        await InsertRunningJobAsync(database, itemId);
        var store = new EfInboxQueryStore(database.CreateContextFactory());

        InboxDetailsDto details = (await store.GetDetailsAsync(
            itemId,
            TestContext.Current.CancellationToken))!;

        Assert.Equal("合并音视频", details.Processing!.StepText);
        Assert.Equal(63, details.Processing.ProgressPercent);
        Assert.True(details.AllowedActions.HasFlag(InboxAction.StopProcessing));
        Assert.Equal("处理中", details.Card.StatusText);
    }

    private static async Task<Guid> RegisterInboxAsync(
        TestMediaDatabase database,
        string sourcePath)
    {
        DateTimeOffset instant = DateTimeOffset.Parse("2026-08-23T10:00:00Z");
        var source = new ObservedFile(
            sourcePath,
            Path.GetFileName(sourcePath),
            Path.GetExtension(sourcePath),
            new FileFingerprint(new FileInfo(sourcePath).Length, instant),
            instant);
        var reconciliation = new ScanReconciliation(
            Path.GetDirectoryName(sourcePath)!,
            instant,
            [
                new CandidateGroup(
                    "direct_media",
                    sourcePath.ToUpperInvariant(),
                    MediaKind.Video,
                    CandidateDisposition.Direct,
                    [source],
                    null)
            ],
            new HashSet<string>(
                [Path.GetFullPath(sourcePath).ToUpperInvariant()],
                StringComparer.OrdinalIgnoreCase));
        await new EfInboxScanStore(database.CreateContextFactory()).ReconcileAsync(
            reconciliation,
            TestContext.Current.CancellationToken);
        return (await database.ReadSingleInboxAsync()).Id;
    }

    private static Task InsertRunningJobAsync(
        TestMediaDatabase database,
        Guid itemId)
    {
        Guid planId = Guid.NewGuid();
        Guid jobId = Guid.NewGuid();
        string item = itemId.ToString().ToUpperInvariant();
        string plan = planId.ToString().ToUpperInvariant();
        string job = jobId.ToString().ToUpperInvariant();
        const string now = "2026-08-23T10:00:00+00:00";
        return database.ExecuteSqlAsync(
            $"""
            UPDATE inbox_items
            SET state = 'processing', required_capability = 'mp4_m4a_merge'
            WHERE id = '{item}'
            """,
            $"""
            INSERT INTO processing_plans
                (id, inbox_item_id, processor_key, processor_version,
                 processor_display_name, capability, output_filename,
                 work_directory_relative_path, step_keys_json, created_at_utc)
            VALUES
                ('{plan}', '{item}', 'mp4_m4a_merge', '1', '合并视频和音频',
                 'mp4_m4a_merge', 'clip.mp4', '.tracefold/processing/{jobId:N}',
                 '["check_sources","merge","verify_output"]', '{now}')
            """,
            $"""
            INSERT INTO processing_jobs
                (id, plan_id, inbox_item_id, active_inbox_item_id, attempt, status,
                 current_step_key, progress_percent, error_code, is_retryable,
                 correlation_id, queued_at_utc, started_at_utc, finished_at_utc,
                 updated_at_utc)
            VALUES
                ('{job}', '{plan}', '{item}', '{item}', 1, 'running',
                 'merge', 63, NULL, 0, 'query-test',
                 '{now}', '{now}', NULL, '{now}')
            """);
    }
}
