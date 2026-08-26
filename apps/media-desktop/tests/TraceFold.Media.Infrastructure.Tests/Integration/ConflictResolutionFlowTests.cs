using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TraceFold.Media.Application;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Infrastructure;
using TraceFold.Media.Infrastructure.Filesystem;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Integration;

public sealed class ConflictResolutionFlowTests
{
    private static readonly byte[] OldBytes = [1, 3, 5, 7, 9];
    private static readonly byte[] NewBytes = [2, 4, 6, 8, 10, 12];
    private static readonly byte[] SharedBytes = [11, 22, 33, 44, 55];
    private static readonly byte[] OccupierBytes = [99, 88, 77];
    private static readonly TestMetadata OldMetadata = new(
        "旧显示名称", ["旧作者甲", "旧作者乙"], "旧系列", ["旧标签"]);
    private static readonly TestMetadata NewMetadata = new(
        "新显示名称", ["新作者甲", "新作者乙"], "新系列", ["新标签甲", "新标签乙"]);

    [Fact]
    public async Task Registered_replace_preserves_id_filename_and_updates_all_metadata()
    {
        await using ConflictFixture fixture = await ConflictFixture.CreateAsync();
        Guid oldId = await fixture.SeedRegisteredAssetAsync(
            "photo.jpg", OldBytes, OldMetadata);
        (Guid itemId, string source) = await fixture.ScanReadySourceAsync(
            "photo.jpg", NewBytes, NewMetadata);
        ConflictCaseRecord conflict = await fixture.RequestConflictAsync(itemId);
        Guid matchId = Assert.Single(
            conflict.Matches,
            match => match.AssetId == oldId).Id;

        IngestResult result = await fixture.Resolution.ResolveAsync(
            conflict.Id,
            ConflictResolution.Replace,
            matchId,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.Completed, result.Outcome);
        Assert.Equal(oldId, result.AssetId);
        Assert.Equal(NewBytes, await File.ReadAllBytesAsync(
            fixture.LibraryPath("photo.jpg"),
            TestContext.Current.CancellationToken));
        Assert.False(File.Exists(source));
        await fixture.AssertAssetAsync(oldId, "photo.jpg", NewBytes, NewMetadata);
        fixture.AssertNoInternalRecoveryFiles();
    }

    [Fact]
    public async Task Keep_both_allows_duplicate_hash_and_uses_next_available_suffix()
    {
        await using ConflictFixture fixture = await ConflictFixture.CreateAsync();
        await fixture.SeedRegisteredAssetAsync(
            "photo.jpg", SharedBytes, OldMetadata);
        fixture.Folders.WriteLibrary("photo (2).jpg", OccupierBytes);
        (Guid itemId, string source) = await fixture.ScanReadySourceAsync(
            "photo.jpg", SharedBytes, NewMetadata);
        ConflictCaseRecord conflict = await fixture.RequestConflictAsync(itemId);

        IngestResult result = await fixture.Resolution.ResolveAsync(
            conflict.Id,
            ConflictResolution.KeepBoth,
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.Completed, result.Outcome);
        Assert.Equal(SharedBytes, await File.ReadAllBytesAsync(
            fixture.LibraryPath("photo (3).jpg"),
            TestContext.Current.CancellationToken));
        Assert.False(File.Exists(source));
        Assert.Equal(2, await fixture.CountAssetsByHashAsync(Hash(SharedBytes)));
        LibraryAssetDetailsDto created = (await fixture.Library.GetDetailsAsync(
            result.AssetId!.Value,
            TestContext.Current.CancellationToken))!;
        Assert.Equal("photo (3).jpg", created.Card.ActualFilename);
        Assert.Equal(NewMetadata.DisplayName, created.Card.DisplayName);
        fixture.AssertNoInternalRecoveryFiles();
    }

