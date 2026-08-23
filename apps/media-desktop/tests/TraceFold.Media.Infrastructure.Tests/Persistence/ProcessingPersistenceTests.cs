using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Processing;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class ProcessingPersistenceTests
{
    private static readonly DateTimeOffset Instant =
        DateTimeOffset.Parse("2026-08-23T10:00:00Z");

    [Fact]
    public async Task Queue_is_idempotent_and_claims_only_one_job()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        ProcessingCandidate candidate = await SeedHashedPairAsync(database);
        PlannedProcessing planned = CreatePlan(candidate);
        var store = new EfProcessingStore(database.CreateContextFactory());

        QueueProcessingResult first = await store.QueueAsync(
            planned,
            TestContext.Current.CancellationToken);
        QueueProcessingResult duplicate = await store.QueueAsync(
            planned,
            TestContext.Current.CancellationToken);
        ProcessingJobRecord? claimed = await store.TryClaimNextAsync(
            TestContext.Current.CancellationToken);
        ProcessingJobRecord? none = await store.TryClaimNextAsync(
            TestContext.Current.CancellationToken);

        Assert.True(first.Created);
        Assert.False(duplicate.Created);
        Assert.Equal(first.Job.Id, duplicate.Job.Id);
        Assert.Equal(first.Job.Id, claimed!.Id);
        Assert.Equal(ProcessingJobStatus.Running, claimed.Status);
        Assert.Null(none);
    }

    [Fact]
    public async Task Concurrent_queue_requests_create_exactly_one_active_job()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        ProcessingCandidate candidate = await SeedHashedPairAsync(database);
        PlannedProcessing planned = CreatePlan(candidate);
        var store = new EfProcessingStore(database.CreateContextFactory());

        QueueProcessingResult[] results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => store.QueueAsync(
                planned,
                TestContext.Current.CancellationToken)));

        Assert.Single(results, result => result.Created);
        Assert.Single(results.Select(result => result.Job.Id).Distinct());
    }

    [Fact]
    public async Task Progress_persistence_is_monotonic()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        ProcessingCandidate candidate = await SeedHashedPairAsync(database);
        var store = new EfProcessingStore(database.CreateContextFactory());
        QueueProcessingResult queued = await store.QueueAsync(
            CreatePlan(candidate),
            TestContext.Current.CancellationToken);
        await store.TryClaimNextAsync(TestContext.Current.CancellationToken);

        await store.UpdateProgressAsync(
            queued.Job.Id,
            new ProcessingProgressUpdate("merge", 60),
            TestContext.Current.CancellationToken);
        await store.UpdateProgressAsync(
            queued.Job.Id,
            new ProcessingProgressUpdate("merge", 20),
            TestContext.Current.CancellationToken);

        ProcessingJobRecord active = (await store.FindActiveAsync(
            candidate.InboxItemId,
            TestContext.Current.CancellationToken))!;
        Assert.Equal(60, active.ProgressPercent);
        Assert.Equal("merge", active.CurrentStepKey);
    }

    [Fact]
    public async Task Successful_output_invalidates_the_older_output()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        using TestMediaFolders folders = TestMediaFolders.Create();
        await SaveFoldersAsync(database, folders);
        ProcessingCandidate candidate = await SeedHashedPairAsync(database);
        var store = new EfProcessingStore(database.CreateContextFactory());
        PlannedProcessing firstPlan = CreatePlan(candidate);
        await store.QueueAsync(firstPlan, TestContext.Current.CancellationToken);
        await store.TryClaimNextAsync(TestContext.Current.CancellationToken);
        ProcessedOutputDraft firstOutput = WriteOutput(folders, firstPlan.JobId, [1, 2, 3]);
        await store.CompleteAsync(
            firstPlan.JobId,
            firstOutput,
            TestContext.Current.CancellationToken);
        await database.ExecuteSqlAsync($"""
            UPDATE inbox_items
            SET state = 'requiresprocessing', required_capability = 'mp4_m4a_merge'
            WHERE id = '{GuidText(candidate.InboxItemId)}'
            """);
        ProcessingCandidate retryCandidate = (await store.LoadCandidateAsync(
            candidate.InboxItemId,
            TestContext.Current.CancellationToken))!;
        PlannedProcessing secondPlan = CreatePlan(retryCandidate);
        await store.QueueAsync(secondPlan, TestContext.Current.CancellationToken);
        await store.TryClaimNextAsync(TestContext.Current.CancellationToken);
        ProcessedOutputDraft secondOutput = WriteOutput(folders, secondPlan.JobId, [4, 5, 6]);

        await store.CompleteAsync(
            secondPlan.JobId,
            secondOutput,
            TestContext.Current.CancellationToken);

        ValidatedProcessingOutput active = (await store.LoadValidatedOutputAsync(
            candidate.InboxItemId,
            TestContext.Current.CancellationToken))!;
        Assert.Equal(secondPlan.JobId, active.Output.JobId);
        Assert.Equal(secondOutput.Sha256, active.Output.Sha256);
    }

    [Fact]
    public async Task Cancellation_only_succeeds_for_a_queued_job()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        ProcessingCandidate first = await SeedHashedPairAsync(database);
        ProcessingCandidate second = await SeedHashedPairAsync(database, "other");
        var store = new EfProcessingStore(database.CreateContextFactory());
        QueueProcessingResult firstQueued = await store.QueueAsync(
            CreatePlan(first),
            TestContext.Current.CancellationToken);
        QueueProcessingResult secondQueued = await store.QueueAsync(
            CreatePlan(second),
            TestContext.Current.CancellationToken);
        await store.TryClaimNextAsync(TestContext.Current.CancellationToken);

        bool runningCancelled = await store.CancelQueuedAsync(
            firstQueued.Job.Id,
            TestContext.Current.CancellationToken);
        bool queuedCancelled = await store.CancelQueuedAsync(
            secondQueued.Job.Id,
            TestContext.Current.CancellationToken);

        Assert.False(runningCancelled);
        Assert.True(queuedCancelled);
        ProcessingCandidate reloaded = (await store.LoadCandidateAsync(
            second.InboxItemId,
            TestContext.Current.CancellationToken))!;
        Assert.Equal(InboxItemState.RequiresProcessing, reloaded.State);
    }

    [Fact]
    public async Task Recovery_interrupts_every_stale_queued_or_running_job()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        ProcessingCandidate first = await SeedHashedPairAsync(database);
        ProcessingCandidate second = await SeedHashedPairAsync(database, "other");
        var store = new EfProcessingStore(database.CreateContextFactory());
        await store.QueueAsync(CreatePlan(first), TestContext.Current.CancellationToken);
        await store.QueueAsync(CreatePlan(second), TestContext.Current.CancellationToken);
        await store.TryClaimNextAsync(TestContext.Current.CancellationToken);

        IReadOnlyList<ProcessingJobRecord> interrupted =
            await store.InterruptRecoverableAsync(
                "processing.startup_interrupted",
                TestContext.Current.CancellationToken);

        Assert.Equal(2, interrupted.Count);
        Assert.All(interrupted, job =>
            Assert.Equal(ProcessingJobStatus.Interrupted, job.Status));
        Assert.Equal(
            InboxItemState.ProcessingInterrupted.ToString().ToLowerInvariant(),
            await database.ReadInboxStateAsync(first.InboxItemId));
        Assert.Equal(
            InboxItemState.ProcessingInterrupted.ToString().ToLowerInvariant(),
            await database.ReadInboxStateAsync(second.InboxItemId));
        Assert.Null(await store.TryClaimNextAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Source_invalidation_returns_the_running_job_and_resets_the_item()
    {
        await using TestMediaDatabase database = await CreateDatabaseAsync();
        ProcessingCandidate candidate = await SeedHashedPairAsync(database);
        var store = new EfProcessingStore(database.CreateContextFactory());
        QueueProcessingResult queued = await store.QueueAsync(
            CreatePlan(candidate),
            TestContext.Current.CancellationToken);
        await store.TryClaimNextAsync(TestContext.Current.CancellationToken);

        Guid? runningJobId = await store.InvalidateForSourceChangeAsync(
            candidate.InboxItemId,
            TestContext.Current.CancellationToken);

        Assert.Equal(queued.Job.Id, runningJobId);
        ProcessingCandidate reloaded = (await store.LoadCandidateAsync(
            candidate.InboxItemId,
            TestContext.Current.CancellationToken))!;
        Assert.Equal(InboxItemState.WaitingForStability, reloaded.State);
        Assert.Null(await store.FindActiveAsync(
            candidate.InboxItemId,
            TestContext.Current.CancellationToken));
    }

    private static async Task<TestMediaDatabase> CreateDatabaseAsync()
    {
        TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        return database;
    }

    private static async Task<ProcessingCandidate> SeedHashedPairAsync(
        TestMediaDatabase database,
        string basename = "paired")
    {
        Guid itemId = Guid.NewGuid();
        Guid videoId = Guid.NewGuid();
        Guid audioId = Guid.NewGuid();
        await database.ExecuteSqlAsync(
            $"""
            INSERT INTO inbox_items
                (id, state, media_kind, actual_filename, grouping_rule,
                 required_capability, is_retryable, created_at_utc, updated_at_utc)
            VALUES
                ('{GuidText(itemId)}', 'requiresprocessing', 'video', '{basename}.mp4', 'mp4_m4a',
                 'mp4_m4a_merge', 0,
                 '{Instant:O}', '{Instant:O}')
            """,
            SourceSql(
                videoId,
                itemId,
                $"C:/inbox/{basename}.mp4",
                $"{basename}.mp4",
                ".mp4",
                100,
                new string('A', 64),
                true),
            SourceSql(
                audioId,
                itemId,
                $"C:/inbox/{basename}.m4a",
                $"{basename}.m4a",
                ".m4a",
                50,
                new string('B', 64),
                false));
        var store = new EfProcessingStore(database.CreateContextFactory());
        return (await store.LoadCandidateAsync(
            itemId,
            TestContext.Current.CancellationToken))!;
    }

    private static string SourceSql(
        Guid id,
        Guid itemId,
        string path,
        string filename,
        string extension,
        long size,
        string sha256,
        bool primary) => $"""
        INSERT INTO source_files
            (id, inbox_item_id, normalized_path, active_path_key, filename, extension,
             size, last_write_time_utc, first_observed_at_utc,
             stable_observation_count, sha256, is_present, is_primary)
        VALUES
            ('{GuidText(id)}', '{GuidText(itemId)}',
             '{path.ToUpperInvariant()}', '{path.ToUpperInvariant()}',
             '{filename}', '{extension}', {size}, '{Instant:O}', '{Instant:O}',
             2, '{sha256}', 1, {(primary ? 1 : 0)})
        """;

    private static string GuidText(Guid value) =>
        value.ToString().ToUpperInvariant();

    private static PlannedProcessing CreatePlan(ProcessingCandidate candidate)
    {
        Guid jobId = Guid.NewGuid();
        var plan = new ProcessingPlanDefinition(
            Guid.NewGuid(),
            jobId,
            candidate.InboxItemId,
            "merge.v1",
            "1",
            "合并视频和音频",
            "mp4_m4a_merge",
            candidate.ActualFilename,
            Path.Combine(".tracefold", "processing", jobId.ToString("N")),
            ["check_sources", "merge", "verify_output"],
            candidate.Sources.Select((source, index) => new ProcessingPlanInput(
                source.SourceFileId,
                index == 0 ? "video" : "audio",
                source.FullPath,
                source.Filename,
                source.Size,
                source.LastWriteTimeUtc,
                source.Sha256!)).ToArray());
        return new PlannedProcessing(jobId, candidate.PreviousAttempts + 1, plan);
    }

    private static async Task SaveFoldersAsync(
        TestMediaDatabase database,
        TestMediaFolders folders) =>
        await new EfDirectorySettingsStore(database.CreateContextFactory()).SaveAsync(
            folders.Value,
            TestContext.Current.CancellationToken);

    private static ProcessedOutputDraft WriteOutput(
        TestMediaFolders folders,
        Guid jobId,
        byte[] bytes)
    {
        string directory = Directory.CreateDirectory(Path.Combine(
            folders.Work,
            ".tracefold",
            "processing",
            jobId.ToString("N"))).FullName;
        string fullPath = Path.Combine(directory, "paired.mp4");
        File.WriteAllBytes(fullPath, bytes);
        FileInfo info = new(fullPath);
        string sha256 = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(bytes));
        return new ProcessedOutputDraft(
            fullPath,
            Path.GetRelativePath(folders.Work, fullPath),
            info.Length,
            info.LastWriteTimeUtc,
            sha256,
            TimeSpan.FromSeconds(1),
            HasVideo: true,
            HasAudio: true);
    }
}
