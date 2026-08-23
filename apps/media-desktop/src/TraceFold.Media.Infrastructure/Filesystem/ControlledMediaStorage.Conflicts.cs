using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;

namespace TraceFold.Media.Infrastructure.Filesystem;

public sealed partial class ControlledMediaStorage
{
    public async Task<LibraryFileSnapshot?> InspectTargetAsync(
        LibraryFolders folders,
        string targetFilename,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        cancellationToken.ThrowIfCancellationRequested();
        string target = ResolveTarget(folders, targetFilename);
        bool entryExists = PathEntryExists(target);
        ControlledReparseGuard.RejectPath(
            folders.LibraryDirectory,
            target,
            includeCandidate: entryExists);
        if (!entryExists)
        {
            return null;
        }

        if (!File.Exists(target))
        {
            throw new StorageConflictException(
                "The formal target path is occupied by a directory.");
        }

        var before = new FileInfo(target);
        long size = before.Length;
        DateTimeOffset lastWriteTimeUtc = before.LastWriteTimeUtc;
        string sha256 = await hasher.ComputeSha256Async(target, cancellationToken);
        var after = new FileInfo(target);
        if (!after.Exists
            || after.Length != size
            || after.LastWriteTimeUtc != lastWriteTimeUtc)
        {
            throw new StorageIntegrityException(
                "The formal target changed while it was being inspected.");
        }

        return new LibraryFileSnapshot(
            target,
            targetFilename,
            size,
            lastWriteTimeUtc,
            RequireSha256(sha256));
    }

    public async Task<CommittedFile> CommitUniqueAsync(
        Guid operationId,
        LibraryFolders folders,
        StagedFile staged,
        string requestedFilename,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(staged);
        string requested = RequireTargetFilename(requestedFilename);
        string payload = await PrepareCommitPayloadAsync(
            operationId,
            folders,
            staged,
            cancellationToken);

        CommittedFile? requestedResult = await TryMoveCommitPayloadAsync(
            folders,
            payload,
            requested,
            staged,
            cancellationToken);
        if (requestedResult is not null)
        {
            CleanupEmptyCommitParents(folders, operationId);
            return requestedResult;
        }

        string extension = Path.GetExtension(requested);
        string stem = requested[..^extension.Length];
        for (int suffix = 2; ; suffix++)
        {
            string candidate = $"{stem} ({suffix}){extension}";
            CommittedFile? result = await TryMoveCommitPayloadAsync(
                folders,
                payload,
                candidate,
                staged,
                cancellationToken);
            if (result is not null)
            {
                CleanupEmptyCommitParents(folders, operationId);
                return result;
            }
        }
    }

    public async Task<ReplacementBackup> BackupTargetAsync(
        Guid operationId,
        LibraryFolders folders,
        LibraryFileSnapshot expected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(expected);
        string target = RequireSnapshotTarget(folders, expected);
        string backupPath = ResolveBackupPath(
            folders,
            operationId,
            expected.RelativePath);
        bool backupEntryExists = PathEntryExists(backupPath);
        ControlledReparseGuard.RejectPath(
            folders.LibraryDirectory,
            backupPath,
            includeCandidate: backupEntryExists);
        if (backupEntryExists)
        {
            if (!File.Exists(backupPath))
            {
                throw new StorageIntegrityException(
                    "The recovery backup path is not a file.");
            }

            if (File.Exists(target) || Directory.Exists(target))
            {
                throw new StorageIntegrityException(
                    "Both the formal target and its recovery backup exist.");
            }

            await VerifyFileAsync(
                backupPath,
                expected.Size,
                expected.Sha256,
                cancellationToken);
            return CreateBackup(folders, backupPath, expected.Size, expected.Sha256);
        }

        await VerifySnapshotAsync(target, expected, cancellationToken);
        string recoveryDirectory = ResolveRecoveryDirectory(folders, operationId);
        EnsureOwnedDirectory(folders.LibraryDirectory, recoveryDirectory);
        try
        {
            File.Move(target, backupPath, overwrite: false);
        }
        catch (IOException) when (File.Exists(backupPath) && !File.Exists(target))
        {
            await VerifyFileAsync(
                backupPath,
                expected.Size,
                expected.Sha256,
                cancellationToken);
            return CreateBackup(folders, backupPath, expected.Size, expected.Sha256);
        }

        try
        {
            await VerifySnapshotAsync(backupPath, expected, cancellationToken);
        }
        catch
        {
            if (!PathEntryExists(target) && File.Exists(backupPath))
            {
                File.Move(backupPath, target, overwrite: false);
            }

            throw;
        }

        return CreateBackup(folders, backupPath, expected.Size, expected.Sha256);
    }

