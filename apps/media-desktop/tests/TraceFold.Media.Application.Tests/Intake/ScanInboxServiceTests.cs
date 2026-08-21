using Microsoft.Extensions.Logging.Abstractions;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Startup;
using TraceFold.Media.Domain.Inbox;
using Xunit;

namespace TraceFold.Media.Application.Tests.Intake;

public sealed class ScanInboxServiceTests
{
    private static readonly DateTimeOffset Instant =
        DateTimeOffset.Parse("2026-08-22T10:00:00Z");

    [Fact]
    public async Task Repeated_refresh_creates_one_active_item()
    {
        var store = new InMemoryInboxScanStore();
        ScanInboxService service = CreateService(
            store,
            new SnapshotCatalog(Snapshot("C:/inbox/photo.jpg", 10, Instant)));

        await service.ScanAsync(ScanTrigger.Manual, CancellationToken.None);
        await service.ScanAsync(ScanTrigger.Manual, CancellationToken.None);
        await service.ScanAsync(ScanTrigger.Manual, CancellationToken.None);

        InboxItem item = Assert.Single(store.ActiveItems);
        Assert.Equal(InboxItemState.ReadyToTag, item.State);
    }

    [Fact]
    public async Task Concurrent_triggers_share_one_active_scan()
    {
        var catalog = new BlockingInboxFileCatalog(
            Snapshot("C:/inbox/photo.jpg", 10, Instant));
        ScanInboxService service = CreateService(new InMemoryInboxScanStore(), catalog);

        Task<ScanResult> first = service.ScanAsync(
            ScanTrigger.Manual,
            CancellationToken.None);
        await catalog.Entered;
        Task<ScanResult> second = service.ScanAsync(
            ScanTrigger.Automatic,
            CancellationToken.None);
        catalog.Release();

        Assert.Equal(await first, await second);
        Assert.Equal(1, catalog.CallCount);
    }

    [Fact]
    public async Task Catalog_failure_returns_failed_without_modifying_existing_items()
    {
        var store = new InMemoryInboxScanStore();
        ScanInboxService healthy = CreateService(
            store,
            new SnapshotCatalog(Snapshot("C:/inbox/photo.jpg", 10, Instant)));
        await healthy.ScanAsync(ScanTrigger.Manual, CancellationToken.None);
        await healthy.ScanAsync(ScanTrigger.StabilityCheck, CancellationToken.None);
        InboxItem existing = Assert.Single(store.ActiveItems);
        int reconciliationsBeforeFailure = store.ReconcileCallCount;
        ScanInboxService failing = CreateService(store, new ThrowingInboxFileCatalog());

        ScanResult result = await failing.ScanAsync(
            ScanTrigger.Manual,
            CancellationToken.None);

        Assert.Equal(ScanOutcome.Failed, result.Outcome);
        Assert.Equal(reconciliationsBeforeFailure, store.ReconcileCallCount);
        Assert.Same(existing, Assert.Single(store.ActiveItems));
        Assert.Equal(InboxItemState.ReadyToTag, existing.State);
    }

    private static ScanInboxService CreateService(
        IInboxScanStore store,
        IInboxFileCatalog catalog)
    {
        var settings = new DirectorySettingsService(
            new AlwaysValidDirectoryProbe(),
            new FixedDirectorySettingsStore(
                new LibraryFolders("C:/inbox", "C:/library", "C:/work")));
        return new ScanInboxService(
            catalog,
            new CandidateGroupingService(
                [new Mp4M4aGroupingRule(), new DirectMediaGroupingRule()]),
            settings,
            store,
            new RecordingActivityNotifier(),
            new FixedTimeProvider(Instant),
            new NeverStoppingLifetimeToken(),
            NullLogger<ScanInboxService>.Instance);
    }

    private static FileSnapshot Snapshot(
        string path,
        long size,
        DateTimeOffset lastWriteTimeUtc) =>
        new(
            path,
            Path.GetFileName(path),
            Path.GetExtension(path),
            new FileFingerprint(size, lastWriteTimeUtc));

