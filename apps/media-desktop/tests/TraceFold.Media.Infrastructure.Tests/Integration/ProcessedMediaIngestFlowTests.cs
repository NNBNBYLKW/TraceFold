using Microsoft.Extensions.Logging.Abstractions;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Infrastructure.Filesystem;
using TraceFold.Media.Infrastructure.MediaTools;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Integration;

public sealed class ProcessedMediaIngestFlowTests
{
    [Fact]
    public async Task Successful_processed_ingest_cleans_both_originals_and_output()
    {
        await using ProcessedFixture fixture = await ProcessedFixture.CreateAsync();

        IngestResult result = await fixture.IngestAsync(failCommit: false);

        Assert.Equal(IngestOutcome.Completed, result.Outcome);
        Assert.False(File.Exists(fixture.InboxMp4));
        Assert.False(File.Exists(fixture.InboxM4a));
        Assert.True(File.Exists(Path.Combine(fixture.Folders.Library, "paired.mp4")));
        Assert.False(File.Exists(fixture.ProcessedOutputPath));
        Assert.Equal(
            "completed",
            await fixture.Database.ReadInboxStateAsync(fixture.ItemId));
    }

    [Fact]
    public async Task Formal_commit_failure_preserves_both_originals_and_output()
    {
        await using ProcessedFixture fixture = await ProcessedFixture.CreateAsync();

        IngestResult result = await fixture.IngestAsync(failCommit: true);

        Assert.Equal(IngestOutcome.Failed, result.Outcome);
        Assert.True(File.Exists(fixture.InboxMp4));
        Assert.True(File.Exists(fixture.InboxM4a));
        Assert.True(File.Exists(fixture.ProcessedOutputPath));
    }

    private sealed class ProcessedFixture : IAsyncDisposable
    {
        private readonly TestMediaFolders folders;
        private readonly IProcessingStore processingStore;
        private readonly ProcessingExecutionController controller;
        private readonly Sha256FileHasher hasher;

        private ProcessedFixture(
            TestMediaDatabase database,
            TestMediaFolders folders,
            IProcessingStore processingStore,
            ProcessingExecutionController controller,
            Sha256FileHasher hasher,
            Guid itemId,
            string inboxMp4,
            string inboxM4a,
            string processedOutputPath)
        {
            Database = database;
            this.folders = folders;
            this.processingStore = processingStore;
            this.controller = controller;
            this.hasher = hasher;
            ItemId = itemId;
            InboxMp4 = inboxMp4;
            InboxM4a = inboxM4a;
            ProcessedOutputPath = processedOutputPath;
        }

        public TestMediaDatabase Database { get; }
        public TestMediaFolders Folders => folders;
        public Guid ItemId { get; }
        public string InboxMp4 { get; }
        public string InboxM4a { get; }
        public string ProcessedOutputPath { get; }

        public static async Task<ProcessedFixture> CreateAsync()
        {
            TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
            await database.MigrateAsync();
            TestMediaFolders folders = TestMediaFolders.Create();
            try
            {
                await new EfDirectorySettingsStore(database.CreateContextFactory())
                    .SaveAsync(
                        folders.Value,
                        TestContext.Current.CancellationToken);
                string video = Path.Combine(folders.Inbox, "paired.mp4");
                string audio = Path.Combine(folders.Inbox, "paired.m4a");
                File.Copy(Fixture("paired.mp4"), video);
                File.Copy(Fixture("paired.m4a"), audio);
                Guid itemId = await RegisterStablePairAsync(
                    database,
                    folders,
                    video,
                    audio);

                var hasher = new Sha256FileHasher();
                var tool = new FfmpegMediaTool(new BundledMediaToolLocator(
                    new MediaToolOptions("C:\\Program Files\\ffmpeg\\bin")));
                var workspace = new ControlledProcessingWorkspace();
                var processor = new Mp4M4aMergeProcessor(
                    tool,
                    workspace,
                    new ProcessingFileVerifier(hasher),
                    hasher);
                var registry = new MediaProcessorRegistry([processor]);
                var processingStore = new EfProcessingStore(
                    database.CreateContextFactory());
                var controller = new ProcessingExecutionController();
                var activity = new NoOpActivity();
                var command = new ProcessingCommandService(
                    processingStore,
                    new ProcessingPlanner(registry),
                    tool,
                    hasher,
                    new MediaOperationCoordinator(),
                    new NoOpSignal(),
                    controller,
                    activity);
                ProcessingCommandResult queued = await command.StartAsync(
                    itemId,
                    TestContext.Current.CancellationToken);
                Assert.Equal(ProcessingCommandOutcome.Queued, queued.Outcome);
                var executor = new ProcessingJobExecutor(
                    processingStore,
                    registry,
                    controller,
                    new ProcessingProgressBuffer(processingStore),
                    workspace,
                    new EfDirectorySettingsStore(database.CreateContextFactory()),
                    activity);
                Assert.True(await executor.ExecuteNextAsync(
                    TestContext.Current.CancellationToken));
                ValidatedProcessingOutput output =
                    (await processingStore.LoadValidatedOutputAsync(
                        itemId,
                        TestContext.Current.CancellationToken))!;
                return new ProcessedFixture(
                    database,
                    folders,
                    processingStore,
                    controller,
                    hasher,
                    itemId,
                    video,
                    audio,
                    output.Output.FullPath);
            }
            catch
            {
                folders.Dispose();
                await database.DisposeAsync();
                throw;
            }
        }

