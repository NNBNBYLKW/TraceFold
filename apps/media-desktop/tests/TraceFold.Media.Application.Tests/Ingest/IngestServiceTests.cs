using Microsoft.Extensions.Logging.Abstractions;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Application.Tests.Ingest;

public sealed class IngestServiceTests
{
    [Fact]
    public async Task IngestAsync_commits_asset_before_deleting_source()
    {
        await using IngestFixture fixture = await IngestFixture.ReadyOrdinaryAsync();

        IngestResult result = await fixture.Service.IngestAsync(
            fixture.ItemId,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.Completed, result.Outcome);
        Assert.True(fixture.Store.AssetCommitted);
        Assert.True(fixture.Storage.DeleteObservedAfterAssetCommit);
        Assert.False(fixture.Storage.SourceExists);
    }

    [Theory]
    [InlineData(ConflictKind.Name)]
    [InlineData(ConflictKind.RegisteredContent)]
    public async Task IngestAsync_blocks_conflict_before_staging(ConflictKind kind)
    {
        await using IngestFixture fixture = await IngestFixture.WithConflictAsync(kind);

        IngestResult result = await fixture.Service.IngestAsync(
            fixture.ItemId,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.ConflictBlocked, result.Outcome);
        Assert.Null(result.OperationId);
        Assert.Equal(0, fixture.Storage.StageCallCount);
        Assert.True(fixture.Storage.SourceExists);
    }

    private sealed class IngestFixture : IAsyncDisposable
    {
        private readonly string _root;

        private IngestFixture(
            string root,
            Guid itemId,
            RecordingIngestStore store,
            RecordingStorage storage,
            IngestService service)
        {
            _root = root;
            ItemId = itemId;
            Store = store;
            Storage = storage;
            Service = service;
        }

        public Guid ItemId { get; }

        public RecordingIngestStore Store { get; }

        public RecordingStorage Storage { get; }

        public IngestService Service { get; }

        public static Task<IngestFixture> ReadyOrdinaryAsync() => CreateAsync(null);

        public static Task<IngestFixture> WithConflictAsync(ConflictKind kind) =>
            CreateAsync(kind);

        public ValueTask DisposeAsync()
        {
            Directory.Delete(_root, recursive: true);
            return ValueTask.CompletedTask;
        }

