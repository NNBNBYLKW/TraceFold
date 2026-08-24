using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class EfInboxQueryStoreRecoveryTests
{
    private static readonly DateTimeOffset Instant =
        DateTimeOffset.Parse("2026-08-24T09:00:00Z");
    private const string Hash =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task Recovery_blocked_details_expose_stage_reason_and_actions()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        (Guid itemId, IngestOperationRecord operation, EfIngestStore ingest) =
            await CreateOperationAsync(database);
        await ingest.MarkStagedAsync(
            operation.Id,
            new StagedFile("C:/work/payload", 3, Hash),
            TestContext.Current.CancellationToken);
        await ingest.MarkRecoveryBlockedAsync(
            operation.Id,
            "recovery.identity_ambiguous",
            TestContext.Current.CancellationToken);
        var query = new EfInboxQueryStore(database.CreateContextFactory());

        InboxDetailsDto details = (await query.GetDetailsAsync(
            itemId,
            TestContext.Current.CancellationToken))!;

        Assert.Equal(IngestOperationStage.RecoveryBlocked, details.Recovery!.Stage);
        Assert.Equal(IngestOperationStage.Staged, details.Recovery.BlockedFromStage);
        Assert.Equal("recovery.identity_ambiguous", details.Recovery.ErrorCode);
        Assert.True(details.AllowedActions.HasFlag(InboxAction.RecheckRecovery));
        Assert.False(details.AllowedActions.HasFlag(InboxAction.RetryCleanup));
    }

    [Fact]
    public async Task Cleanup_pending_details_expose_attempt_and_next_due_time()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        (Guid itemId, IngestOperationRecord operation, EfIngestStore ingest) =
            await CreateOperationAsync(database);
        var staged = new StagedFile("C:/work/payload", 3, Hash);
        var committed = new CommittedFile(
            "C:/library/photo.jpg", "photo.jpg", 3, Hash);
        await ingest.MarkStagedAsync(
            operation.Id, staged, TestContext.Current.CancellationToken);
        await ingest.MarkFileCommittedAsync(
            operation.Id, committed, TestContext.Current.CancellationToken);
        await ingest.CommitAssetAsync(
            operation.Id, committed, TestContext.Current.CancellationToken);
        DateTimeOffset due = Instant.AddMinutes(10);
        await ingest.ScheduleCleanupRetryAsync(
            operation.Id,
            "ingest.cleanup_failed",
            3,
            due,
            TestContext.Current.CancellationToken);
        var query = new EfInboxQueryStore(database.CreateContextFactory());

        InboxDetailsDto details = (await query.GetDetailsAsync(
            itemId,
            TestContext.Current.CancellationToken))!;

        Assert.Equal(IngestOperationStage.CleanupPending, details.Recovery!.Stage);
        Assert.Equal(3, details.Recovery.CleanupAttemptCount);
        Assert.Equal(due, details.Recovery.NextCleanupAttemptAtUtc);
        Assert.True(details.AllowedActions.HasFlag(InboxAction.RetryCleanup));
        Assert.False(details.AllowedActions.HasFlag(InboxAction.RecheckRecovery));
    }

    [Fact]
    public async Task Completed_details_have_no_recovery_summary()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        (Guid itemId, IngestOperationRecord operation, EfIngestStore ingest) =
            await CreateOperationAsync(database);
        var committed = new CommittedFile(
            "C:/library/photo.jpg", "photo.jpg", 3, Hash);
        await ingest.MarkStagedAsync(
            operation.Id,
            new StagedFile("C:/work/payload", 3, Hash),
            TestContext.Current.CancellationToken);
        await ingest.MarkFileCommittedAsync(
            operation.Id, committed, TestContext.Current.CancellationToken);
        await ingest.CommitAssetAsync(
            operation.Id, committed, TestContext.Current.CancellationToken);
        await ingest.CompleteAsync(
            operation.Id,
            TestContext.Current.CancellationToken);
        var query = new EfInboxQueryStore(database.CreateContextFactory());

        InboxDetailsDto details = (await query.GetDetailsAsync(
            itemId,
            TestContext.Current.CancellationToken))!;

        Assert.Null(details.Recovery);
    }

    [Fact]
    public async Task Conflict_card_projects_the_same_actions_and_metadata_version_as_details()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid itemId = await database.SeedInboxItemAsync(
            InboxItemState.ConflictBlocked,
            metadataVersion: 7);
        var store = new EfInboxQueryStore(database.CreateContextFactory());

        InboxCardDto card = Assert.Single(await store.SearchAsync(
            new InboxQuery(null, null), TestContext.Current.CancellationToken));
        InboxDetailsDto details = Assert.IsType<InboxDetailsDto>(
            await store.GetDetailsAsync(
                itemId, TestContext.Current.CancellationToken));

        Assert.Equal(7, card.MetadataVersion);
        Assert.Equal(details.AllowedActions, card.AllowedActions);
        Assert.True(card.AllowedActions.HasFlag(InboxAction.ResolveConflict));
    }

    private static async Task<(
        Guid ItemId,
        IngestOperationRecord Operation,
        EfIngestStore Store)> CreateOperationAsync(TestMediaDatabase database)
    {
        string path = "C:/inbox/photo.jpg";
        var source = new ObservedFile(
            path,
            "photo.jpg",
            ".jpg",
            new FileFingerprint(3, Instant),
            Instant);
        var reconciliation = new ScanReconciliation(
            "C:/inbox",
            Instant,
            [
                new CandidateGroup(
                    "direct_media",
                    path.ToUpperInvariant(),
                    MediaKind.Image,
                    CandidateDisposition.Direct,
                    [source],
                    null)
            ],
            new HashSet<string>(
                [Path.GetFullPath(path).ToUpperInvariant()],
                StringComparer.OrdinalIgnoreCase));
        var scan = new EfInboxScanStore(database.CreateContextFactory());
        await scan.ReconcileAsync(
            reconciliation,
            TestContext.Current.CancellationToken);
        await scan.ReconcileAsync(
            reconciliation with { ObservedAtUtc = Instant.AddSeconds(2) },
            TestContext.Current.CancellationToken);
        Guid itemId = (await database.ReadSingleInboxAsync()).Id;
        var ingest = new EfIngestStore(database.CreateContextFactory());
        IngestCandidate candidate = (await ingest.LoadCandidateAsync(
            itemId,
            TestContext.Current.CancellationToken))!;
        var preparation = new IngestPreparation(
            candidate.InboxItemId,
            candidate.MediaKind,
            candidate.Sources[0].FullPath,
            candidate.Sources[0].Size,
            candidate.Sources[0].LastWriteTimeUtc,
            Hash,
            candidate.ActualFilename,
            null,
            [],
            null,
            [],
            candidate.MetadataVersion);
        IngestOperationRecord operation = await ingest.CreateAsync(
            preparation,
            IngestExecutionPlan.Create(candidate.ActualFilename),
            TestContext.Current.CancellationToken);
        return (itemId, operation, ingest);
    }
}