    [Fact]
    public async Task Unmanaged_replace_creates_one_asset_and_preserves_selected_filename()
    {
        await using ConflictFixture fixture = await ConflictFixture.CreateAsync();
        fixture.Folders.WriteLibrary("photo.jpg", OldBytes);
        (Guid itemId, string source) = await fixture.ScanReadySourceAsync(
            "photo.jpg", NewBytes, NewMetadata);
        ConflictCaseRecord conflict = await fixture.RequestConflictAsync(itemId);
        ConflictMatchRecord match = Assert.Single(conflict.Matches);
        Assert.False(match.IsRegistered);

        IngestResult result = await fixture.Resolution.ResolveAsync(
            conflict.Id,
            ConflictResolution.Replace,
            match.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.Completed, result.Outcome);
        Assert.NotNull(result.AssetId);
        Assert.False(File.Exists(source));
        Assert.Equal(NewBytes, await File.ReadAllBytesAsync(
            fixture.LibraryPath("photo.jpg"),
            TestContext.Current.CancellationToken));
        Assert.Single(await fixture.Library.SearchAsync(
            LibraryQuery.Default,
            TestContext.Current.CancellationToken));
        await fixture.AssertAssetAsync(
            result.AssetId.Value,
            "photo.jpg",
            NewBytes,
            NewMetadata);
        fixture.AssertNoInternalRecoveryFiles();
    }

    [Fact]
    public async Task Source_change_invalidates_case_without_touching_formal_file()
    {
        await using ConflictFixture fixture = await ConflictFixture.CreateAsync();
        Guid oldId = await fixture.SeedRegisteredAssetAsync(
            "photo.jpg", OldBytes, OldMetadata);
        (Guid itemId, string source) = await fixture.ScanReadySourceAsync(
            "photo.jpg", NewBytes, NewMetadata);
        ConflictCaseRecord conflict = await fixture.RequestConflictAsync(itemId);
        await File.WriteAllBytesAsync(
            source,
            [.. NewBytes, 42],
            TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(1));

        IngestResult result = await fixture.Resolution.ResolveAsync(
            conflict.Id,
            ConflictResolution.Replace,
            Assert.Single(conflict.Matches).Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.ConflictChanged, result.Outcome);
        Assert.True(File.Exists(source));
        Assert.Equal(OldBytes, await File.ReadAllBytesAsync(
            fixture.LibraryPath("photo.jpg"),
            TestContext.Current.CancellationToken));
        Assert.Equal(
            ConflictCaseStatus.Invalidated,
            (await fixture.ConflictStore.LoadAsync(
                conflict.Id,
                TestContext.Current.CancellationToken))!.Status);
        await fixture.AssertAssetAsync(oldId, "photo.jpg", OldBytes, OldMetadata);
    }

