using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;

namespace TraceFold.Media.Infrastructure.Filesystem;

public sealed partial class ControlledMediaStorage
{
    private async Task<string> PrepareCommitPayloadAsync(
        Guid operationId,
        LibraryFolders folders,
        StagedFile staged,
        CancellationToken cancellationToken)
    {
        string expectedPayload = ControlledPathGuard.RequireDescendant(
            folders.WorkDirectory,
            Path.Combine(
                ResolveOperationDirectory(folders, operationId),
                "payload"));
        string stagedPath = ControlledPathGuard.RequireDescendant(
            folders.WorkDirectory,
            staged.FullPath);
        if (!StringComparer.OrdinalIgnoreCase.Equals(expectedPayload, stagedPath))
        {
            throw new ControlledPathException(
                "The staged payload does not belong to the requested operation.");
        }

        ControlledReparseGuard.RejectPath(
            folders.WorkDirectory,
            stagedPath,
            includeCandidate: true);
        await VerifyFileAsync(
            stagedPath,
            staged.Size,
            staged.Sha256,
            cancellationToken);

        string commitDirectory = ResolveCommitDirectory(folders, operationId);
        EnsureOwnedDirectory(folders.LibraryDirectory, commitDirectory);
        string payload = ControlledPathGuard.RequireDescendant(
            folders.LibraryDirectory,
            Path.Combine(commitDirectory, "payload"));
        if (PathEntryExists(payload))
        {
            ControlledReparseGuard.RejectPath(
                folders.LibraryDirectory,
                payload,
                includeCandidate: true);
            if (!File.Exists(payload))
            {
                throw new StorageIntegrityException(
                    "The operation commit payload path is not a file.");
            }

            await VerifyFileAsync(
                payload,
                staged.Size,
                staged.Sha256,
                cancellationToken);
            return payload;
        }

        string copying = ControlledPathGuard.RequireDescendant(
            folders.LibraryDirectory,
            Path.Combine(
                commitDirectory,
                $"copy-{Guid.NewGuid():N}.tmp"));
        try
        {
            await CopyAndFlushAsync(stagedPath, copying, cancellationToken);
            await VerifyFileAsync(
                copying,
                staged.Size,
                staged.Sha256,
                cancellationToken);
            try
            {
                File.Move(copying, payload, overwrite: false);
            }
            catch (IOException) when (File.Exists(payload))
            {
                ControlledReparseGuard.RejectPath(
                    folders.LibraryDirectory,
                    payload,
                    includeCandidate: true);
                await VerifyFileAsync(
                    payload,
                    staged.Size,
                    staged.Sha256,
                    cancellationToken);
            }
        }
        finally
        {
            if (File.Exists(copying))
            {
                ControlledReparseGuard.RejectPath(
                    folders.LibraryDirectory,
                    copying,
                    includeCandidate: true);
                File.Delete(copying);
            }
        }

        await VerifyFileAsync(
            payload,
            staged.Size,
            staged.Sha256,
            cancellationToken);
        return payload;
    }

    private async Task<CommittedFile?> TryMoveCommitPayloadAsync(
        LibraryFolders folders,
        string payload,
        string targetFilename,
        StagedFile staged,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string target = ResolveTarget(folders, targetFilename);
        if (PathEntryExists(target))
        {
            ControlledReparseGuard.RejectPath(
                folders.LibraryDirectory,
                target,
                includeCandidate: true);
            return null;
        }

        ControlledReparseGuard.RejectPath(
            folders.LibraryDirectory,
            target,
            includeCandidate: false);
        try
        {
            File.Move(payload, target, overwrite: false);
        }
        catch (IOException) when (PathEntryExists(target))
        {
            ControlledReparseGuard.RejectPath(
                folders.LibraryDirectory,
                target,
                includeCandidate: true);
            return null;
        }

        await VerifyFileAsync(
            target,
            staged.Size,
            staged.Sha256,
            cancellationToken);
        return new CommittedFile(
            target,
            targetFilename,
            staged.Size,
            RequireSha256(staged.Sha256));
    }

    private static string RequireSnapshotTarget(
        LibraryFolders folders,
        LibraryFileSnapshot snapshot)
    {
        if (snapshot.Size < 0)
        {
            throw new StorageIntegrityException(
                "The target snapshot size is invalid.");
        }

        _ = RequireSha256(snapshot.Sha256);
        string target = ResolveTarget(folders, snapshot.RelativePath);
        string supplied = ControlledPathGuard.RequireDirectChild(
            folders.LibraryDirectory,
            snapshot.FullPath);
        if (!StringComparer.OrdinalIgnoreCase.Equals(target, supplied))
        {
            throw new ControlledPathException(
                "The target snapshot paths do not identify the same file.");
        }

        ControlledReparseGuard.RejectPath(
            folders.LibraryDirectory,
            target,
            includeCandidate: PathEntryExists(target));
        return target;
    }

