using TraceFold.Media.Application.Processing;
using TraceFold.Media.Infrastructure.Filesystem;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Filesystem;

public sealed class ProcessingFileVerifierTests
{
    [Fact]
    public async Task Matching_direct_inbox_file_with_spaces_and_chinese_is_accepted()
    {
        using TestMediaFolders folders = TestMediaFolders.Create();
        string source = folders.WriteInbox("原始 视频.mp4", [1, 2, 3, 4]);
        var hasher = new Sha256FileHasher();
        ProcessingPlanInput expected = await SnapshotAsync(source, hasher);
        var verifier = new ProcessingFileVerifier(hasher);

        bool matches = await verifier.MatchesAsync(
            folders.Value,
            expected,
            TestContext.Current.CancellationToken);

        Assert.True(matches);
    }

    [Fact]
    public async Task Missing_or_changed_identity_returns_false()
    {
        using TestMediaFolders folders = TestMediaFolders.Create();
        string source = folders.WriteInbox("paired.mp4", [1, 2, 3]);
        var hasher = new Sha256FileHasher();
        ProcessingPlanInput expected = await SnapshotAsync(source, hasher);
        var verifier = new ProcessingFileVerifier(hasher);
        File.Delete(source);
        Assert.False(await verifier.MatchesAsync(
            folders.Value,
            expected,
            TestContext.Current.CancellationToken));
        await File.WriteAllBytesAsync(
            source,
            [1, 2, 3, 4],
            TestContext.Current.CancellationToken);
        Assert.False(await verifier.MatchesAsync(
            folders.Value,
            expected,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Same_size_hash_change_returns_false_even_with_restored_timestamp()
    {
        using TestMediaFolders folders = TestMediaFolders.Create();
        string source = folders.WriteInbox("paired.mp4", [1, 2, 3]);
        var hasher = new Sha256FileHasher();
        ProcessingPlanInput expected = await SnapshotAsync(source, hasher);
        await File.WriteAllBytesAsync(
            source,
            [9, 9, 9],
            TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(source, expected.LastWriteTimeUtc.UtcDateTime);

        bool matches = await new ProcessingFileVerifier(hasher).MatchesAsync(
            folders.Value,
            expected,
            TestContext.Current.CancellationToken);

        Assert.False(matches);
    }

    [Fact]
    public async Task Nested_or_outside_source_is_rejected()
    {
        using TestMediaFolders folders = TestMediaFolders.Create();
        string nestedDirectory = Directory.CreateDirectory(
            Path.Combine(folders.Inbox, "nested")).FullName;
        string nested = Path.Combine(nestedDirectory, "paired.mp4");
        await File.WriteAllBytesAsync(
            nested,
            [1],
            TestContext.Current.CancellationToken);
        var hasher = new Sha256FileHasher();
        ProcessingPlanInput expected = await SnapshotAsync(nested, hasher);

        await Assert.ThrowsAsync<ControlledPathException>(() =>
            new ProcessingFileVerifier(hasher).MatchesAsync(
                folders.Value,
                expected,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Reparse_source_is_rejected()
    {
        using TestMediaFolders folders = TestMediaFolders.Create();
        string outside = Path.Combine(folders.Root, "outside.mp4");
        await File.WriteAllBytesAsync(
            outside,
            [1, 2, 3],
            TestContext.Current.CancellationToken);
        string linked = Path.Combine(folders.Inbox, "linked.mp4");
        File.CreateSymbolicLink(linked, outside);
        var hasher = new Sha256FileHasher();
        ProcessingPlanInput expected = await SnapshotAsync(outside, hasher) with
        {
            FullPath = linked,
            Filename = "linked.mp4"
        };

        await Assert.ThrowsAsync<ControlledPathException>(() =>
            new ProcessingFileVerifier(hasher).MatchesAsync(
                folders.Value,
                expected,
                TestContext.Current.CancellationToken));
    }

    private static async Task<ProcessingPlanInput> SnapshotAsync(
        string path,
        Sha256FileHasher hasher)
    {
        var info = new FileInfo(path);
        return new ProcessingPlanInput(
            Guid.NewGuid(),
            "video",
            path,
            info.Name,
            info.Length,
            info.LastWriteTimeUtc,
            await hasher.ComputeSha256Async(
                path,
                TestContext.Current.CancellationToken));
    }
}
