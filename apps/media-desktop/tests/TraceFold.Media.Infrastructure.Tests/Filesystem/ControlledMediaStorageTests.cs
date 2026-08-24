using TraceFold.Media.Application.Storage;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Infrastructure.Filesystem;
using TraceFold.Media.Infrastructure.Tests.Support;
using TraceFold.Media.Domain.Ingest;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Filesystem;

public sealed partial class ControlledMediaStorageTests
{
    [Fact]
    public async Task InspectOperation_reports_exact_source_staged_target_and_backup_identities()
    {
        using var folders = TestMediaFolders.Create();
        Guid operationId = Guid.NewGuid();
        byte[] newBytes = [1, 2, 3];
        byte[] oldBytes = [7, 8];
        string source = folders.WriteInbox("photo.jpg", newBytes);
        string staged = Path.Combine(
            Directory.CreateDirectory(Path.Combine(
                folders.Work,
                ".tracefold",
                "ingest",
                operationId.ToString("N"))).FullName,
            "payload");
        await File.WriteAllBytesAsync(
            staged,
            newBytes,
            TestContext.Current.CancellationToken);
        string target = folders.WriteLibrary("photo.jpg", newBytes);
        string backup = Path.Combine(
            Directory.CreateDirectory(Path.Combine(
                folders.Library,
                ".tracefold",
                "recovery",
                operationId.ToString("N"))).FullName,
            "original.jpg");
        await File.WriteAllBytesAsync(
            backup,
            oldBytes,
            TestContext.Current.CancellationToken);
        var hasher = new Sha256FileHasher();
        string newHash = await hasher.ComputeSha256Async(
            source,
            TestContext.Current.CancellationToken);
        string oldHash = await hasher.ComputeSha256Async(
            backup,
            TestContext.Current.CancellationToken);
        IngestOperationRecord operation = InspectionOperation(
            operationId,
            source,
            newBytes.Length,
            newHash,
            staged,
            target,
            oldBytes.Length,
            oldHash,
            backup,
            IngestOperationStage.FileCommitted,
            IngestBackupState.Created);
        var storage = new ControlledMediaStorage(hasher);

        IngestStorageEvidence evidence = await storage.InspectOperationAsync(
            folders.Value,
            operation,
            TestContext.Current.CancellationToken);

        Assert.Equal(FileIdentityKind.ExpectedSource, evidence.Source.Kind);
        Assert.Equal(FileIdentityKind.ExpectedStaged, evidence.Staged.Kind);
        Assert.Equal(FileIdentityKind.ExpectedNew, evidence.Target.Kind);
        Assert.Equal(FileIdentityKind.ExpectedBackup, evidence.Backup.Kind);
        Assert.All(
            new[] { evidence.Source, evidence.Staged, evidence.Target, evidence.Backup },
            identity => Assert.True(identity.MatchesExpected));
    }

    [Fact]
    public async Task InspectOperation_reports_expected_old_and_absent_without_creating_internal_directories()
    {
        using var folders = TestMediaFolders.Create();
        Guid operationId = Guid.NewGuid();
        byte[] newBytes = [1, 2, 3];
        byte[] oldBytes = [7, 8];
        string source = folders.WriteInbox("photo.jpg", newBytes);
        string target = folders.WriteLibrary("photo.jpg", oldBytes);
        var hasher = new Sha256FileHasher();
        string newHash = await hasher.ComputeSha256Async(
            source,
            TestContext.Current.CancellationToken);
        string oldHash = await hasher.ComputeSha256Async(
            target,
            TestContext.Current.CancellationToken);
        IngestOperationRecord operation = InspectionOperation(
            operationId,
            source,
            newBytes.Length,
            newHash,
            stagedPath: null,
            target,
            oldBytes.Length,
            oldHash,
            backupPath: null,
            IngestOperationStage.Created,
            IngestBackupState.Pending);
        var storage = new ControlledMediaStorage(hasher);

        IngestStorageEvidence evidence = await storage.InspectOperationAsync(
            folders.Value,
            operation,
            TestContext.Current.CancellationToken);

        Assert.Equal(FileIdentityKind.ExpectedSource, evidence.Source.Kind);
        Assert.Equal(FileIdentityKind.Absent, evidence.Staged.Kind);
        Assert.Equal(FileIdentityKind.ExpectedOld, evidence.Target.Kind);
        Assert.Equal(FileIdentityKind.Absent, evidence.Backup.Kind);
        Assert.False(Directory.Exists(Path.Combine(folders.Work, ".tracefold")));
        Assert.False(Directory.Exists(Path.Combine(folders.Library, ".tracefold")));
    }

