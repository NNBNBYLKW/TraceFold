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
    public async Task Inbox_preview_prefers_the_active_valid_processed_output()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        using var folders = TestMediaFolders.Create();
        await new EfDirectorySettingsStore(database.CreateContextFactory()).SaveAsync(
            folders.Value,
            TestContext.Current.CancellationToken);
        string source = folders.WriteInbox("clip.mp4", [1, 2, 3]);
        Guid itemId = await RegisterInboxAsync(database, source);
        string output = await InsertSuccessfulProcessedOutputAsync(
            database,
            folders,
            itemId);
        var store = new EfPreviewPathStore(database.CreateContextFactory());

        PreviewPathRecord preview = (await store.FindAsync(
            new MetadataOwner(MetadataOwnerKind.InboxItem, itemId),
            TestContext.Current.CancellationToken))!;

        Assert.Equal(Path.GetFullPath(output), preview.TrustedFullPath);
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

    private static async Task<string> InsertSuccessfulProcessedOutputAsync(
        TestMediaDatabase database,
        TestMediaFolders folders,
        Guid itemId)
    {
        Guid planId = Guid.NewGuid();
        Guid jobId = Guid.NewGuid();
        string relativePath = Path.Combine(
            ".tracefold",
            "processing",
            jobId.ToString("N"),
            "clip.mp4");
        string output = Path.Combine(folders.Work, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllBytesAsync(
            output,
            [4, 5, 6],
            TestContext.Current.CancellationToken);
        string item = itemId.ToString().ToUpperInvariant();
        string plan = planId.ToString().ToUpperInvariant();
        string job = jobId.ToString().ToUpperInvariant();
        const string now = "2026-08-23T10:00:00+00:00";
        await database.ExecuteSqlAsync(
            $"""
            INSERT INTO processing_plans
                (id, inbox_item_id, processor_key, processor_version,
                 processor_display_name, capability, output_filename,
                 work_directory_relative_path, step_keys_json, created_at_utc)
            VALUES
                ('{plan}', '{item}', 'mp4_m4a_merge', '1', '合并视频和音频',
                 'mp4_m4a_merge', 'clip.mp4', '.tracefold/processing/{jobId:N}',
                 '["merge"]', '{now}')
            """,
            $"""
            INSERT INTO processing_jobs
                (id, plan_id, inbox_item_id, active_inbox_item_id, attempt, status,
                 current_step_key, progress_percent, error_code, is_retryable,
                 correlation_id, queued_at_utc, started_at_utc, finished_at_utc,
                 updated_at_utc)
            VALUES
                ('{job}', '{plan}', '{item}', NULL, 1, 'succeeded',
                 'verify_output', 100, NULL, 0, 'preview-test',
                 '{now}', '{now}', '{now}', '{now}')
            """,
            $"""
            INSERT INTO processed_outputs
                (job_id, inbox_item_id, active_inbox_item_id, relative_path,
                 size, last_write_time_utc, sha256, duration_ticks,
                 has_video, has_audio, is_valid)
            VALUES
                ('{job}', '{item}', '{item}', '{relativePath}',
                 3, '{now}', '{new string('A', 64)}', 10000000, 1, 1, 1)
            """);
        return output;
    }
}