    [Fact]
    public async Task Target_change_invalidates_and_redetects_without_overwriting_it()
    {
        await using ConflictFixture fixture = await ConflictFixture.CreateAsync();
        await fixture.SeedRegisteredAssetAsync("photo.jpg", OldBytes, OldMetadata);
        (Guid itemId, string source) = await fixture.ScanReadySourceAsync(
            "photo.jpg", NewBytes, NewMetadata);
        ConflictCaseRecord conflict = await fixture.RequestConflictAsync(itemId);
        byte[] changedTarget = [7, 7, 7, 7, 7, 7, 7];
        await File.WriteAllBytesAsync(
            fixture.LibraryPath("photo.jpg"),
            changedTarget,
            TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(
            fixture.LibraryPath("photo.jpg"),
            DateTime.UtcNow.AddMinutes(2));

        IngestResult result = await fixture.Resolution.ResolveAsync(
            conflict.Id,
            ConflictResolution.Replace,
            Assert.Single(conflict.Matches).Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.ConflictBlocked, result.Outcome);
        Assert.NotEqual(conflict.Id, result.ConflictCaseId);
        Assert.True(File.Exists(source));
        Assert.Equal(changedTarget, await File.ReadAllBytesAsync(
            fixture.LibraryPath("photo.jpg"),
            TestContext.Current.CancellationToken));
        Assert.Equal(
            ConflictCaseStatus.Invalidated,
            (await fixture.ConflictStore.LoadAsync(
                conflict.Id,
                TestContext.Current.CancellationToken))!.Status);
    }

    [Fact]
    public async Task Combined_name_and_content_conflict_is_one_default_target()
    {
        await using ConflictFixture fixture = await ConflictFixture.CreateAsync();
        Guid assetId = await fixture.SeedRegisteredAssetAsync(
            "photo.jpg", SharedBytes, OldMetadata);
        (Guid itemId, _) = await fixture.ScanReadySourceAsync(
            "photo.jpg", SharedBytes, NewMetadata);

        ConflictCaseRecord conflict = await fixture.RequestConflictAsync(itemId);

        ConflictMatchRecord match = Assert.Single(conflict.Matches);
        Assert.Equal(assetId, match.AssetId);
        Assert.True(match.Reason.HasFlag(ConflictReason.Name));
        Assert.True(match.Reason.HasFlag(ConflictReason.Content));
        ConflictCaseDto dto = (await fixture.ConflictQuery.GetOpenAsync(
            itemId,
            TestContext.Current.CancellationToken))!;
        Assert.Equal(match.Id, dto.DefaultReplaceMatchId);
    }

    [Fact]
    public async Task Multiple_content_targets_replace_only_selected_asset_metadata()
    {
        await using ConflictFixture fixture = await ConflictFixture.CreateAsync();
        Guid firstId = await fixture.SeedRegisteredAssetAsync(
            "first.jpg", SharedBytes, OldMetadata);
        var secondMetadata = new TestMetadata(
            "第二项", ["第二作者"], "第二系列", ["第二标签"]);
        (Guid secondItemId, _) = await fixture.ScanReadySourceAsync(
            "second.jpg", SharedBytes, secondMetadata);
        ConflictCaseRecord secondConflict = await fixture.RequestConflictAsync(
            secondItemId);
        IngestResult secondResult = await fixture.Resolution.ResolveAsync(
            secondConflict.Id,
            ConflictResolution.KeepBoth,
            null,
            TestContext.Current.CancellationToken);
        Guid secondId = secondResult.AssetId!.Value;
        (Guid itemId, string source) = await fixture.ScanReadySourceAsync(
            "incoming.jpg", SharedBytes, NewMetadata);
        ConflictCaseRecord conflict = await fixture.RequestConflictAsync(itemId);
        Assert.Equal(2, conflict.Matches.Count(match => match.IsRegistered));
        Guid selectedMatchId = conflict.Matches.Single(
            match => match.AssetId == firstId).Id;

        IngestResult result = await fixture.Resolution.ResolveAsync(
            conflict.Id,
            ConflictResolution.Replace,
            selectedMatchId,
            TestContext.Current.CancellationToken);

        Assert.Equal(firstId, result.AssetId);
        Assert.False(File.Exists(source));
        Assert.False(File.Exists(fixture.LibraryPath("incoming.jpg")));
        await fixture.AssertAssetAsync(
            firstId, "first.jpg", SharedBytes, NewMetadata);
        await fixture.AssertAssetAsync(
            secondId, "second.jpg", SharedBytes, secondMetadata);
    }

    [Fact]
    public async Task Direct_video_uses_the_same_registered_replace_pipeline()
    {
        await using ConflictFixture fixture = await ConflictFixture.CreateAsync();
        Guid oldId = await fixture.SeedRegisteredAssetAsync(
            "clip.mp4", OldBytes, OldMetadata);
        (Guid itemId, string source) = await fixture.ScanReadySourceAsync(
            "clip.mp4", NewBytes, NewMetadata);
        ConflictCaseRecord conflict = await fixture.RequestConflictAsync(itemId);

        IngestResult result = await fixture.Resolution.ResolveAsync(
            conflict.Id,
            ConflictResolution.Replace,
            Assert.Single(conflict.Matches).Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.Completed, result.Outcome);
        Assert.Equal(oldId, result.AssetId);
        Assert.False(File.Exists(source));
        await fixture.AssertAssetAsync(oldId, "clip.mp4", NewBytes, NewMetadata);
    }

    [Fact]
    public async Task Open_conflict_survives_rebuilding_the_service_provider()
    {
        await using ConflictFixture fixture = await ConflictFixture.CreateAsync();
        await fixture.SeedRegisteredAssetAsync("photo.jpg", OldBytes, OldMetadata);
        (Guid itemId, _) = await fixture.ScanReadySourceAsync(
            "photo.jpg", NewBytes, NewMetadata);
        ConflictCaseRecord conflict = await fixture.RequestConflictAsync(itemId);

        await fixture.RebuildAsync();

        ConflictCaseDto reopened = (await fixture.ConflictQuery.GetOpenAsync(
            itemId,
            TestContext.Current.CancellationToken))!;
        Assert.Equal(conflict.Id, reopened.Id);
        Assert.Equal(conflict.Matches.Select(match => match.Id),
            reopened.Matches.Select(match => match.Id));
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    private sealed record TestMetadata(
        string DisplayName,
        IReadOnlyList<string> Authors,
        string Series,
        IReadOnlyList<string> Tags);

    private sealed class ConflictFixture : IAsyncDisposable
    {
        private readonly string dataRoot;
        private readonly AdjustableTimeProvider clock;
        private ServiceProvider provider;

        private ConflictFixture(
            TestMediaFolders folders,
            string dataRoot,
            AdjustableTimeProvider clock,
            ServiceProvider provider)
        {
            Folders = folders;
            this.dataRoot = dataRoot;
            this.clock = clock;
            this.provider = provider;
            ResolveServices();
        }

        public TestMediaFolders Folders { get; }

        public IConflictResolutionService Resolution { get; private set; } = null!;

        public IConflictCaseStore ConflictStore { get; private set; } = null!;

        public IConflictQueryService ConflictQuery { get; private set; } = null!;

        public ILibraryQueryService Library { get; private set; } = null!;

        private IInboxScanner Scanner { get; set; } = null!;

        private IInboxQueryService Inbox { get; set; } = null!;

        private IIngestService Ingest { get; set; } = null!;

        private IMetadataEditorService Metadata { get; set; } = null!;

        private ITaxonomyService Taxonomy { get; set; } = null!;

        public static async Task<ConflictFixture> CreateAsync()
        {
            TestMediaFolders folders = TestMediaFolders.Create();
            string dataRoot = Path.Combine(folders.Root, "data");
            var clock = new AdjustableTimeProvider(
                DateTimeOffset.Parse("2026-08-24T08:00:00Z"));
            ServiceProvider provider = BuildProvider(dataRoot, clock);
            try
            {
                await provider.GetRequiredService<IDatabaseInitializer>()
                    .InitializeAsync(TestContext.Current.CancellationToken);
                DirectorySettingsResult settings = await provider
                    .GetRequiredService<DirectorySettingsService>()
                    .SaveAsync(
                        folders.Value,
                        TestContext.Current.CancellationToken);
                Assert.True(settings.Succeeded);
                return new ConflictFixture(folders, dataRoot, clock, provider);
            }
            catch
            {
                await provider.DisposeAsync();
                folders.Dispose();
                throw;
            }
        }

        public async Task RebuildAsync()
        {
            await provider.DisposeAsync();
            provider = BuildProvider(dataRoot, clock);
            await provider.GetRequiredService<IDatabaseInitializer>()
                .InitializeAsync(TestContext.Current.CancellationToken);
            ResolveServices();
        }

        public async Task<Guid> SeedRegisteredAssetAsync(
            string filename,
            byte[] bytes,
            TestMetadata metadata)
        {
            (Guid itemId, _) = await ScanReadySourceAsync(
                filename,
                bytes,
                metadata);
            IngestResult result = await Ingest.IngestAsync(
                itemId,
                TestContext.Current.CancellationToken);
            Assert.Equal(IngestOutcome.Completed, result.Outcome);
            return result.AssetId!.Value;
        }

        public async Task<(Guid ItemId, string SourcePath)> ScanReadySourceAsync(
            string filename,
            byte[] bytes,
            TestMetadata metadata)
        {
            string source = Folders.WriteInbox(filename, bytes);
            await ObserveAsync();
            IReadOnlyList<InboxCardDto> cards = await Inbox.SearchAsync(
                new InboxQuery(filename, null),
                TestContext.Current.CancellationToken);
            InboxCardDto card = Assert.Single(cards, candidate =>
                candidate.ActualFilename.Equals(
                    filename,
                    StringComparison.OrdinalIgnoreCase));
            Assert.Equal(InboxItemState.ReadyToTag, card.State);
            await SaveMetadataAsync(card.Id, metadata);
            return (card.Id, source);
        }

        public async Task<ConflictCaseRecord> RequestConflictAsync(Guid itemId)
        {
            IngestResult result = await Ingest.IngestAsync(
                itemId,
                TestContext.Current.CancellationToken);
            Assert.Equal(IngestOutcome.ConflictBlocked, result.Outcome);
            Assert.NotNull(result.ConflictCaseId);
            return (await ConflictStore.LoadAsync(
                result.ConflictCaseId.Value,
                TestContext.Current.CancellationToken))!;
        }

        public string LibraryPath(string filename) =>
            Path.Combine(Folders.Library, filename);

        public async Task<int> CountAssetsByHashAsync(string hash)
        {
            IReadOnlyList<LibraryAssetDto> assets = await Library.SearchAsync(
                LibraryQuery.Default,
                TestContext.Current.CancellationToken);
            int count = 0;
            foreach (LibraryAssetDto asset in assets)
            {
                LibraryAssetDetailsDto details = (await Library.GetDetailsAsync(
                    asset.Id,
                    TestContext.Current.CancellationToken))!;
                if (StringComparer.OrdinalIgnoreCase.Equals(details.Sha256, hash))
                {
                    count++;
                }
            }

            return count;
        }

        public async Task AssertAssetAsync(
            Guid assetId,
            string filename,
            byte[] bytes,
            TestMetadata expected)
        {
            LibraryAssetDetailsDto details = (await Library.GetDetailsAsync(
                assetId,
                TestContext.Current.CancellationToken))!;
            Assert.Equal(filename, details.Card.ActualFilename);
            Assert.Equal(expected.DisplayName, details.Card.DisplayName);
            Assert.Equal(Hash(bytes), details.Sha256);
            Assert.Equal(
                expected.Authors,
                details.Metadata.Authors.Select(author => author.DisplayName));
            Assert.Equal(expected.Series, details.Metadata.Series?.DisplayName);
            Assert.Equal(
                expected.Tags,
                details.Metadata.Tags.Select(tag => tag.DisplayName));
        }

        public void AssertNoInternalRecoveryFiles()
        {
            foreach (string category in new[] { "commit", "recovery" })
            {
                string path = Path.Combine(
                    Folders.Library,
                    ".tracefold",
                    category);
                Assert.True(!Directory.Exists(path)
                    || !Directory.EnumerateFileSystemEntries(path).Any());
            }
        }

        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync();
            Folders.Dispose();
        }

        private static ServiceProvider BuildProvider(
            string dataRoot,
            TimeProvider clock)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IHostApplicationLifetime, TestHostLifetime>();
            services.AddMediaApplication();
            services.AddMediaInfrastructure(dataRoot);
            services.AddSingleton(clock);
            services.AddSingleton<TimeProvider>(clock);
            return services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        }

        private void ResolveServices()
        {
            Scanner = provider.GetRequiredService<IInboxScanner>();
            Inbox = provider.GetRequiredService<IInboxQueryService>();
            Ingest = provider.GetRequiredService<IIngestService>();
            Metadata = provider.GetRequiredService<IMetadataEditorService>();
            Taxonomy = provider.GetRequiredService<ITaxonomyService>();
            Resolution = provider.GetRequiredService<IConflictResolutionService>();
            ConflictStore = provider.GetRequiredService<IConflictCaseStore>();
            ConflictQuery = provider.GetRequiredService<IConflictQueryService>();
            Library = provider.GetRequiredService<ILibraryQueryService>();
            Assert.IsType<Sha256FileHasher>(
                provider.GetRequiredService<TraceFold.Media.Application.Storage.IFileHasher>());
            Assert.IsType<ControlledMediaStorage>(
                provider.GetRequiredService<TraceFold.Media.Application.Storage.IControlledMediaStorage>());
        }

        private async Task ObserveAsync()
        {
            await Scanner.ScanAsync(
                ScanTrigger.Manual,
                TestContext.Current.CancellationToken);
            clock.Advance(TimeSpan.FromSeconds(11));
            await Scanner.ScanAsync(
                ScanTrigger.StabilityCheck,
                TestContext.Current.CancellationToken);
        }

        private async Task SaveMetadataAsync(Guid itemId, TestMetadata metadata)
        {
            var authors = new List<Guid>(metadata.Authors.Count);
            foreach (string name in metadata.Authors)
            {
                authors.Add((await Taxonomy.CreateAsync(
                    TaxonomyKind.Author,
                    name,
                    TestContext.Current.CancellationToken)).Id);
            }

            Guid series = (await Taxonomy.CreateAsync(
                TaxonomyKind.Series,
                metadata.Series,
                TestContext.Current.CancellationToken)).Id;
            var tags = new List<Guid>(metadata.Tags.Count);
            foreach (string name in metadata.Tags)
            {
                tags.Add((await Taxonomy.CreateAsync(
                    TaxonomyKind.Tag,
                    name,
                    TestContext.Current.CancellationToken)).Id);
            }

            MetadataSaveResult saved = await Metadata.SaveAsync(
                new MetadataOwner(MetadataOwnerKind.InboxItem, itemId),
                new MetadataUpdate(
                    metadata.DisplayName,
                    authors,
                    series,
                    tags,
                    0),
                TestContext.Current.CancellationToken);
            Assert.True(saved.Succeeded);
        }
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