    [Fact]
    public async Task InspectOperation_reports_wrong_hash_and_reparse_point_as_unknown_without_mutation()
    {
        using var folders = TestMediaFolders.Create();
        Guid operationId = Guid.NewGuid();
        byte[] expectedBytes = [1, 2, 3];
        string outside = Path.Combine(folders.Root, "outside.jpg");
        await File.WriteAllBytesAsync(
            outside,
            expectedBytes,
            TestContext.Current.CancellationToken);
        string source = Path.Combine(folders.Inbox, "photo.jpg");
        File.CreateSymbolicLink(source, outside);
        string target = folders.WriteLibrary("photo.jpg", [9, 9, 9]);
        var hasher = new Sha256FileHasher();
        string expectedHash = await hasher.ComputeSha256Async(
            outside,
            TestContext.Current.CancellationToken);
        IngestOperationRecord operation = InspectionOperation(
            operationId,
            source,
            expectedBytes.Length,
            expectedHash,
            stagedPath: null,
            target,
            expectedTargetSize: 2,
            expectedTargetHash: new string('B', 64),
            backupPath: null,
            IngestOperationStage.FileCommitted,
            IngestBackupState.Pending);
        var storage = new ControlledMediaStorage(hasher);

        IngestStorageEvidence evidence = await storage.InspectOperationAsync(
            folders.Value,
            operation,
            TestContext.Current.CancellationToken);

        Assert.Equal(FileIdentityKind.Unknown, evidence.Source.Kind);
        Assert.Equal(FileIdentityKind.Unknown, evidence.Target.Kind);
        Assert.True(File.Exists(source));
        Assert.Equal(
            expectedBytes,
            await File.ReadAllBytesAsync(
                outside,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            [9, 9, 9],
            await File.ReadAllBytesAsync(
                target,
                TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(Path.Combine(folders.Work, ".tracefold")));
        Assert.False(Directory.Exists(Path.Combine(folders.Library, ".tracefold")));
    }

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

    private static IngestOperationRecord InspectionOperation(
        Guid operationId,
        string sourcePath,
        long sourceSize,
        string sourceHash,
        string? stagedPath,
        string targetPath,
        long expectedTargetSize,
        string expectedTargetHash,
        string? backupPath,
        IngestOperationStage stage,
        IngestBackupState backupState) =>
        new(
            operationId,
            Guid.NewGuid(),
            stage,
            IngestDisposition.ReplaceUnregistered,
            Guid.NewGuid(),
            sourcePath,
            sourceSize,
            DateTimeOffset.UtcNow,
            sourceHash,
            Path.GetFileName(targetPath),
            stagedPath,
            stage >= IngestOperationStage.DatabaseCommitted ? Guid.NewGuid() : null,
            null,
            null,
            Path.GetFileName(targetPath),
            expectedTargetSize,
            new FileInfo(targetPath).LastWriteTimeUtc,
            expectedTargetHash,
            backupState,
            backupPath is null
                ? null
                : Path.GetRelativePath(
                    Path.GetDirectoryName(targetPath)!,
                    backupPath),
            backupPath is null ? null : expectedTargetSize,
            backupPath is null ? null : expectedTargetHash,
            null,
            null,
            0,
            null);

}
