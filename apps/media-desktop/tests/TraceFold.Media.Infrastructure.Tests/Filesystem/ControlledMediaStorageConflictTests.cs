using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Infrastructure.Filesystem;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Filesystem;

public sealed partial class ControlledMediaStorageTests
{
    [Fact]
    public async Task InspectTarget_returns_verified_identity_without_mutating_file()
    {
        using var folders = TestMediaFolders.Create();
        string target = folders.WriteLibrary("photo.jpg", [1, 2, 3]);
        var storage = new ControlledMediaStorage(new Sha256FileHasher());

        LibraryFileSnapshot? snapshot = await storage.InspectTargetAsync(
            folders.Value,
            "photo.jpg",
            TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal("photo.jpg", snapshot.RelativePath);
        Assert.Equal(3, snapshot.Size);
        Assert.Equal(
            await HashAsync(target),
            snapshot.Sha256);
        Assert.Equal(
            [1, 2, 3],
            await File.ReadAllBytesAsync(
                target,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InspectTarget_rejects_a_broken_reparse_point()
    {
        using var folders = TestMediaFolders.Create();
        string missing = Path.Combine(folders.Root, "missing.jpg");
        File.CreateSymbolicLink(
            Path.Combine(folders.Library, "photo.jpg"),
            missing);
        var storage = new ControlledMediaStorage(new Sha256FileHasher());

        await Assert.ThrowsAsync<ControlledPathException>(() =>
            storage.InspectTargetAsync(
                folders.Value,
                "photo.jpg",
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CommitUnique_advances_suffix_when_existing_files_win()
    {
        using var folders = TestMediaFolders.Create();
        folders.WriteLibrary("photo.jpg", [9]);
        folders.WriteLibrary("photo (2).jpg", [8]);
        var storage = new ControlledMediaStorage(new Sha256FileHasher());
        Guid operationId = Guid.NewGuid();
        StagedFile staged = await StageAsync(
            storage,
            folders,
            operationId,
            "photo.jpg",
            [1, 2, 3]);

        CommittedFile result = await storage.CommitUniqueAsync(
            operationId,
            folders.Value,
            staged,
            "photo.jpg",
            TestContext.Current.CancellationToken);

        Assert.Equal("photo (3).jpg", result.RelativePath);
        Assert.Equal(
            [1, 2, 3],
            await File.ReadAllBytesAsync(
                result.FullPath,
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("archive", "archive (2)")]
    [InlineData("photo (2).jpg", "photo (2) (2).jpg")]
    public async Task CommitUnique_suffixes_extensionless_and_already_suffixed_names(
        string requested,
        string expected)
    {
        using var folders = TestMediaFolders.Create();
        folders.WriteLibrary(requested, [9]);
        var storage = new ControlledMediaStorage(new Sha256FileHasher());
        Guid operationId = Guid.NewGuid();
        StagedFile staged = await StageAsync(
            storage,
            folders,
            operationId,
            requested,
            [1]);

        CommittedFile result = await storage.CommitUniqueAsync(
            operationId,
            folders.Value,
            staged,
            requested,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.RelativePath);
    }

    [Fact]
    public async Task CommitUnique_retries_when_target_appears_during_commit_preparation()
    {
        using var folders = TestMediaFolders.Create();
        Guid operationId = Guid.NewGuid();
        var realStorage = new ControlledMediaStorage(new Sha256FileHasher());
        StagedFile staged = await StageAsync(
            realStorage,
            folders,
            operationId,
            "photo.jpg",
            [1, 2, 3]);
        string winner = Path.Combine(folders.Library, "photo.jpg");
        var storage = new ControlledMediaStorage(
            new TargetCreatingHasher(folders.Library, winner));

        CommittedFile result = await storage.CommitUniqueAsync(
            operationId,
            folders.Value,
            staged,
            "photo.jpg",
            TestContext.Current.CancellationToken);

        Assert.Equal("photo (2).jpg", result.RelativePath);
        Assert.Equal([9], await File.ReadAllBytesAsync(
            winner,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CommitUnique_rejects_a_reparse_point_target()
    {
        using var folders = TestMediaFolders.Create();
        string outside = Path.Combine(folders.Root, "outside.jpg");
        await File.WriteAllBytesAsync(
            outside,
            [9],
            TestContext.Current.CancellationToken);
        File.CreateSymbolicLink(
            Path.Combine(folders.Library, "photo.jpg"),
            outside);
        var storage = new ControlledMediaStorage(new Sha256FileHasher());
        Guid operationId = Guid.NewGuid();
        StagedFile staged = await StageAsync(
            storage,
            folders,
            operationId,
            "photo.jpg",
            [1, 2, 3]);

        await Assert.ThrowsAsync<ControlledPathException>(() =>
            storage.CommitUniqueAsync(
                operationId,
                folders.Value,
                staged,
                "photo.jpg",
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("../escape.jpg")]
    [InlineData("folder/photo.jpg")]
    [InlineData("name:stream")]
    public async Task CommitUnique_rejects_an_unsafe_requested_name(string requested)
    {
        using var folders = TestMediaFolders.Create();
        var storage = new ControlledMediaStorage(new Sha256FileHasher());
        Guid operationId = Guid.NewGuid();
        StagedFile staged = await StageAsync(
            storage,
            folders,
            operationId,
            "photo.jpg",
            [1, 2, 3]);

        await Assert.ThrowsAsync<ControlledPathException>(() =>
            storage.CommitUniqueAsync(
                operationId,
                folders.Value,
                staged,
                requested,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Replacement_keeps_verified_backup_until_explicit_cleanup()
    {
        using var folders = TestMediaFolders.Create();
        string target = folders.WriteLibrary("photo.jpg", [7, 8]);
        var storage = new ControlledMediaStorage(new Sha256FileHasher());
        LibraryFileSnapshot expected = (await storage.InspectTargetAsync(
            folders.Value,
            "photo.jpg",
            TestContext.Current.CancellationToken))!;
        Guid operationId = Guid.NewGuid();
        StagedFile staged = await StageAsync(
            storage,
            folders,
            operationId,
            "photo.jpg",
            [1, 2, 3]);

        ReplacementBackup backup = await storage.BackupTargetAsync(
            operationId,
            folders.Value,
            expected,
            TestContext.Current.CancellationToken);
        CommittedFile committed = await storage.CommitReplacementAsync(
            operationId,
            folders.Value,
            staged,
            expected,
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(backup.FullPath));
        Assert.Equal([7, 8], await File.ReadAllBytesAsync(
            backup.FullPath,
            TestContext.Current.CancellationToken));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(
            committed.FullPath,
            TestContext.Current.CancellationToken));
        Assert.False(File.Exists(target + ".unexpected"));
    }

    [Fact]
    public async Task Backup_rejects_a_target_changed_after_inspection()
    {
        using var folders = TestMediaFolders.Create();
        string target = folders.WriteLibrary("photo.jpg", [7, 8]);
        var storage = new ControlledMediaStorage(new Sha256FileHasher());
        LibraryFileSnapshot expected = (await storage.InspectTargetAsync(
            folders.Value,
            "photo.jpg",
            TestContext.Current.CancellationToken))!;
        await File.WriteAllBytesAsync(
            target,
            [6, 6],
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<StorageIntegrityException>(() =>
            storage.BackupTargetAsync(
                Guid.NewGuid(),
                folders.Value,
                expected,
                TestContext.Current.CancellationToken));

        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task Backup_rejects_an_operation_path_occupied_by_a_directory()
    {
        using var folders = TestMediaFolders.Create();
        string target = folders.WriteLibrary("photo.jpg", [7, 8]);
        var storage = new ControlledMediaStorage(new Sha256FileHasher());
        LibraryFileSnapshot expected = (await storage.InspectTargetAsync(
            folders.Value,
            "photo.jpg",
            TestContext.Current.CancellationToken))!;
        Guid operationId = Guid.NewGuid();
        Directory.CreateDirectory(Path.Combine(
            folders.Library,
            ".tracefold",
            "recovery",
            operationId.ToString("N"),
            "original.jpg"));

        await Assert.ThrowsAsync<StorageIntegrityException>(() =>
            storage.BackupTargetAsync(
                operationId,
                folders.Value,
                expected,
                TestContext.Current.CancellationToken));

        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task Replacement_rejects_a_reparse_point_in_place_of_its_backup()
    {
        using var folders = TestMediaFolders.Create();
        string target = folders.WriteLibrary("photo.jpg", [7, 8]);
        var storage = new ControlledMediaStorage(new Sha256FileHasher());
        LibraryFileSnapshot expected = (await storage.InspectTargetAsync(
            folders.Value,
            "photo.jpg",
            TestContext.Current.CancellationToken))!;
        Guid operationId = Guid.NewGuid();
        StagedFile staged = await StageAsync(
            storage,
            folders,
            operationId,
            "photo.jpg",
            [1, 2, 3]);
        File.Delete(target);
        string outside = Path.Combine(folders.Root, "outside.jpg");
        await File.WriteAllBytesAsync(
            outside,
            [7, 8],
            TestContext.Current.CancellationToken);
        string recoveryDirectory = Directory.CreateDirectory(Path.Combine(
            folders.Library,
            ".tracefold",
            "recovery",
            operationId.ToString("N"))).FullName;
        File.CreateSymbolicLink(
            Path.Combine(recoveryDirectory, "original.jpg"),
            outside);

        await Assert.ThrowsAsync<ControlledPathException>(() =>
            storage.CommitReplacementAsync(
                operationId,
                folders.Value,
                staged,
                expected,
                TestContext.Current.CancellationToken));

        Assert.False(File.Exists(target));
        Assert.Equal(
            [7, 8],
            await File.ReadAllBytesAsync(
                outside,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Repeated_backup_and_replacement_are_idempotent_for_exact_files()
    {
        using var folders = TestMediaFolders.Create();
        folders.WriteLibrary("photo.jpg", [7, 8]);
        var storage = new ControlledMediaStorage(new Sha256FileHasher());
        LibraryFileSnapshot expected = (await storage.InspectTargetAsync(
            folders.Value,
            "photo.jpg",
            TestContext.Current.CancellationToken))!;
        Guid operationId = Guid.NewGuid();
        StagedFile staged = await StageAsync(
            storage,
            folders,
            operationId,
            "photo.jpg",
            [1, 2, 3]);

        ReplacementBackup firstBackup = await storage.BackupTargetAsync(
            operationId,
            folders.Value,
            expected,
            TestContext.Current.CancellationToken);
        ReplacementBackup secondBackup = await storage.BackupTargetAsync(
            operationId,
            folders.Value,
            expected,
            TestContext.Current.CancellationToken);
        CommittedFile firstCommit = await storage.CommitReplacementAsync(
            operationId,
            folders.Value,
            staged,
            expected,
            TestContext.Current.CancellationToken);
        CommittedFile secondCommit = await storage.CommitReplacementAsync(
            operationId,
            folders.Value,
            staged,
            expected,
            TestContext.Current.CancellationToken);

        Assert.Equal(firstBackup, secondBackup);
        Assert.Equal(firstCommit, secondCommit);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(
            firstCommit.FullPath,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Restore_refuses_when_target_contains_unknown_content()
    {
        using var folders = TestMediaFolders.Create();
        ReplacementFixture replacement = await ReplaceAsync(folders);
        LibraryFileSnapshot committed = (await replacement.Storage.InspectTargetAsync(
            folders.Value,
            "photo.jpg",
            TestContext.Current.CancellationToken))!;
        await File.WriteAllBytesAsync(
            committed.FullPath,
            [6, 6],
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<StorageIntegrityException>(() =>
            replacement.Storage.RestoreBackupAsync(
                replacement.OperationId,
                folders.Value,
                committed,
                replacement.Backup,
                TestContext.Current.CancellationToken));

        Assert.True(File.Exists(replacement.Backup.FullPath));
        Assert.Equal([6, 6], await File.ReadAllBytesAsync(
            committed.FullPath,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Restore_replaces_only_the_verified_new_target_with_the_backup()
    {
        using var folders = TestMediaFolders.Create();
        ReplacementFixture replacement = await ReplaceAsync(folders);
        LibraryFileSnapshot committed = (await replacement.Storage.InspectTargetAsync(
            folders.Value,
            "photo.jpg",
            TestContext.Current.CancellationToken))!;

        await replacement.Storage.RestoreBackupAsync(
            replacement.OperationId,
            folders.Value,
            committed,
            replacement.Backup,
            TestContext.Current.CancellationToken);

        Assert.Equal([7, 8], await File.ReadAllBytesAsync(
            committed.FullPath,
            TestContext.Current.CancellationToken));
        Assert.False(File.Exists(replacement.Backup.FullPath));
    }

    [Fact]
    public async Task DeleteBackupIfMatches_deletes_only_the_selected_operation_backup()
    {
        using var folders = TestMediaFolders.Create();
        ReplacementFixture selected = await ReplaceAsync(folders);
        Guid siblingId = Guid.NewGuid();
        string siblingDirectory = Directory.CreateDirectory(Path.Combine(
            folders.Library,
            ".tracefold",
            "recovery",
            siblingId.ToString("N"))).FullName;
        string sibling = Path.Combine(siblingDirectory, "original.jpg");
        await File.WriteAllBytesAsync(
            sibling,
            [5],
            TestContext.Current.CancellationToken);

        SourceDeleteResult result = await selected.Storage.DeleteBackupIfMatchesAsync(
            folders.Value,
            selected.Backup,
            TestContext.Current.CancellationToken);

        Assert.Equal(SourceDeleteOutcome.Deleted, result.Outcome);
        Assert.False(File.Exists(selected.Backup.FullPath));
        Assert.True(File.Exists(sibling));
    }

    [Fact]
    public async Task DeleteBackupIfMatches_preserves_a_changed_backup()
    {
        using var folders = TestMediaFolders.Create();
        ReplacementFixture replacement = await ReplaceAsync(folders);
        await File.WriteAllBytesAsync(
            replacement.Backup.FullPath,
            [6, 6],
            TestContext.Current.CancellationToken);

        SourceDeleteResult result =
            await replacement.Storage.DeleteBackupIfMatchesAsync(
                folders.Value,
                replacement.Backup,
                TestContext.Current.CancellationToken);

        Assert.Equal(SourceDeleteOutcome.SourceChanged, result.Outcome);
        Assert.Equal(
            [6, 6],
            await File.ReadAllBytesAsync(
                replacement.Backup.FullPath,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Replacement_backup_cleanup_kind_uses_guarded_backup_deletion()
    {
        using var folders = TestMediaFolders.Create();
        ReplacementFixture replacement = await ReplaceAsync(folders);
        var cleanup = new IngestCleanupFile(
            IngestCleanupKind.ReplacementBackup,
            replacement.Backup.FullPath,
            replacement.Backup.Size,
            replacement.Backup.Sha256);

        SourceDeleteResult result =
            await replacement.Storage.DeleteCleanupFileIfMatchesAsync(
                folders.Value,
                cleanup,
                TestContext.Current.CancellationToken);

        Assert.Equal(SourceDeleteOutcome.Deleted, result.Outcome);
        Assert.False(File.Exists(replacement.Backup.FullPath));
    }

    private static async Task<StagedFile> StageAsync(
        ControlledMediaStorage storage,
        TestMediaFolders folders,
        Guid operationId,
        string filename,
        byte[] bytes)
    {
        string source = folders.WriteInbox(filename, bytes);
        string hash = await HashAsync(source);
        return await storage.StageAsync(
            operationId,
            folders.Value,
            source,
            hash,
            TestContext.Current.CancellationToken);
    }

    private static async Task<ReplacementFixture> ReplaceAsync(
        TestMediaFolders folders)
    {
        folders.WriteLibrary("photo.jpg", [7, 8]);
        var storage = new ControlledMediaStorage(new Sha256FileHasher());
        LibraryFileSnapshot expected = (await storage.InspectTargetAsync(
            folders.Value,
            "photo.jpg",
            TestContext.Current.CancellationToken))!;
        Guid operationId = Guid.NewGuid();
        StagedFile staged = await StageAsync(
            storage,
            folders,
            operationId,
            "photo.jpg",
            [1, 2, 3]);
        ReplacementBackup backup = await storage.BackupTargetAsync(
            operationId,
            folders.Value,
            expected,
            TestContext.Current.CancellationToken);
        _ = await storage.CommitReplacementAsync(
            operationId,
            folders.Value,
            staged,
            expected,
            TestContext.Current.CancellationToken);
        return new ReplacementFixture(storage, operationId, backup);
    }

    private static Task<string> HashAsync(string path) =>
        new Sha256FileHasher().ComputeSha256Async(
            path,
            TestContext.Current.CancellationToken);

    private sealed class TargetCreatingHasher(
        string libraryRoot,
        string target) : IFileHasher
    {
        private readonly Sha256FileHasher _inner = new();
        private bool _created;

        public async Task<string> ComputeSha256Async(
            string fullPath,
            CancellationToken cancellationToken)
        {
            string hash = await _inner.ComputeSha256Async(
                fullPath,
                cancellationToken);
            if (!_created
                && fullPath.StartsWith(
                    Path.Combine(libraryRoot, ".tracefold", "commit"),
                    StringComparison.OrdinalIgnoreCase))
            {
                _created = true;
                await File.WriteAllBytesAsync(target, [9], cancellationToken);
            }

            return hash;
        }
    }

    private sealed record ReplacementFixture(
        ControlledMediaStorage Storage,
        Guid OperationId,
        ReplacementBackup Backup);
}