    private sealed class SnapshotCatalog(params FileSnapshot[] snapshots)
        : IInboxFileCatalog
    {
        public Task<IReadOnlyList<FileSnapshot>> EnumerateTopLevelAsync(
            string inboxDirectory,
            IReadOnlySet<string> observedExtensions,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FileSnapshot>>(snapshots);
    }

    private sealed class BlockingInboxFileCatalog(params FileSnapshot[] snapshots)
        : IInboxFileCatalog
    {
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public int CallCount { get; private set; }

        public void Release() => _release.TrySetResult();

        public async Task<IReadOnlyList<FileSnapshot>> EnumerateTopLevelAsync(
            string inboxDirectory,
            IReadOnlySet<string> observedExtensions,
            CancellationToken cancellationToken)
        {
            CallCount++;
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return snapshots;
        }
    }

    private sealed class ThrowingInboxFileCatalog : IInboxFileCatalog
    {
        public Task<IReadOnlyList<FileSnapshot>> EnumerateTopLevelAsync(
            string inboxDirectory,
            IReadOnlySet<string> observedExtensions,
            CancellationToken cancellationToken) =>
            throw new IOException("catalog unavailable");
    }

    private sealed class InMemoryInboxScanStore : IInboxScanStore
    {
        private readonly Dictionary<string, TrackedItem> _items =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTimeOffset> _firstObserved =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<InboxItem> ActiveItems =>
            _items.Values.Select(value => value.Item).ToArray();

        public int ReconcileCallCount { get; private set; }

        public Task<IReadOnlyDictionary<string, DateTimeOffset>> LoadFirstObservedAsync(
            IReadOnlySet<string> normalizedPaths,
            CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, DateTimeOffset> result = _firstObserved
                .Where(pair => normalizedPaths.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(result);
        }

        public Task<ScanPersistenceResult> ReconcileAsync(
            ScanReconciliation reconciliation,
            CancellationToken cancellationToken)
        {
            ReconcileCallCount++;
            int created = 0;
            int updated = 0;
            foreach (CandidateGroup group in reconciliation.Groups)
            {
                ObservedFile primary = group.Sources[0];
                foreach (ObservedFile source in group.Sources)
                {
                    _firstObserved.TryAdd(
                        Normalize(source.FullPath),
                        source.FirstObservedAtUtc);
                }

                if (!_items.TryGetValue(group.GroupKey, out TrackedItem? tracked))
                {
                    tracked = new TrackedItem(
                        InboxItem.Discover(Guid.NewGuid()),
                        primary.Fingerprint,
                        StableCount: 1);
                    _items.Add(group.GroupKey, tracked);
                    created++;
                    continue;
                }

                int stableCount = tracked.Fingerprint == primary.Fingerprint
                    ? tracked.StableCount + 1
                    : 1;
                if (tracked.Fingerprint != primary.Fingerprint)
                {
                    tracked.Item.ResetForSourceChange();
                }

                if (stableCount >= 2
                    && tracked.Item.State == InboxItemState.WaitingForStability)
                {
                    tracked.Item.MarkAnalyzing();
                    tracked.Item.MarkReady();
                }

                _items[group.GroupKey] = tracked with
                {
                    Fingerprint = primary.Fingerprint,
                    StableCount = stableCount
                };
                updated++;
            }

            bool waiting = _items.Values.Any(item =>
                item.Item.State == InboxItemState.WaitingForStability);
            return Task.FromResult(new ScanPersistenceResult(
                created,
                updated,
                waiting));
        }

        public Task<bool> HasWaitingCandidatesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_items.Values.Any(item =>
                item.Item.State == InboxItemState.WaitingForStability));

        public Task ClearMissingAsync(Guid inboxItemId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        private static string Normalize(string path) =>
            Path.GetFullPath(path).ToUpperInvariant();

        private sealed record TrackedItem(
            InboxItem Item,
            FileFingerprint Fingerprint,
            int StableCount);
    }

    private sealed class RecordingActivityNotifier : IInboxActivityNotifier
    {
        public event EventHandler<InboxActivityEventArgs>? Changed;

        public void Publish(InboxActivityKind kind) =>
            Changed?.Invoke(this, new InboxActivityEventArgs(kind));

        public Task WaitForCandidateAsync(CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class FixedDirectorySettingsStore(LibraryFolders folders)
        : IDirectorySettingsStore
    {
        public Task<LibraryFolders?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<LibraryFolders?>(folders);

        public Task SaveAsync(
            LibraryFolders value,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class AlwaysValidDirectoryProbe : IDirectoryProbe
    {
        public ValueTask<bool> ExistsAsync(
            string path,
            CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public ValueTask<bool> CanWriteAsync(
            string path,
            CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }

    private sealed class NeverStoppingLifetimeToken : IApplicationLifetimeToken
    {
        public CancellationToken ApplicationStopping => CancellationToken.None;
    }
}