    public async Task<CommittedFile> CommitReplacementAsync(
        Guid operationId,
        LibraryFolders folders,
        StagedFile staged,
        LibraryFileSnapshot expectedTarget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(staged);
        ArgumentNullException.ThrowIfNull(expectedTarget);
        string target = RequireSnapshotTarget(folders, expectedTarget);
        string backupPath = ResolveBackupPath(
            folders,
            operationId,
            expectedTarget.RelativePath);
        ControlledReparseGuard.RejectPath(
            folders.LibraryDirectory,
            backupPath,
            includeCandidate: PathEntryExists(backupPath));
        await VerifyFileAsync(
            backupPath,
            expectedTarget.Size,
            expectedTarget.Sha256,
            cancellationToken);

        if (Directory.Exists(target))
        {
            throw new StorageConflictException(
                "The formal target path is occupied by a directory.");
        }

        if (File.Exists(target))
        {
            ControlledReparseGuard.RejectPath(
                folders.LibraryDirectory,
                target,
                includeCandidate: true);
            if (await MatchesFileAsync(
                    target,
                    staged.Size,
                    staged.Sha256,
                    null,
                    cancellationToken))
            {
                return new CommittedFile(
                    target,
                    expectedTarget.RelativePath,
                    staged.Size,
                    RequireSha256(staged.Sha256));
            }

            throw new StorageIntegrityException(
                "The formal target is not absent before replacement.");
        }

        string payload = await PrepareCommitPayloadAsync(
            operationId,
            folders,
            staged,
            cancellationToken);
        try
        {
            File.Move(payload, target, overwrite: false);
        }
        catch (IOException) when (File.Exists(target))
        {
            ControlledReparseGuard.RejectPath(
                folders.LibraryDirectory,
                target,
                includeCandidate: true);
            if (!await MatchesFileAsync(
                    target,
                    staged.Size,
                    staged.Sha256,
                    null,
                    cancellationToken))
            {
                throw new StorageIntegrityException(
                    "Unknown content won the replacement target name.");
            }
        }

        await VerifyFileAsync(
            target,
            staged.Size,
            staged.Sha256,
            cancellationToken);
        CleanupEmptyCommitParents(folders, operationId);
        return new CommittedFile(
            target,
            expectedTarget.RelativePath,
            staged.Size,
            RequireSha256(staged.Sha256));
    }

