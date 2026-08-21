using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class EfInboxScanStoreTests
{
    private static readonly DateTimeOffset Instant =
        DateTimeOffset.Parse("2026-08-22T10:00:00Z");

    [Fact]
    public async Task Two_unchanged_observations_promote_one_item_to_ready()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        var store = new EfInboxScanStore(database.CreateContextFactory());

        ScanPersistenceResult first = await store.ReconcileAsync(
            DirectReconciliation(size: 10, Instant),
            CancellationToken.None);
        Guid firstId = (await database.ReadSingleInboxAsync()).Id;
        ScanPersistenceResult second = await store.ReconcileAsync(
            DirectReconciliation(size: 10, Instant.AddSeconds(2)),
            CancellationToken.None);

        PersistedInboxSnapshot item = await database.ReadSingleInboxAsync();
        Assert.Equal(1, first.CreatedItems);
        Assert.Equal(1, second.UpdatedItems);
        Assert.Equal(firstId, item.Id);
        Assert.Equal("readytotag", item.State);
        Assert.Equal(2, item.PrimaryStableObservationCount);
    }

    [Fact]
    public async Task Changed_source_resets_stability_but_preserves_identity_and_draft_version()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        var store = new EfInboxScanStore(database.CreateContextFactory());
        await store.ReconcileAsync(DirectReconciliation(10, Instant), CancellationToken.None);
        await store.ReconcileAsync(
            DirectReconciliation(10, Instant.AddSeconds(2)),
            CancellationToken.None);
        PersistedInboxSnapshot before = await database.ReadSingleInboxAsync();
        await database.SetDraftVersionAsync(before.Id, 7);

        await store.ReconcileAsync(
            DirectReconciliation(20, Instant.AddSeconds(4)),
            CancellationToken.None);

        PersistedInboxSnapshot after = await database.ReadSingleInboxAsync();
        Assert.Equal(before.Id, after.Id);
        Assert.Equal("waitingforstability", after.State);
        Assert.Equal(1, after.PrimaryStableObservationCount);
        Assert.Equal(20, after.PrimarySourceSize);
        Assert.Null(after.PrimarySha256);
        Assert.Equal(7, after.DraftVersion);
    }

    [Fact]
    public async Task Missing_source_is_retained_until_explicitly_cleared()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        var store = new EfInboxScanStore(database.CreateContextFactory());
        await store.ReconcileAsync(DirectReconciliation(10, Instant), CancellationToken.None);
        Guid id = (await database.ReadSingleInboxAsync()).Id;

        await store.ReconcileAsync(
            new ScanReconciliation(
                "C:/inbox",
                Instant.AddSeconds(2),
                [],
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)),
            CancellationToken.None);

        PersistedInboxSnapshot missing = await database.ReadSingleInboxAsync();
        Assert.Equal("sourcemissing", missing.State);
        Assert.False(missing.PrimaryIsPresent);
        Assert.Equal(1, await database.CountInboxItemsAsync());

        await store.ClearMissingAsync(id, CancellationToken.None);

        Assert.Equal(0, await database.CountInboxItemsAsync());
    }

    [Fact]
    public async Task Late_m4a_reuses_mp4_item_and_becomes_requires_processing_when_stable()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        var store = new EfInboxScanStore(database.CreateContextFactory());
        await store.ReconcileAsync(Mp4OnlyReconciliation(Instant), CancellationToken.None);
        Guid originalId = (await database.ReadSingleInboxAsync()).Id;

        await store.ReconcileAsync(PairReconciliation(Instant.AddSeconds(2)), CancellationToken.None);
        await store.ReconcileAsync(PairReconciliation(Instant.AddSeconds(4)), CancellationToken.None);

        PersistedInboxSnapshot item = await database.ReadSingleInboxAsync();
        Assert.Equal(originalId, item.Id);
        Assert.Equal("requiresprocessing", item.State);
        Assert.Equal("mp4_m4a_merge", item.RequiredCapability);
        Assert.Equal(2, item.SourceCount);
    }

    private static ScanReconciliation DirectReconciliation(
        long size,
        DateTimeOffset observedAtUtc)
    {
        ObservedFile source = File("C:/inbox/photo.jpg", size, Instant);
        return Reconciliation(
            observedAtUtc,
            new CandidateGroup(
                "direct_media",
                source.FullPath.ToUpperInvariant(),
                MediaKind.Image,
                CandidateDisposition.Direct,
                [source],
                null),
            [source]);
    }

    private static ScanReconciliation Mp4OnlyReconciliation(DateTimeOffset observedAtUtc)
    {
        ObservedFile source = File("C:/inbox/clip.mp4", 10, Instant);
        return Reconciliation(
            observedAtUtc,
            new CandidateGroup(
                "mp4_m4a",
                source.FullPath.ToUpperInvariant(),
                MediaKind.Video,
                CandidateDisposition.WaitingForCompanion,
                [source],
                null),
            [source]);
    }

    private static ScanReconciliation PairReconciliation(DateTimeOffset observedAtUtc)
    {
        ObservedFile video = File("C:/inbox/clip.mp4", 10, Instant);
        ObservedFile audio = File("C:/inbox/clip.m4a", 5, Instant.AddSeconds(2));
        return Reconciliation(
            observedAtUtc,
            new CandidateGroup(
                "mp4_m4a",
                "CLIP",
                MediaKind.Video,
                CandidateDisposition.RequiresProcessing,
                [video, audio],
                "mp4_m4a_merge"),
            [video, audio]);
    }

    private static ScanReconciliation Reconciliation(
        DateTimeOffset observedAtUtc,
        CandidateGroup group,
        IReadOnlyList<ObservedFile> sources) =>
        new(
            "C:/inbox",
            observedAtUtc,
            [group],
            sources
                .Select(source => Path.GetFullPath(source.FullPath).ToUpperInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase));

    private static ObservedFile File(
        string path,
        long size,
        DateTimeOffset lastWriteTimeUtc) =>
        new(
            path,
            Path.GetFileName(path),
            Path.GetExtension(path),
            new FileFingerprint(size, lastWriteTimeUtc),
            Instant);
}
