using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Startup;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Infrastructure;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Integration;

public sealed class IngestCrashRecoveryFlowTests
{
    private static readonly byte[] OldBytes = [1, 3, 5, 7, 9];
    private static readonly byte[] NewBytes = [2, 4, 6, 8, 10, 12];

    public static TheoryData<IngestFaultPoint> FaultPoints => new()
    {
        IngestFaultPoint.AfterOperationCreated,
        IngestFaultPoint.AfterStaged,
        IngestFaultPoint.AfterBackupCreated,
        IngestFaultPoint.AfterFileCommitted,
        IngestFaultPoint.BeforeDatabaseCommit,
        IngestFaultPoint.AfterDatabaseCommit,
        IngestFaultPoint.BeforeCleanup,
        IngestFaultPoint.AfterCleanupEntry,
        IngestFaultPoint.AfterCleanup
    };

    [Theory]
    [MemberData(nameof(FaultPoints))]
    public async Task Restart_recovers_registered_replacement_at_every_persisted_fault_point(
        IngestFaultPoint faultPoint)
    {
        await using var fixture = await CrashFixture.CreateAsync();
        Guid originalAssetId = await fixture.SeedOriginalAsync();
        (Guid inboxItemId, Guid conflictCaseId, Guid matchId) =
            await fixture.PrepareReplacementAsync();

        await fixture.RebuildAsync(new ThrowingFaultInjector(faultPoint));
        IConflictResolutionService resolution = fixture.Provider
            .GetRequiredService<IConflictResolutionService>();
        InjectedCrashException crash = await Assert.ThrowsAsync<InjectedCrashException>(
            () => resolution.ResolveAsync(
                conflictCaseId,
                ConflictResolution.Replace,
                matchId,
                TestContext.Current.CancellationToken));
        Assert.Equal(faultPoint, crash.Point);

        await fixture.RebuildAsync();
        IStartupCoordinator startup = fixture.Provider
            .GetRequiredService<IStartupCoordinator>();
        await startup.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(StartupState.Ready, startup.Current.State);
        ILibraryQueryService library = fixture.Provider
            .GetRequiredService<ILibraryQueryService>();
        LibraryAssetDto asset = Assert.Single(await library.SearchAsync(
            LibraryQuery.Default,
            TestContext.Current.CancellationToken));
        Assert.Equal(originalAssetId, asset.Id);
        Assert.Equal(
            NewBytes,
            await File.ReadAllBytesAsync(
                Path.Combine(fixture.Folders.Library, "photo.jpg"),
                TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(fixture.Folders.Inbox, "photo.jpg")));
        IngestOperationRecord operation = (await fixture.Provider
            .GetRequiredService<IIngestStore>()
            .FindActiveAsync(
                inboxItemId,
                TestContext.Current.CancellationToken))!;
        Assert.Equal(IngestOperationStage.Completed, operation.Stage);
        Assert.Equal(originalAssetId, operation.AssetId);
        Assert.Single(Directory.EnumerateFiles(
            fixture.Folders.Library,
            "*",
            SearchOption.TopDirectoryOnly));
        fixture.AssertNoRecoveryPayloads();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Corrupt_target_or_backup_blocks_recovery_and_preserves_every_observed_file(
        bool corruptTarget)
    {
        await using var fixture = await CrashFixture.CreateAsync();
        await fixture.SeedOriginalAsync();
        (Guid inboxItemId, Guid conflictCaseId, Guid matchId) =
            await fixture.PrepareReplacementAsync();
        await fixture.RebuildAsync(
            new ThrowingFaultInjector(IngestFaultPoint.AfterFileCommitted));
        await Assert.ThrowsAsync<InjectedCrashException>(() => fixture.Provider
            .GetRequiredService<IConflictResolutionService>()
            .ResolveAsync(
                conflictCaseId,
                ConflictResolution.Replace,
                matchId,
                TestContext.Current.CancellationToken));
        IngestOperationRecord interrupted = (await fixture.Provider
            .GetRequiredService<IIngestStore>()
            .FindActiveAsync(
                inboxItemId,
                TestContext.Current.CancellationToken))!;
        string target = Path.Combine(fixture.Folders.Library, "photo.jpg");
        string backup = Path.Combine(
            fixture.Folders.Library,
            interrupted.BackupRelativePath!);
        string corrupted = corruptTarget ? target : backup;
        byte[] corruptBytes = [99, 88, 77, 66];
        await File.WriteAllBytesAsync(
            corrupted,
            corruptBytes,
            TestContext.Current.CancellationToken);

        await fixture.RebuildAsync();
        await fixture.Provider.GetRequiredService<IStartupCoordinator>()
            .InitializeAsync(TestContext.Current.CancellationToken);

        IngestOperationRecord blocked = (await fixture.Provider
            .GetRequiredService<IIngestStore>()
            .FindActiveAsync(
                inboxItemId,
                TestContext.Current.CancellationToken))!;
        Assert.Equal(IngestOperationStage.RecoveryBlocked, blocked.Stage);
        Assert.Equal(
            corruptTarget ? corruptBytes : NewBytes,
            await File.ReadAllBytesAsync(
                target,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            corruptTarget ? OldBytes : corruptBytes,
            await File.ReadAllBytesAsync(
                backup,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            NewBytes,
            await File.ReadAllBytesAsync(
                Path.Combine(fixture.Folders.Inbox, "photo.jpg"),
                TestContext.Current.CancellationToken));
        Assert.Equal(
            NewBytes,
            await File.ReadAllBytesAsync(
                interrupted.StagedPath!,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Corrupt_database_version_blocks_recovery_without_touching_files()
    {
        await using var fixture = await CrashFixture.CreateAsync();
        await fixture.SeedOriginalAsync();
        (Guid inboxItemId, Guid conflictCaseId, Guid matchId) =
            await fixture.PrepareReplacementAsync();
        await fixture.RebuildAsync(
            new ThrowingFaultInjector(IngestFaultPoint.AfterDatabaseCommit));
        await Assert.ThrowsAsync<InjectedCrashException>(() => fixture.Provider
            .GetRequiredService<IConflictResolutionService>()
            .ResolveAsync(
                conflictCaseId,
                ConflictResolution.Replace,
                matchId,
                TestContext.Current.CancellationToken));
        IngestOperationRecord interrupted = (await fixture.Provider
            .GetRequiredService<IIngestStore>()
            .FindActiveAsync(
                inboxItemId,
                TestContext.Current.CancellationToken))!;
        await fixture.CorruptAssetHashAsync(interrupted.AssetId!.Value);

        await fixture.RebuildAsync();
        await fixture.Provider.GetRequiredService<IStartupCoordinator>()
            .InitializeAsync(TestContext.Current.CancellationToken);

        IngestOperationRecord blocked = (await fixture.Provider
            .GetRequiredService<IIngestStore>()
            .FindActiveAsync(
                inboxItemId,
                TestContext.Current.CancellationToken))!;
        Assert.Equal(IngestOperationStage.RecoveryBlocked, blocked.Stage);
        Assert.Equal(
            NewBytes,
            await File.ReadAllBytesAsync(
                Path.Combine(fixture.Folders.Library, "photo.jpg"),
                TestContext.Current.CancellationToken));
        Assert.Equal(
            OldBytes,
            await File.ReadAllBytesAsync(
                Path.Combine(
                    fixture.Folders.Library,
                    interrupted.BackupRelativePath!),
                TestContext.Current.CancellationToken));
        Assert.Equal(
            NewBytes,
            await File.ReadAllBytesAsync(
                Path.Combine(fixture.Folders.Inbox, "photo.jpg"),
                TestContext.Current.CancellationToken));
        Assert.Equal(
            NewBytes,
            await File.ReadAllBytesAsync(
                interrupted.StagedPath!,
                TestContext.Current.CancellationToken));
    }

    private sealed class CrashFixture : IAsyncDisposable
    {
        private readonly string dataRoot;
        private readonly AdjustableTimeProvider clock;
        private ServiceProvider provider;

        private CrashFixture(
            TestMediaFolders folders,
            string dataRoot,
            AdjustableTimeProvider clock,
            ServiceProvider provider)
        {
            Folders = folders;
            this.dataRoot = dataRoot;
            this.clock = clock;
            this.provider = provider;
        }

        public TestMediaFolders Folders { get; }

        public ServiceProvider Provider => provider;

        public static async Task<CrashFixture> CreateAsync()
        {
            TestMediaFolders folders = TestMediaFolders.Create();
            string dataRoot = Path.Combine(folders.Root, "data");
            var clock = new AdjustableTimeProvider(
                DateTimeOffset.Parse("2026-08-24T08:00:00Z"));
            ServiceProvider provider = BuildProvider(dataRoot, clock, null);
            try
            {
                await provider.GetRequiredService<IDatabaseInitializer>()
                    .InitializeAsync(TestContext.Current.CancellationToken);
                DirectorySettingsResult result = await provider
                    .GetRequiredService<DirectorySettingsService>()
                    .SaveAsync(
                        folders.Value,
                        TestContext.Current.CancellationToken);
                Assert.True(result.Succeeded);
                return new CrashFixture(folders, dataRoot, clock, provider);
            }
            catch
            {
                await provider.DisposeAsync();
                folders.Dispose();
                throw;
            }
        }

        public async Task<Guid> SeedOriginalAsync()
        {
            (Guid itemId, _) = await ScanReadyAsync("photo.jpg", OldBytes);
            await SaveDisplayNameAsync(itemId, "旧资料");
            IngestResult result = await provider
                .GetRequiredService<IIngestService>()
                .IngestAsync(itemId, TestContext.Current.CancellationToken);
            Assert.Equal(IngestOutcome.Completed, result.Outcome);
            return result.AssetId!.Value;
        }

        public async Task<(Guid ItemId, Guid ConflictCaseId, Guid MatchId)>
            PrepareReplacementAsync()
        {
            (Guid itemId, _) = await ScanReadyAsync("photo.jpg", NewBytes);
            await SaveDisplayNameAsync(itemId, "新资料");
            IngestResult blocked = await provider
                .GetRequiredService<IIngestService>()
                .IngestAsync(itemId, TestContext.Current.CancellationToken);
            Assert.Equal(IngestOutcome.ConflictBlocked, blocked.Outcome);
            ConflictCaseRecord conflict = (await provider
                .GetRequiredService<IConflictCaseStore>()
                .LoadAsync(
                    blocked.ConflictCaseId!.Value,
                    TestContext.Current.CancellationToken))!;
            ConflictMatchRecord match = Assert.Single(
                conflict.Matches,
                candidate => candidate.AssetId.HasValue);
            return (itemId, conflict.Id, match.Id);
        }

        public async Task RebuildAsync(IIngestFaultInjector? injector = null)
        {
            await provider.DisposeAsync();
            provider = BuildProvider(dataRoot, clock, injector);
        }

        public async Task CorruptAssetHashAsync(Guid assetId)
        {
            IDbContextFactory<MediaDesktopDbContext> factory = provider
                .GetRequiredService<IDbContextFactory<MediaDesktopDbContext>>();
            await using MediaDesktopDbContext context =
                await factory.CreateDbContextAsync(
                    TestContext.Current.CancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE media_assets
                SET sha256 = {new string('F', 64)}
                WHERE id = {assetId}
                """, TestContext.Current.CancellationToken);
        }

        public void AssertNoRecoveryPayloads()
        {
            foreach (string path in new[]
            {
                Path.Combine(Folders.Work, ".tracefold", "ingest"),
                Path.Combine(Folders.Library, ".tracefold", "commit"),
                Path.Combine(Folders.Library, ".tracefold", "recovery")
            })
            {
                Assert.True(
                    !Directory.Exists(path)
                    || !Directory.EnumerateFileSystemEntries(path).Any(),
                    $"Recovery payloads remain in {path}.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync();
            Folders.Dispose();
        }

        private async Task<(Guid ItemId, string SourcePath)> ScanReadyAsync(
            string filename,
            byte[] bytes)
        {
            string source = Folders.WriteInbox(filename, bytes);
            IInboxScanner scanner = provider.GetRequiredService<IInboxScanner>();
            await scanner.ScanAsync(
                ScanTrigger.Manual,
                TestContext.Current.CancellationToken);
            clock.Advance(TimeSpan.FromSeconds(11));
            await scanner.ScanAsync(
                ScanTrigger.StabilityCheck,
                TestContext.Current.CancellationToken);
            InboxCardDto card = Assert.Single(await provider
                .GetRequiredService<IInboxQueryService>()
                .SearchAsync(
                    new InboxQuery(filename, null),
                    TestContext.Current.CancellationToken),
                candidate => candidate.ActualFilename.Equals(
                    filename,
                    StringComparison.OrdinalIgnoreCase)
                    && candidate.State == TraceFold.Media.Domain.Inbox.InboxItemState.ReadyToTag);
            return (card.Id, source);
        }

        private async Task SaveDisplayNameAsync(Guid itemId, string displayName)
        {
            MetadataSaveResult saved = await provider
                .GetRequiredService<IMetadataEditorService>()
                .SaveAsync(
                    new MetadataOwner(MetadataOwnerKind.InboxItem, itemId),
                    new MetadataUpdate(displayName, [], null, [], 0),
                    TestContext.Current.CancellationToken);
            Assert.True(saved.Succeeded);
        }

        private static ServiceProvider BuildProvider(
            string dataRoot,
            TimeProvider clock,
            IIngestFaultInjector? injector)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IHostApplicationLifetime, TestHostLifetime>();
            services.AddMediaApplication();
            services.AddMediaInfrastructure(dataRoot);
            services.AddSingleton(clock);
            services.AddSingleton<TimeProvider>(clock);
            if (injector is not null)
            {
                services.AddSingleton(injector);
                services.AddSingleton<IIngestFaultInjector>(injector);
            }

            return services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        }
    }

    private sealed class ThrowingFaultInjector(IngestFaultPoint selected)
        : IIngestFaultInjector
    {
        public Task ReachAsync(
            IngestFaultPoint point,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (point == selected)
            {
                throw new InjectedCrashException(point);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class InjectedCrashException(IngestFaultPoint point)
        : Exception(point.ToString())
    {
        public IngestFaultPoint Point { get; } = point;
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        private DateTimeOffset now = utcNow;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan value) => now += value;
    }

    private sealed class TestHostLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication()
        {
        }
    }
}
