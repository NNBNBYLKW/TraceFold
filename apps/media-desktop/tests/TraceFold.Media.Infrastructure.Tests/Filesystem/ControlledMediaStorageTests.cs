using TraceFold.Media.Application.Storage;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Infrastructure.Filesystem;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Filesystem;

public sealed partial class ControlledMediaStorageTests
{
    [Fact]
    public async Task DeleteSourceIfMatchesAsync_never_deletes_same_size_replacement_created_during_verification()
    {
        using var folders = TestMediaFolders.Create();
        string source = folders.WriteInbox("photo.jpg", [1, 2, 3]);
        var realHasher = new Sha256FileHasher();
        string originalHash = await realHasher.ComputeSha256Async(
            source,
            TestContext.Current.CancellationToken);
        byte[] replacement = [9, 9, 9];
        var storage = new ControlledMediaStorage(
            new ReplacingHasher(source, replacement, originalHash));

        SourceDeleteResult result = await storage.DeleteSourceIfMatchesAsync(
            folders.Value,
            source,
            expectedSize: 3,
            expectedSha256: originalHash,
            TestContext.Current.CancellationToken);

        Assert.Equal(SourceDeleteOutcome.Deleted, result.Outcome);
        Assert.True(File.Exists(source));
        Assert.Equal(
            replacement,
            await File.ReadAllBytesAsync(
                source,
                TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFiles(
            folders.Inbox,
            ".tracefold-delete-*.tmp"));
    }

    [Fact]
    public async Task DeleteSourceIfMatchesAsync_never_deletes_changed_source()
    {
        using var folders = TestMediaFolders.Create();
        string source = folders.WriteInbox("photo.jpg", [1, 2, 3]);
        var hasher = new Sha256FileHasher();
        var storage = new ControlledMediaStorage(hasher);
        string originalHash = await hasher.ComputeSha256Async(
            source,
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            source,
            [9, 9, 9],
            TestContext.Current.CancellationToken);

        SourceDeleteResult result = await storage.DeleteSourceIfMatchesAsync(
            folders.Value,
            source,
            expectedSize: 3,
            expectedSha256: originalHash,
            TestContext.Current.CancellationToken);

        Assert.Equal(SourceDeleteOutcome.SourceChanged, result.Outcome);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task Stage_and_commit_preserve_source_and_exact_filename()
    {
        using var folders = TestMediaFolders.Create();
        string source = folders.WriteInbox("原始 名称.jpg", [1, 2, 3, 4]);
        var hasher = new Sha256FileHasher();
        var storage = new ControlledMediaStorage(hasher);
        string hash = await hasher.ComputeSha256Async(
            source,
            TestContext.Current.CancellationToken);
        Guid operationId = Guid.NewGuid();

        StagedFile staged = await storage.StageAsync(
            operationId,
            folders.Value,
            source,
            hash,
            TestContext.Current.CancellationToken);
        CommittedFile committed = await storage.CommitAsync(
            operationId,
            folders.Value,
            staged,
            "原始 名称.jpg",
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(source));
        Assert.True(File.Exists(staged.FullPath));
        Assert.Equal(
            [1, 2, 3, 4],
            await File.ReadAllBytesAsync(
                committed.FullPath,
                TestContext.Current.CancellationToken));
        Assert.Equal("原始 名称.jpg", Path.GetFileName(committed.FullPath));
        Assert.Equal(hash, staged.Sha256);
        Assert.Equal(hash, committed.Sha256);
        Assert.True(await storage.VerifyCommittedAsync(
            folders.Value,
            committed,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Existing_target_causes_conflict_and_preserves_both_files()
    {
        using var folders = TestMediaFolders.Create();
        string source = folders.WriteInbox("photo.jpg", [1, 2, 3]);
        string existing = Path.Combine(folders.Library, "photo.jpg");
        await File.WriteAllBytesAsync(
            existing,
            [7, 8],
            TestContext.Current.CancellationToken);
        var hasher = new Sha256FileHasher();
        var storage = new ControlledMediaStorage(hasher);
        string hash = await hasher.ComputeSha256Async(
            source,
            TestContext.Current.CancellationToken);
        Guid operationId = Guid.NewGuid();
        StagedFile staged = await storage.StageAsync(
            operationId,
            folders.Value,
            source,
            hash,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<StorageConflictException>(() => storage.CommitAsync(
            operationId,
            folders.Value,
            staged,
            "photo.jpg",
            TestContext.Current.CancellationToken));

        Assert.Equal(
            [1, 2, 3],
            await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken));
        Assert.Equal(
            [7, 8],
            await File.ReadAllBytesAsync(existing, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("../escape.jpg")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("name:stream")]
    public async Task TargetExistsAsync_rejects_unsafe_target_names(string targetFilename)
    {
        using var folders = TestMediaFolders.Create();
        var storage = new ControlledMediaStorage(new Sha256FileHasher());

        await Assert.ThrowsAsync<ControlledPathException>(() =>
            storage.TargetExistsAsync(
                folders.Value,
                targetFilename,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TargetExistsAsync_rejects_a_rooted_target_name()
    {
        using var folders = TestMediaFolders.Create();
        var storage = new ControlledMediaStorage(new Sha256FileHasher());
        string rooted = Path.Combine(folders.Root, "outside.jpg");

        await Assert.ThrowsAsync<ControlledPathException>(() =>
            storage.TargetExistsAsync(
                folders.Value,
                rooted,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StageAsync_rejects_a_reparse_point_source()
    {
        using var folders = TestMediaFolders.Create();
        string outside = Path.Combine(folders.Root, "outside.jpg");
        await File.WriteAllBytesAsync(
            outside,
            [1, 2, 3],
            TestContext.Current.CancellationToken);
        string linked = Path.Combine(folders.Inbox, "linked.jpg");
        File.CreateSymbolicLink(linked, outside);
        var hasher = new Sha256FileHasher();
        var storage = new ControlledMediaStorage(hasher);
        string hash = await hasher.ComputeSha256Async(
            outside,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ControlledPathException>(() => storage.StageAsync(
            Guid.NewGuid(),
            folders.Value,
            linked,
            hash,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cleanup_removes_only_the_selected_operation_temporary_files()
    {
        using var folders = TestMediaFolders.Create();
        Guid selected = Guid.NewGuid();
        Guid sibling = Guid.NewGuid();
        string selectedDirectory = Directory.CreateDirectory(Path.Combine(
            folders.Work,
            ".tracefold",
            "ingest",
            selected.ToString("N"))).FullName;
        string siblingDirectory = Directory.CreateDirectory(Path.Combine(
            folders.Work,
            ".tracefold",
            "ingest",
            sibling.ToString("N"))).FullName;
        await File.WriteAllBytesAsync(
            Path.Combine(selectedDirectory, "payload"),
            [1],
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(siblingDirectory, "payload"),
            [2],
            TestContext.Current.CancellationToken);
        string selectedTemporary = Path.Combine(
            folders.Library,
            $".tracefold-{selected:N}.tmp");
        string siblingTemporary = Path.Combine(
            folders.Library,
            $".tracefold-{sibling:N}.tmp");
        await File.WriteAllBytesAsync(
            selectedTemporary,
            [3],
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            siblingTemporary,
            [4],
            TestContext.Current.CancellationToken);
        string selectedCommitDirectory = Directory.CreateDirectory(Path.Combine(
            folders.Library,
            ".tracefold",
            "commit",
            selected.ToString("N"))).FullName;
        string siblingCommitDirectory = Directory.CreateDirectory(Path.Combine(
            folders.Library,
            ".tracefold",
            "commit",
            sibling.ToString("N"))).FullName;
        await File.WriteAllBytesAsync(
            Path.Combine(selectedCommitDirectory, "payload"),
            [5],
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(siblingCommitDirectory, "payload"),
            [6],
            TestContext.Current.CancellationToken);
        var storage = new ControlledMediaStorage(new Sha256FileHasher());

        await storage.CleanupTemporaryFilesAsync(
            selected,
            folders.Value,
            TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(selectedDirectory));
        Assert.False(File.Exists(selectedTemporary));
        Assert.True(Directory.Exists(siblingDirectory));
        Assert.True(File.Exists(siblingTemporary));
        Assert.False(Directory.Exists(selectedCommitDirectory));
        Assert.True(Directory.Exists(siblingCommitDirectory));
    }

    [Fact]
    public async Task Processed_output_can_stage_then_cleanup_its_empty_job_directory()
    {
        using var folders = TestMediaFolders.Create();
        Guid jobId = Guid.NewGuid();
        string jobDirectory = Directory.CreateDirectory(Path.Combine(
            folders.Work,
            ".tracefold",
            "processing",
            jobId.ToString("N"))).FullName;
        string output = Path.Combine(jobDirectory, "clip.mp4");
        await File.WriteAllBytesAsync(
            output,
            [1, 2, 3, 4],
            TestContext.Current.CancellationToken);
        var hasher = new Sha256FileHasher();
        string hash = await hasher.ComputeSha256Async(
            output,
            TestContext.Current.CancellationToken);
        var input = new PreparedIngestInput(
            IngestCleanupKind.ProcessingOutput,
            output,
            4,
            new FileInfo(output).LastWriteTimeUtc,
            hash,
            [new IngestCleanupFile(
                IngestCleanupKind.ProcessingOutput,
                output,
                4,
                hash)]);
        var storage = new ControlledMediaStorage(hasher);

        StagedFile staged = await storage.StageAsync(
            Guid.NewGuid(),
            folders.Value,
            input,
            TestContext.Current.CancellationToken);
        SourceDeleteResult deleted = await storage.DeleteCleanupFileIfMatchesAsync(
            folders.Value,
            input.CleanupFiles[0],
            TestContext.Current.CancellationToken);

        Assert.Equal(SourceDeleteOutcome.Deleted, deleted.Outcome);
        Assert.True(File.Exists(staged.FullPath));
        Assert.False(File.Exists(output));
        Assert.False(Directory.Exists(jobDirectory));
    }

    [Fact]
    public async Task Processing_output_outside_a_guid_job_directory_is_rejected()
    {
        using var folders = TestMediaFolders.Create();
        string outside = Path.Combine(folders.Work, "outside.mp4");
        await File.WriteAllBytesAsync(
            outside,
            [1, 2, 3],
            TestContext.Current.CancellationToken);
        var input = new PreparedIngestInput(
            IngestCleanupKind.ProcessingOutput,
            outside,
            3,
            new FileInfo(outside).LastWriteTimeUtc,
            new string('A', 64),
            []);
        var storage = new ControlledMediaStorage(new Sha256FileHasher());

        await Assert.ThrowsAsync<ControlledPathException>(() => storage.StageAsync(
            Guid.NewGuid(),
            folders.Value,
            input,
            TestContext.Current.CancellationToken));
    }

    private sealed class ReplacingHasher(
        string sourcePath,
        byte[] replacement,
        string originalHash) : IFileHasher
    {
        public async Task<string> ComputeSha256Async(
            string fullPath,
            CancellationToken cancellationToken)
        {
            await File.WriteAllBytesAsync(
                sourcePath,
                replacement,
                cancellationToken);
            return originalHash;
        }
    }

}