    private static string ResolveCommitDirectory(
        LibraryFolders folders,
        Guid operationId) =>
        ControlledPathGuard.RequireDescendant(
            folders.LibraryDirectory,
            Path.Combine(
                folders.LibraryDirectory,
                ".tracefold",
                "commit",
                operationId.ToString("N")));

    private static string ResolveRecoveryDirectory(
        LibraryFolders folders,
        Guid operationId) =>
        ControlledPathGuard.RequireDescendant(
            folders.LibraryDirectory,
            Path.Combine(
                folders.LibraryDirectory,
                ".tracefold",
                "recovery",
                operationId.ToString("N")));

    private static string ResolveBackupPath(
        LibraryFolders folders,
        Guid operationId,
        string targetFilename)
    {
        string filename = RequireTargetFilename(targetFilename);
        string recoveryDirectory = ResolveRecoveryDirectory(
            folders,
            operationId);
        return ControlledPathGuard.RequireDescendant(
            folders.LibraryDirectory,
            Path.Combine(
                recoveryDirectory,
                $"original{Path.GetExtension(filename)}"));
    }

    private static void EnsureOwnedDirectory(
        string root,
        string ownedDirectory)
    {
        string controlled = ControlledPathGuard.RequireDescendant(
            root,
            ownedDirectory);
        bool entryExists = PathEntryExists(controlled);
        ControlledReparseGuard.RejectPath(
            root,
            controlled,
            includeCandidate: entryExists);
        if (entryExists && !Directory.Exists(controlled))
        {
            throw new StorageIntegrityException(
                "A non-directory entry occupies an operation-owned directory path.");
        }

        if (!entryExists)
        {
            Directory.CreateDirectory(controlled);
        }

        ControlledReparseGuard.RejectPath(
            root,
            controlled,
            includeCandidate: true);
    }

    private async Task VerifySnapshotAsync(
        string path,
        LibraryFileSnapshot expected,
        CancellationToken cancellationToken)
    {
        string expectedHash = RequireSha256(expected.Sha256);
        if (!File.Exists(path))
        {
            throw new StorageIntegrityException(
                "The snapshotted file is missing.");
        }

        var before = new FileInfo(path);
        if (before.Length != expected.Size
            || before.LastWriteTimeUtc != expected.LastWriteTimeUtc.UtcDateTime)
        {
            throw new StorageIntegrityException(
                "The file no longer matches the persisted snapshot.");
        }

        string actualHash = await hasher.ComputeSha256Async(
            path,
            cancellationToken);
        var after = new FileInfo(path);
        if (!after.Exists
            || after.Length != expected.Size
            || after.LastWriteTimeUtc != expected.LastWriteTimeUtc.UtcDateTime
            || !StringComparer.OrdinalIgnoreCase.Equals(actualHash, expectedHash))
        {
            throw new StorageIntegrityException(
                "The file no longer matches the persisted snapshot.");
        }
    }

    private async Task<bool> MatchesFileAsync(
        string path,
        long expectedSize,
        string expectedSha256,
        DateTimeOffset? expectedLastWriteTimeUtc,
        CancellationToken cancellationToken)
    {
        string expectedHash = RequireSha256(expectedSha256);
        if (!File.Exists(path))
        {
            return false;
        }

        var before = new FileInfo(path);
        if (before.Length != expectedSize
            || (expectedLastWriteTimeUtc is not null
                && before.LastWriteTimeUtc
                    != expectedLastWriteTimeUtc.Value.UtcDateTime))
        {
            return false;
        }

        DateTime observedLastWriteTimeUtc = before.LastWriteTimeUtc;
        string actualHash = await hasher.ComputeSha256Async(
            path,
            cancellationToken);
        var after = new FileInfo(path);
        return after.Exists
            && after.Length == expectedSize
            && after.LastWriteTimeUtc == observedLastWriteTimeUtc
            && (expectedLastWriteTimeUtc is null
                || after.LastWriteTimeUtc
                    == expectedLastWriteTimeUtc.Value.UtcDateTime)
            && StringComparer.OrdinalIgnoreCase.Equals(actualHash, expectedHash);
    }

    private static ReplacementBackup CreateBackup(
        LibraryFolders folders,
        string backupPath,
        long size,
        string sha256) =>
        new(
            backupPath,
            Path.GetRelativePath(folders.LibraryDirectory, backupPath),
            size,
            RequireSha256(sha256));

    private static string RequireOperationBackup(
        LibraryFolders folders,
        Guid operationId,
        string targetFilename,
        ReplacementBackup backup)
    {
        (Guid persistedOperationId, string supplied) =
            RequirePersistedBackup(folders, backup);
        string expected = ResolveBackupPath(
            folders,
            operationId,
            targetFilename);
        if (persistedOperationId != operationId
            || !StringComparer.OrdinalIgnoreCase.Equals(expected, supplied))
        {
            throw new ControlledPathException(
                "The recovery backup does not belong to the requested operation.");
        }

        return supplied;
    }