        private static async Task<IngestFixture> CreateAsync(ConflictKind? conflict)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                $"tracefold-ingest-unit-{Guid.NewGuid():N}");
            string inbox = Directory.CreateDirectory(Path.Combine(root, "inbox")).FullName;
            string library = Directory.CreateDirectory(Path.Combine(root, "library")).FullName;
            string work = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
            string source = Path.Combine(inbox, "photo.jpg");
            await File.WriteAllBytesAsync(
                source,
                [1, 2, 3],
                TestContext.Current.CancellationToken);
            var info = new FileInfo(source);
            Guid itemId = Guid.NewGuid();
            var candidate = new IngestCandidate(
                itemId,
                InboxItemState.ReadyToTag,
                MediaKind.Image,
                "photo.jpg",
                null,
                [
                    new IngestSourceRecord(
                        source,
                        info.Length,
                        new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                        IsPresent: true,
                        IsPrimary: true)
                ],
                MetadataSelection.Create("display", [], null, []),
                MetadataVersion: 0);
            var store = new RecordingIngestStore(candidate, conflict);
            var storage = new RecordingStorage(store, conflict == ConflictKind.Name);
            var settings = new FixedSettingsStore(
                new LibraryFolders(inbox, library, work));
            var service = new IngestService(
                new CompletedScanner(),
                store,
                settings,
                new TestInputResolver(),
                storage,
                new IngestCleanupService(store, storage),
                new MediaOperationCoordinator(),
                NullLogger<IngestService>.Instance);
            return new IngestFixture(root, itemId, store, storage, service);
        }
    }

    private sealed class RecordingIngestStore(
        IngestCandidate candidate,
        ConflictKind? conflict) : IIngestStore
    {
        private IngestOperationRecord? _operation;

        public bool AssetCommitted { get; private set; }

        public Task<IngestCandidate?> LoadCandidateAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IngestCandidate?>(candidate);

        public Task<IngestOperationRecord?> FindActiveAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) => Task.FromResult(_operation);

        public Task<ConflictCheck> CheckConflictsAsync(
            string targetFilename,
            string sourceSha256,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ConflictCheck(
                conflict == ConflictKind.RegisteredContent ? conflict : null));

        public Task BlockConflictAsync(
            Guid inboxItemId,
            ConflictKind kind,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IngestOperationRecord> CreateAsync(
            IngestPreparation preparation,
            CancellationToken cancellationToken)
        {
            _operation = new IngestOperationRecord(
                Guid.NewGuid(),
                preparation.InboxItemId,
                IngestOperationStage.Created,
                preparation.SourcePath,
                preparation.SourceSize,
                preparation.SourceLastWriteTimeUtc,
                preparation.SourceSha256,
                preparation.TargetFilename,
                null,
                null,
                null);
            return Task.FromResult(_operation);
        }

        public Task MarkStagedAsync(
            Guid operationId,
            StagedFile staged,
            CancellationToken cancellationToken)
        {
            _operation = _operation! with
            {
                Stage = IngestOperationStage.Staged,
                StagedPath = staged.FullPath
            };
            return Task.CompletedTask;
        }

        public Task AbandonForRetryAsync(
            Guid operationId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkFileCommittedAsync(
            Guid operationId,
            CommittedFile committed,
            CancellationToken cancellationToken)
        {
            _operation = _operation! with { Stage = IngestOperationStage.FileCommitted };
            return Task.CompletedTask;
        }

        public Task<Guid> CommitAssetAsync(
            Guid operationId,
            CommittedFile committed,
            CancellationToken cancellationToken)
        {
            AssetCommitted = true;
            Guid assetId = Guid.NewGuid();
            _operation = _operation! with
            {
                Stage = IngestOperationStage.DatabaseCommitted,
                AssetId = assetId
            };
            return Task.FromResult(assetId);
        }

        public Task MarkCleanupPendingAsync(
            Guid operationId,
            string errorCode,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CompleteAsync(
            Guid operationId,
            CancellationToken cancellationToken)
        {
            _operation = _operation! with { Stage = IngestOperationStage.Completed };
            return Task.CompletedTask;
        }

        public Task FailAsync(
            Guid operationId,
            string errorCode,
            bool isRetryable,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<IngestOperationRecord>> LoadIncompleteAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IngestOperationRecord>>([]);
    }

    private sealed class RecordingStorage(
        RecordingIngestStore store,
        bool targetExists) : IControlledMediaStorage
    {
        public int StageCallCount { get; private set; }

        public bool SourceExists { get; private set; } = true;

        public bool DeleteObservedAfterAssetCommit { get; private set; }

        public Task<bool> TargetExistsAsync(
            LibraryFolders folders,
            string targetFilename,
            CancellationToken cancellationToken) => Task.FromResult(targetExists);

        public Task<StagedFile> StageAsync(
            Guid operationId,
            LibraryFolders folders,
            string sourcePath,
            string expectedSha256,
            CancellationToken cancellationToken)
        {
            StageCallCount++;
            return Task.FromResult(new StagedFile(
                Path.Combine(folders.WorkDirectory, "payload"),
                3,
                expectedSha256));
        }

        public Task<CommittedFile> CommitAsync(
            Guid operationId,
            LibraryFolders folders,
            StagedFile staged,
            string targetFilename,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CommittedFile(
                Path.Combine(folders.LibraryDirectory, targetFilename),
                targetFilename,
                staged.Size,
                staged.Sha256));

        public Task<SourceDeleteResult> DeleteSourceIfMatchesAsync(
            LibraryFolders folders,
            string sourcePath,
            long expectedSize,
            string expectedSha256,
            CancellationToken cancellationToken)
        {
            DeleteObservedAfterAssetCommit = store.AssetCommitted;
            SourceExists = false;
            return Task.FromResult(new SourceDeleteResult(SourceDeleteOutcome.Deleted));
        }

        public Task<bool> VerifyCommittedAsync(
            LibraryFolders folders,
            CommittedFile committed,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task CleanupTemporaryFilesAsync(
            Guid operationId,
            LibraryFolders folders,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedHasher : IFileHasher
    {
        public Task<string> ComputeSha256Async(
            string path,
            CancellationToken cancellationToken) =>
            Task.FromResult(new string('A', 64));
    }

    private sealed class TestInputResolver : IIngestInputResolver
    {
        public Task<PreparedIngestInput> ResolveAsync(
            IngestCandidate candidate,
            CancellationToken cancellationToken)
        {
            IngestSourceRecord source = candidate.Sources.Single();
            string hash = new('A', 64);
            var cleanup = new IngestCleanupFile(
                IngestCleanupKind.InboxSource,
                source.FullPath,
                source.Size,
                hash);
            return Task.FromResult(new PreparedIngestInput(
                cleanup.Kind,
                source.FullPath,
                source.Size,
                source.LastWriteTimeUtc,
                hash,
                [cleanup]));
        }
    }

    private sealed class FixedSettingsStore(LibraryFolders folders)
        : IDirectorySettingsStore
    {
        public Task<LibraryFolders?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<LibraryFolders?>(folders);

        public Task SaveAsync(
            LibraryFolders value,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CompletedScanner : IInboxScanner
    {
        public Task<ScanResult> ScanAsync(
            ScanTrigger trigger,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ScanResult(
                ScanOutcome.Completed,
                1,
                0,
                1,
                DateTimeOffset.UtcNow,
                "ok"));
    }
}