        public async Task<IngestResult> IngestAsync(bool failCommit)
        {
            var ingestStore = new EfIngestStore(Database.CreateContextFactory());
            IControlledMediaStorage storage = new CommitControlledStorage(
                new ControlledMediaStorage(hasher),
                failCommit);
            var invalidation = new ProcessingInvalidationService(
                processingStore,
                controller);
            var service = new IngestService(
                new NoOpScanner(),
                ingestStore,
                new EfDirectorySettingsStore(Database.CreateContextFactory()),
                new IngestInputResolver(processingStore, hasher, invalidation),
                storage,
                new IngestCleanupService(ingestStore, storage),
                new MediaOperationCoordinator(),
                NullLogger<IngestService>.Instance);
            return await service.IngestAsync(
                ItemId,
                TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            folders.Dispose();
            await Database.DisposeAsync();
        }
    }

    private static async Task<Guid> RegisterStablePairAsync(
        TestMediaDatabase database,
        TestMediaFolders folders,
        string videoPath,
        string audioPath)
    {
        DateTimeOffset observedAt = DateTimeOffset.Parse("2026-08-23T10:00:00Z");
        ObservedFile video = Observed(videoPath, observedAt);
        ObservedFile audio = Observed(audioPath, observedAt);
        var reconciliation = new ScanReconciliation(
            folders.Inbox,
            observedAt,
            [
                new CandidateGroup(
                    "mp4_m4a",
                    "PAIRED",
                    MediaKind.Video,
                    CandidateDisposition.RequiresProcessing,
                    [video, audio],
                    "mp4_m4a_merge")
            ],
            new HashSet<string>(
                [videoPath.ToUpperInvariant(), audioPath.ToUpperInvariant()],
                StringComparer.OrdinalIgnoreCase));
        var scan = new EfInboxScanStore(database.CreateContextFactory());
        await scan.ReconcileAsync(
            reconciliation,
            TestContext.Current.CancellationToken);
        await scan.ReconcileAsync(
            reconciliation with { ObservedAtUtc = observedAt.AddSeconds(2) },
            TestContext.Current.CancellationToken);
        return (await database.ReadSingleInboxAsync()).Id;
    }

    private static ObservedFile Observed(string path, DateTimeOffset firstObserved)
    {
        var info = new FileInfo(path);
        return new ObservedFile(
            info.FullName,
            info.Name,
            info.Extension,
            new FileFingerprint(info.Length, info.LastWriteTimeUtc),
            firstObserved);
    }

    private static string Fixture(string filename)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null
            && !File.Exists(Path.Combine(current.FullName, ".git"))
            && !Directory.Exists(Path.Combine(current.FullName, ".git")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return Path.Combine(
            current.FullName,
            "apps",
            "media-api",
            "tests",
            "fixtures",
            "media",
            filename);
    }

    private sealed class NoOpScanner : IInboxScanner
    {
        public Task<ScanResult> ScanAsync(ScanTrigger trigger, CancellationToken cancellationToken) =>
            Task.FromResult(new ScanResult(
                ScanOutcome.Completed, 2, 0, 0, DateTimeOffset.UtcNow, "ok"));
    }

    private sealed class NoOpSignal : IProcessingQueueSignal
    {
        public void Signal()
        {
        }

        public Task WaitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoOpActivity : IInboxActivityNotifier
    {
        public event EventHandler<InboxActivityEventArgs>? Changed;

        public void Publish(InboxActivityKind kind) =>
            Changed?.Invoke(this, new InboxActivityEventArgs(kind));

        public Task WaitForCandidateAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class CommitControlledStorage(
        ControlledMediaStorage inner,
        bool failCommit) : IControlledMediaStorage
    {
        public Task<bool> TargetExistsAsync(LibraryFolders folders, string targetFilename, CancellationToken cancellationToken) => inner.TargetExistsAsync(folders, targetFilename, cancellationToken);
        public Task<StagedFile> StageAsync(Guid operationId, LibraryFolders folders, string sourcePath, string expectedSha256, CancellationToken cancellationToken) => inner.StageAsync(operationId, folders, sourcePath, expectedSha256, cancellationToken);
        public Task<StagedFile> StageAsync(Guid operationId, LibraryFolders folders, PreparedIngestInput input, CancellationToken cancellationToken) => inner.StageAsync(operationId, folders, input, cancellationToken);
        public Task<CommittedFile> CommitAsync(Guid operationId, LibraryFolders folders, StagedFile staged, string targetFilename, CancellationToken cancellationToken) => failCommit ? Task.FromException<CommittedFile>(new IOException("forced commit failure")) : inner.CommitAsync(operationId, folders, staged, targetFilename, cancellationToken);
        public Task<SourceDeleteResult> DeleteSourceIfMatchesAsync(LibraryFolders folders, string sourcePath, long expectedSize, string expectedSha256, CancellationToken cancellationToken) => inner.DeleteSourceIfMatchesAsync(folders, sourcePath, expectedSize, expectedSha256, cancellationToken);
        public Task<SourceDeleteResult> DeleteCleanupFileIfMatchesAsync(LibraryFolders folders, IngestCleanupFile cleanupFile, CancellationToken cancellationToken) => inner.DeleteCleanupFileIfMatchesAsync(folders, cleanupFile, cancellationToken);
        public Task<bool> VerifyCommittedAsync(LibraryFolders folders, CommittedFile committed, CancellationToken cancellationToken) => inner.VerifyCommittedAsync(folders, committed, cancellationToken);
        public Task CleanupTemporaryFilesAsync(Guid operationId, LibraryFolders folders, CancellationToken cancellationToken) => inner.CleanupTemporaryFilesAsync(operationId, folders, cancellationToken);
    }
}