    private static (Guid OperationId, string BackupPath) RequirePersistedBackup(
        LibraryFolders folders,
        ReplacementBackup backup)
    {
        if (backup.Size < 0)
        {
            throw new StorageIntegrityException(
                "The recovery backup size is invalid.");
        }

        _ = RequireSha256(backup.Sha256);
        if (string.IsNullOrWhiteSpace(backup.RelativePath)
            || Path.IsPathRooted(backup.RelativePath))
        {
            throw new ControlledPathException(
                "The recovery backup relative path is invalid.");
        }

        string fromRelative = ControlledPathGuard.RequireDescendant(
            folders.LibraryDirectory,
            Path.Combine(folders.LibraryDirectory, backup.RelativePath));
        string supplied = ControlledPathGuard.RequireDescendant(
            folders.LibraryDirectory,
            backup.FullPath);
        if (!StringComparer.OrdinalIgnoreCase.Equals(fromRelative, supplied))
        {
            throw new ControlledPathException(
                "The recovery backup paths do not identify the same file.");
        }

        string? operationDirectory = Path.GetDirectoryName(supplied);
        string? categoryDirectory = operationDirectory is null
            ? null
            : Path.GetDirectoryName(operationDirectory);
        string? internalDirectory = categoryDirectory is null
            ? null
            : Path.GetDirectoryName(categoryDirectory);
        string backupName = Path.GetFileName(supplied);
        if (operationDirectory is null
            || categoryDirectory is null
            || internalDirectory is null
            || !StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetFileName(categoryDirectory),
                "recovery")
            || !StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetFileName(internalDirectory),
                ".tracefold")
            || !StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetDirectoryName(internalDirectory),
                Path.TrimEndingDirectorySeparator(folders.LibraryDirectory))
            || !Guid.TryParseExact(
                Path.GetFileName(operationDirectory),
                "N",
                out Guid operationId)
            || !StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetFileNameWithoutExtension(backupName),
                "original"))
        {
            throw new ControlledPathException(
                "The recovery backup is outside a controlled operation path.");
        }

        string expectedOperationDirectory = ResolveRecoveryDirectory(
            folders,
            operationId);
        if (!StringComparer.OrdinalIgnoreCase.Equals(
                expectedOperationDirectory,
                operationDirectory))
        {
            throw new ControlledPathException(
                "The recovery backup operation path is invalid.");
        }

        ControlledReparseGuard.RejectPath(
            folders.LibraryDirectory,
            supplied,
            includeCandidate: PathEntryExists(supplied));
        return (operationId, supplied);
    }

    private static void CleanupEmptyCommitParents(
        LibraryFolders folders,
        Guid operationId)
    {
        string operationDirectory = ResolveCommitDirectory(folders, operationId);
        string categoryDirectory = ControlledPathGuard.RequireDescendant(
            folders.LibraryDirectory,
            Path.Combine(folders.LibraryDirectory, ".tracefold", "commit"));
        string internalDirectory = ControlledPathGuard.RequireDescendant(
            folders.LibraryDirectory,
            Path.Combine(folders.LibraryDirectory, ".tracefold"));
        TryDeleteEmptyOwnedDirectory(
            folders.LibraryDirectory,
            operationDirectory);
        TryDeleteEmptyOwnedDirectory(
            folders.LibraryDirectory,
            categoryDirectory);
        TryDeleteEmptyOwnedDirectory(
            folders.LibraryDirectory,
            internalDirectory);
    }

    private static void CleanupEmptyRecoveryParents(
        LibraryFolders folders,
        Guid operationId)
    {
        string operationDirectory = ResolveRecoveryDirectory(folders, operationId);
        string categoryDirectory = ControlledPathGuard.RequireDescendant(
            folders.LibraryDirectory,
            Path.Combine(folders.LibraryDirectory, ".tracefold", "recovery"));
        string internalDirectory = ControlledPathGuard.RequireDescendant(
            folders.LibraryDirectory,
            Path.Combine(folders.LibraryDirectory, ".tracefold"));
        TryDeleteEmptyOwnedDirectory(
            folders.LibraryDirectory,
            operationDirectory);
        TryDeleteEmptyOwnedDirectory(
            folders.LibraryDirectory,
            categoryDirectory);
        TryDeleteEmptyOwnedDirectory(
            folders.LibraryDirectory,
            internalDirectory);
    }

    private static void TryDeleteEmptyOwnedDirectory(
        string root,
        string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        ControlledReparseGuard.RejectPath(
            root,
            directory,
            includeCandidate: true);
        if (Directory.EnumerateFileSystemEntries(directory).Any())
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: false);
        }
        catch (IOException) when (Directory.Exists(directory))
        {
            // A concurrent operation populated the directory after inspection.
        }
    }

}
