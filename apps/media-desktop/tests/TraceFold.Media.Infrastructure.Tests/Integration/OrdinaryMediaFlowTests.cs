using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TraceFold.Media.Application;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Infrastructure;
using TraceFold.Media.Infrastructure.Filesystem;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Integration;

public sealed class OrdinaryMediaFlowTests
{
    private static readonly byte[] PixelPng =
    [
        137, 80, 78, 71, 13, 10, 26, 10,
        0, 0, 0, 13, 73, 72, 68, 82,
        0, 0, 0, 1, 0, 0, 0, 1,
        8, 6, 0, 0, 0, 31, 21, 196, 137
    ];

    [Fact]
    public async Task Ordinary_png_flows_from_inbox_to_formal_library()
    {
        await using OrdinaryMediaFixture fixture = await OrdinaryMediaFixture.CreateAsync();
        string source = fixture.Write("photo.png", PixelPng);
        TaxonomyTermDto author = await fixture.Taxonomy.CreateAsync(
            TaxonomyKind.Author,
            "作者甲",
            TestContext.Current.CancellationToken);
        TaxonomyTermDto series = await fixture.Taxonomy.CreateAsync(
            TaxonomyKind.Series,
            "系列甲",
            TestContext.Current.CancellationToken);
        TaxonomyTermDto tag = await fixture.Taxonomy.CreateAsync(
            TaxonomyKind.Tag,
            "标签甲",
            TestContext.Current.CancellationToken);
        await fixture.ObserveAsync();
        InboxCardDto card = await fixture.CardAsync("photo.png");
        MetadataSaveResult metadata = await fixture.Metadata.SaveAsync(
            new MetadataOwner(MetadataOwnerKind.InboxItem, card.Id),
            new MetadataUpdate("封面", [author.Id], series.Id, [tag.Id], 0),
            TestContext.Current.CancellationToken);
        Assert.True(metadata.Succeeded);

        IngestResult result = await fixture.Ingest.IngestAsync(
            card.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.Completed, result.Outcome);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(
            fixture.Folders.LibraryDirectory,
            "photo.png")));
        LibraryAssetDto asset = Assert.Single(await fixture.Library.SearchAsync(
            null,
            TestContext.Current.CancellationToken));
        Assert.Equal("封面", asset.DisplayName);
        Assert.Equal("photo.png", asset.ActualFilename);
    }

    [Fact]
    public async Task Existing_formal_name_blocks_ingest_and_preserves_source()
    {
        await using OrdinaryMediaFixture fixture = await OrdinaryMediaFixture.CreateAsync();
        string source = fixture.Write("same.png", PixelPng);
        File.WriteAllBytes(
            Path.Combine(fixture.Folders.LibraryDirectory, "same.png"),
            [9, 9, 9]);
        await fixture.ObserveAsync();
        InboxCardDto card = await fixture.CardAsync("same.png");

        IngestResult result = await fixture.Ingest.IngestAsync(
            card.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.ConflictBlocked, result.Outcome);
        Assert.Equal("ingest.name_conflict", result.Message);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task Registered_hash_blocks_second_name_and_preserves_source()
    {
        await using OrdinaryMediaFixture fixture = await OrdinaryMediaFixture.CreateAsync();
        fixture.Write("first.png", PixelPng);
        await fixture.ObserveAsync();
        InboxCardDto first = await fixture.CardAsync("first.png");
        Assert.Equal(
            IngestOutcome.Completed,
            (await fixture.Ingest.IngestAsync(
                first.Id,
                TestContext.Current.CancellationToken)).Outcome);

        string duplicate = fixture.Write("duplicate.png", PixelPng);
        await fixture.ObserveAsync();
        InboxCardDto second = await fixture.CardAsync("duplicate.png");
        IngestResult result = await fixture.Ingest.IngestAsync(
            second.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.ConflictBlocked, result.Outcome);
        Assert.Equal("ingest.content_conflict", result.Message);
        Assert.True(File.Exists(duplicate));
    }

    [Fact]
    public async Task Matching_mp4_and_m4a_require_processing_and_are_preserved()
    {
        await using OrdinaryMediaFixture fixture = await OrdinaryMediaFixture.CreateAsync();
        string video = fixture.Write("clip.mp4", [1, 2, 3]);
        string audio = fixture.Write("clip.m4a", [4, 5, 6]);
        await fixture.ObserveAsync();
        InboxCardDto card = await fixture.CardAsync("clip.mp4");

        IngestResult result = await fixture.Ingest.IngestAsync(
            card.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(InboxItemState.RequiresProcessing, card.State);
        Assert.Equal(IngestOutcome.RequiresProcessing, result.Outcome);
        Assert.True(File.Exists(video));
        Assert.True(File.Exists(audio));
    }

    [Fact]
    public async Task Missing_preview_does_not_block_ordinary_ingest()
    {
        await using OrdinaryMediaFixture fixture = await OrdinaryMediaFixture.CreateAsync();
        string source = fixture.Write("preview.png", PixelPng);
        await fixture.ObserveAsync();
        InboxCardDto card = await fixture.CardAsync("preview.png");
        DateTime lastWrite = File.GetLastWriteTimeUtc(source);
        File.Delete(source);
        PreviewDescriptor preview = await fixture.Preview.ResolveAsync(
            new MetadataOwner(MetadataOwnerKind.InboxItem, card.Id),
            TestContext.Current.CancellationToken);
        Assert.False(preview.Exists);
        File.WriteAllBytes(source, PixelPng);
        File.SetLastWriteTimeUtc(source, lastWrite);

        IngestResult result = await fixture.Ingest.IngestAsync(
            card.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.Completed, result.Outcome);
        Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task Failed_item_does_not_block_a_healthy_item()
    {
        await using OrdinaryMediaFixture fixture = await OrdinaryMediaFixture.CreateAsync(
            failStageForFilename: "fail.png");
        string failedSource = fixture.Write("fail.png", PixelPng);
        string healthySource = fixture.Write("healthy.png", [.. PixelPng, 1]);
        await fixture.ObserveAsync();
        InboxCardDto failed = await fixture.CardAsync("fail.png");
        InboxCardDto healthy = await fixture.CardAsync("healthy.png");

        IngestResult failedResult = await fixture.Ingest.IngestAsync(
            failed.Id,
            TestContext.Current.CancellationToken);
        IngestResult healthyResult = await fixture.Ingest.IngestAsync(
            healthy.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(IngestOutcome.Failed, failedResult.Outcome);
        Assert.Equal(IngestOutcome.Completed, healthyResult.Outcome);
        Assert.True(File.Exists(failedSource));
        Assert.False(File.Exists(healthySource));
    }

    private sealed class OrdinaryMediaFixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly ServiceProvider _provider;
        private readonly AdjustableTimeProvider _clock;

        private OrdinaryMediaFixture(
            string root,
            ServiceProvider provider,
            AdjustableTimeProvider clock,
            LibraryFolders folders)
        {
            _root = root;
            _provider = provider;
            _clock = clock;
            Folders = folders;
            Scanner = provider.GetRequiredService<IInboxScanner>();
            Inbox = provider.GetRequiredService<IInboxQueryService>();
            Metadata = provider.GetRequiredService<IMetadataEditorService>();
            Taxonomy = provider.GetRequiredService<ITaxonomyService>();
            Ingest = provider.GetRequiredService<IIngestService>();
            Library = provider.GetRequiredService<ILibraryQueryService>();
            Preview = provider.GetRequiredService<IMediaPreviewResolver>();
        }

        public LibraryFolders Folders { get; }

        public IInboxScanner Scanner { get; }

        public IInboxQueryService Inbox { get; }

        public IMetadataEditorService Metadata { get; }

        public ITaxonomyService Taxonomy { get; }

        public IIngestService Ingest { get; }

        public ILibraryQueryService Library { get; }

        public IMediaPreviewResolver Preview { get; }

        public static async Task<OrdinaryMediaFixture> CreateAsync(
            string? failStageForFilename = null)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                $"tracefold-flow-{Guid.NewGuid():N}");
            string inbox = Directory.CreateDirectory(Path.Combine(root, "inbox")).FullName;
            string library = Directory.CreateDirectory(Path.Combine(root, "library")).FullName;
            string work = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
            string data = Path.Combine(root, "data");
            var folders = new LibraryFolders(inbox, library, work);
            var clock = new AdjustableTimeProvider(
                new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero));
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IHostApplicationLifetime, TestHostLifetime>();
            services.AddMediaApplication();
            services.AddMediaInfrastructure(data);
            services.AddSingleton<TimeProvider>(clock);
            if (failStageForFilename is not null)
            {
                services.AddSingleton<IControlledMediaStorage>(
                    new SelectiveFailingStorage(failStageForFilename));
            }

            ServiceProvider provider = services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });
            try
            {
                await provider.GetRequiredService<IDatabaseInitializer>()
                    .InitializeAsync(TestContext.Current.CancellationToken);
                DirectorySettingsResult saved = await provider
                    .GetRequiredService<DirectorySettingsService>()
                    .SaveAsync(folders, TestContext.Current.CancellationToken);
                Assert.True(saved.Succeeded);
                return new OrdinaryMediaFixture(root, provider, clock, folders);
            }
            catch
            {
                await provider.DisposeAsync();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }

                throw;
            }
        }

        public string Write(string filename, byte[] bytes)
        {
            string path = Path.Combine(Folders.InboxDirectory, filename);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public async Task ObserveAsync()
        {
            await Scanner.ScanAsync(
                ScanTrigger.Manual,
                TestContext.Current.CancellationToken);
            _clock.Advance(TimeSpan.FromSeconds(2));
            await Scanner.ScanAsync(
                ScanTrigger.StabilityCheck,
                TestContext.Current.CancellationToken);
        }

        public async Task<InboxCardDto> CardAsync(string filename)
        {
            IReadOnlyList<InboxCardDto> cards = await Inbox.SearchAsync(
                new InboxQuery(filename, null),
                TestContext.Current.CancellationToken);
            return Assert.Single(cards, card =>
                card.ActualFilename.Equals(
                    filename,
                    StringComparison.OrdinalIgnoreCase));
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private sealed class SelectiveFailingStorage(string filename)
        : IControlledMediaStorage
    {
        private readonly ControlledMediaStorage _inner =
            new(new Sha256FileHasher());

        public Task<bool> TargetExistsAsync(
            LibraryFolders folders,
            string targetFilename,
            CancellationToken cancellationToken) =>
            _inner.TargetExistsAsync(folders, targetFilename, cancellationToken);

        public Task<StagedFile> StageAsync(
            Guid operationId,
            LibraryFolders folders,
            string sourcePath,
            string expectedSha256,
            CancellationToken cancellationToken) =>
            Path.GetFileName(sourcePath).Equals(filename, StringComparison.OrdinalIgnoreCase)
                ? Task.FromException<StagedFile>(new IOException("selected failure"))
                : _inner.StageAsync(
                    operationId,
                    folders,
                    sourcePath,
                    expectedSha256,
                    cancellationToken);

        public Task<CommittedFile> CommitAsync(
            Guid operationId,
            LibraryFolders folders,
            StagedFile staged,
            string targetFilename,
            CancellationToken cancellationToken) =>
            _inner.CommitAsync(
                operationId,
                folders,
                staged,
                targetFilename,
                cancellationToken);

        public Task<SourceDeleteResult> DeleteSourceIfMatchesAsync(
            LibraryFolders folders,
            string sourcePath,
            long expectedSize,
            string expectedSha256,
            CancellationToken cancellationToken) =>
            _inner.DeleteSourceIfMatchesAsync(
                folders,
                sourcePath,
                expectedSize,
                expectedSha256,
                cancellationToken);

        public Task<bool> VerifyCommittedAsync(
            LibraryFolders folders,
            CommittedFile committed,
            CancellationToken cancellationToken) =>
            _inner.VerifyCommittedAsync(folders, committed, cancellationToken);

        public Task CleanupTemporaryFilesAsync(
            Guid operationId,
            LibraryFolders folders,
            CancellationToken cancellationToken) =>
            _inner.CleanupTemporaryFilesAsync(
                operationId,
                folders,
                cancellationToken);
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value) => _utcNow += value;
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