    public async Task RestoreBackupAsync(
        Guid operationId,
        LibraryFolders folders,
        LibraryFileSnapshot target,
        ReplacementBackup backup,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(backup);
        string targetPath = RequireSnapshotTarget(folders, target);
        string backupPath = RequireOperationBackup(
            folders,
            operationId,
            target.RelativePath,
            backup);
        if (!File.Exists(backupPath))
        {
            if (File.Exists(targetPath)
                && await MatchesFileAsync(
                    targetPath,
                    backup.Size,
                    backup.Sha256,
                    null,
                    cancellationToken))
            {
                return;
            }

            throw new StorageIntegrityException(
                "The verified recovery backup is missing.");
        }

        await VerifyFileAsync(
            backupPath,
            backup.Size,
            backup.Sha256,
            cancellationToken);
        if (Directory.Exists(targetPath))
        {
            throw new StorageIntegrityException(
                "A directory occupies the formal target during restore.");
        }

        if (File.Exists(targetPath))
        {
            ControlledReparseGuard.RejectPath(
                folders.LibraryDirectory,
                targetPath,
                includeCandidate: true);
            if (await MatchesFileAsync(
                    targetPath,
                    backup.Size,
                    backup.Sha256,
                    null,
                    cancellationToken))
            {
                return;
            }

            if (!await MatchesFileAsync(
                    targetPath,
                    target.Size,
                    target.Sha256,
                    target.LastWriteTimeUtc,
                    cancellationToken))
            {
                throw new StorageIntegrityException(
                    "The formal target contains unknown content.");
            }

            string commitDirectory = ResolveCommitDirectory(folders, operationId);
            EnsureOwnedDirectory(folders.LibraryDirectory, commitDirectory);
            string quarantine = ControlledPathGuard.RequireDescendant(
                folders.LibraryDirectory,
                Path.Combine(commitDirectory, "replaced"));
            if (File.Exists(quarantine) || Directory.Exists(quarantine))
            {
                throw new StorageConflictException(
                    "The replacement quarantine already exists.");
            }

            File.Move(targetPath, quarantine, overwrite: false);
            try
            {
                await VerifySnapshotAsync(quarantine, target, cancellationToken);
            }
            catch
            {
                if (!File.Exists(targetPath) && File.Exists(quarantine))
                {
                    File.Move(quarantine, targetPath, overwrite: false);
                }

                throw;
            }
        }

        File.Move(backupPath, targetPath, overwrite: false);
        await VerifyFileAsync(
            targetPath,
            backup.Size,
            backup.Sha256,
            cancellationToken);
        CleanupEmptyRecoveryParents(folders, operationId);
    }

    public async Task<SourceDeleteResult> DeleteBackupIfMatchesAsync(
        LibraryFolders folders,
        ReplacementBackup backup,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(backup);
        (Guid operationId, string backupPath) = RequirePersistedBackup(
            folders,
            backup);
        if (!PathEntryExists(backupPath))
        {
            return new SourceDeleteResult(SourceDeleteOutcome.SourceMissing);
        }

        if (!File.Exists(backupPath))
        {
            return new SourceDeleteResult(SourceDeleteOutcome.SourceChanged);
        }

        ControlledReparseGuard.RejectPath(
            folders.LibraryDirectory,
            backupPath,
            includeCandidate: true);
        if (new FileInfo(backupPath).Length != backup.Size)
        {
            return new SourceDeleteResult(SourceDeleteOutcome.SourceChanged);
        }

        string operationDirectory = ResolveRecoveryDirectory(folders, operationId);
        string quarantine = ControlledPathGuard.RequireDescendant(
            folders.LibraryDirectory,
            Path.Combine(
                operationDirectory,
                $".tracefold-delete-{Guid.NewGuid():N}.tmp"));
        try
        {
            File.Move(backupPath, quarantine, overwrite: false);
        }
        catch (FileNotFoundException)
        {
            return new SourceDeleteResult(SourceDeleteOutcome.SourceMissing);
        }
        catch (DirectoryNotFoundException)
        {
            return new SourceDeleteResult(SourceDeleteOutcome.SourceMissing);
        }
        try
        {
            if (!await MatchesFileAsync(
                    quarantine,
                    backup.Size,
                    backup.Sha256,
                    null,
                    cancellationToken))
            {
                return new SourceDeleteResult(SourceDeleteOutcome.SourceChanged);
            }

            File.Delete(quarantine);
            CleanupEmptyRecoveryParents(folders, operationId);
            return new SourceDeleteResult(SourceDeleteOutcome.Deleted);
        }
        finally
        {
            if (File.Exists(quarantine) && !File.Exists(backupPath))
            {
                try
                {
                    File.Move(quarantine, backupPath, overwrite: false);
                }
                catch (IOException) when (File.Exists(backupPath))
                {
                    // Preserve both the verified backup and a concurrent winner.
                }
            }
        }
    }

}
