using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Infrastructure.Filesystem;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class PreviewPathStoreTests
{
    [Fact]
    public async Task Missing_registered_inbox_file_returns_exists_false()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        using var folders = TestMediaFolders.Create();
        await new EfDirectorySettingsStore(database.CreateContextFactory()).SaveAsync(
            folders.Value,
            TestContext.Current.CancellationToken);
        string source = folders.WriteInbox("photo.jpg", [1, 2, 3]);
        Guid itemId = await RegisterInboxAsync(database, source);
        File.Delete(source);
        var resolver = new MediaPreviewResolver(
            new EfPreviewPathStore(database.CreateContextFactory()));

        PreviewDescriptor descriptor = await resolver.ResolveAsync(
            new MetadataOwner(MetadataOwnerKind.InboxItem, itemId),
            TestContext.Current.CancellationToken);

        Assert.Equal(MediaKind.Image, descriptor.MediaKind);
        Assert.True(StringComparer.OrdinalIgnoreCase.Equals(
            Path.GetFullPath(source),
            descriptor.TrustedFullPath));
        Assert.False(descriptor.Exists);
    }

    [Fact]
    public async Task Formal_asset_preview_resolves_from_id_and_configured_library_root()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        using var folders = TestMediaFolders.Create();
        await new EfDirectorySettingsStore(database.CreateContextFactory()).SaveAsync(
            folders.Value,
            TestContext.Current.CancellationToken);
        Guid assetId = Guid.NewGuid();
        string path = Path.Combine(folders.Library, "photo.jpg");
        await File.WriteAllBytesAsync(
            path,
            [1, 2, 3],
            TestContext.Current.CancellationToken);
        await InsertAssetAsync(database, assetId, "photo.jpg");
        var resolver = new MediaPreviewResolver(
            new EfPreviewPathStore(database.CreateContextFactory()));

        PreviewDescriptor descriptor = await resolver.ResolveAsync(
            new MetadataOwner(MetadataOwnerKind.MediaAsset, assetId),
            TestContext.Current.CancellationToken);

        Assert.Equal(MediaKind.Image, descriptor.MediaKind);
        Assert.Equal(Path.GetFullPath(path), descriptor.TrustedFullPath);
        Assert.True(descriptor.Exists);
    }

    [Fact]
    public async Task Database_relative_path_escape_is_rejected()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        using var folders = TestMediaFolders.Create();
        await new EfDirectorySettingsStore(database.CreateContextFactory()).SaveAsync(
            folders.Value,
            TestContext.Current.CancellationToken);
        Guid assetId = Guid.NewGuid();
        await InsertAssetAsync(database, assetId, "../outside.jpg");
        var store = new EfPreviewPathStore(database.CreateContextFactory());

        await Assert.ThrowsAsync<ControlledPathException>(() => store.FindAsync(
            new MetadataOwner(MetadataOwnerKind.MediaAsset, assetId),
            TestContext.Current.CancellationToken));
    }

    private static async Task<Guid> RegisterInboxAsync(
        TestMediaDatabase database,
        string sourcePath)
    {
        DateTimeOffset instant = DateTimeOffset.Parse("2026-08-22T10:00:00Z");
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
                    MediaKind.Image,
                    CandidateDisposition.Direct,
                    [source],
                    null)
            ],
            new HashSet<string>(
                [Path.GetFullPath(sourcePath).ToUpperInvariant()],
                StringComparer.OrdinalIgnoreCase));
        var store = new EfInboxScanStore(database.CreateContextFactory());
        await store.ReconcileAsync(
            reconciliation,
            TestContext.Current.CancellationToken);
        return (await database.ReadSingleInboxAsync()).Id;
    }

    private static Task InsertAssetAsync(
        TestMediaDatabase database,
        Guid assetId,
        string relativePath) =>
        database.ExecuteSqlAsync($"""
            INSERT INTO media_assets
                (id, media_kind, actual_filename, relative_path,
                 normalized_relative_path, size, sha256, metadata_version,
                 created_at_utc, updated_at_utc)
            VALUES
                ('{assetId.ToString().ToUpperInvariant()}', 'image', 'photo.jpg', '{relativePath}',
                 '{relativePath.ToUpperInvariant()}', 3, '{new string('A', 64)}', 0,
                 '2026-08-22T10:00:00+00:00', '2026-08-22T10:00:00+00:00')
            """);
}
