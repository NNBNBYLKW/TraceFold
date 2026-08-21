using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;

namespace TraceFold.Media.Infrastructure.Filesystem;

public sealed class ControlledMediaStorage(IFileHasher hasher) : IControlledMediaStorage
{
    public Task<bool> TargetExistsAsync(
        LibraryFolders folders,
        string targetFilename,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string target = ResolveTarget(folders, targetFilename);
        RejectReparsePoint(folders.LibraryDirectory, target, includeCandidate: false);
        return Task.FromResult(File.Exists(target));
    }

    public async Task<StagedFile> StageAsync(
        Guid operationId,
        LibraryFolders folders,
        string sourcePath,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        string expectedHash = RequireSha256(expectedSha256);
        string source = ControlledPathGuard.RequireDirectChild(
            folders.InboxDirectory,
            sourcePath);
        RejectReparsePoint(folders.InboxDirectory, source, includeCandidate: true);

        string operationDirectory = ResolveOperationDirectory(folders, operationId);
        RejectReparsePoint(
            folders.WorkDirectory,
            operationDirectory,
            includeCandidate: false);
        Directory.CreateDirectory(operationDirectory);
        RejectReparsePoint(
            folders.WorkDirectory,
            operationDirectory,
            includeCandidate: true);
        string payload = ControlledPathGuard.RequireDescendant(
            folders.WorkDirectory,
            Path.Combine(operationDirectory, "payload"));
        if (File.Exists(payload))
        {
            throw new StorageConflictException(
                "The operation staging payload already exists.");
        }

        try
        {
            await CopyAndFlushAsync(source, payload, cancellationToken);
            long size = new FileInfo(payload).Length;
            string actualHash = await hasher.ComputeSha256Async(payload, cancellationToken);
            if (!StringComparer.OrdinalIgnoreCase.Equals(actualHash, expectedHash))
            {
                throw new StorageIntegrityException(
                    "The staged payload does not match the expected SHA-256 hash.");
            }

            return new StagedFile(payload, size, actualHash);
        }
        catch
        {
            if (File.Exists(payload))
            {
                File.Delete(payload);
            }

            throw;
        }
    }

    public async Task<CommittedFile> CommitAsync(
        Guid operationId,
        LibraryFolders folders,
        StagedFile staged,
        string targetFilename,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(staged);
        string expectedPayload = ControlledPathGuard.RequireDescendant(
            folders.WorkDirectory,
            Path.Combine(ResolveOperationDirectory(folders, operationId), "payload"));
        string stagedPath = ControlledPathGuard.RequireDescendant(
            folders.WorkDirectory,
            staged.FullPath);
        if (!StringComparer.OrdinalIgnoreCase.Equals(expectedPayload, stagedPath))
        {
            throw new ControlledPathException(
                "The staged payload does not belong to the requested operation.");
        }

        RejectReparsePoint(folders.WorkDirectory, stagedPath, includeCandidate: true);
        string target = ResolveTarget(folders, targetFilename);
        string temporary = ControlledPathGuard.RequireDirectChild(
            folders.LibraryDirectory,
            Path.Combine(
                folders.LibraryDirectory,
                $".tracefold-{operationId:N}.tmp"));
        RejectReparsePoint(folders.LibraryDirectory, target, includeCandidate: false);
        RejectReparsePoint(folders.LibraryDirectory, temporary, includeCandidate: false);
        if (File.Exists(target))
        {
            throw new StorageConflictException("The formal target already exists.");
        }

        if (File.Exists(temporary))
        {
            throw new StorageConflictException(
                "The operation target-side temporary file already exists.");
        }

        await VerifyFileAsync(stagedPath, staged.Size, staged.Sha256, cancellationToken);
        try
        {
            await CopyAndFlushAsync(stagedPath, temporary, cancellationToken);
            await VerifyFileAsync(temporary, staged.Size, staged.Sha256, cancellationToken);
            if (File.Exists(target))
            {
                throw new StorageConflictException("The formal target already exists.");
            }

            try
            {
                File.Move(temporary, target, overwrite: false);
            }
            catch (IOException error) when (File.Exists(target))
            {
                throw new StorageConflictException(
                    "The formal target was created concurrently.",
                    error);
            }
        }
        catch
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            throw;
        }

        return new CommittedFile(
            target,
            targetFilename,
            staged.Size,
            staged.Sha256);
    }

    public async Task<SourceDeleteResult> DeleteSourceIfMatchesAsync(
        LibraryFolders folders,
        string sourcePath,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        string expectedHash = RequireSha256(expectedSha256);
        string source = ControlledPathGuard.RequireDirectChild(
            folders.InboxDirectory,
            sourcePath);
        if (!File.Exists(source))
        {
            return new SourceDeleteResult(SourceDeleteOutcome.SourceMissing);
        }

        RejectReparsePoint(folders.InboxDirectory, source, includeCandidate: true);
        if (new FileInfo(source).Length != expectedSize)
        {
            return new SourceDeleteResult(SourceDeleteOutcome.SourceChanged);
        }

        string actualHash;
        try
        {
            actualHash = await hasher.ComputeSha256Async(source, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return new SourceDeleteResult(SourceDeleteOutcome.SourceMissing);
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(actualHash, expectedHash)
            || !File.Exists(source)
            || new FileInfo(source).Length != expectedSize)
        {
            return new SourceDeleteResult(SourceDeleteOutcome.SourceChanged);
        }

        File.Delete(source);
        return new SourceDeleteResult(SourceDeleteOutcome.Deleted);
    }

    public async Task<bool> VerifyCommittedAsync(
        LibraryFolders folders,
        CommittedFile committed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(committed);
        string target = ResolveTarget(folders, committed.RelativePath);
        string supplied = ControlledPathGuard.RequireDirectChild(
            folders.LibraryDirectory,
            committed.FullPath);
        if (!StringComparer.OrdinalIgnoreCase.Equals(target, supplied)
            || !File.Exists(target))
        {
            return false;
        }

        RejectReparsePoint(folders.LibraryDirectory, target, includeCandidate: true);
        if (new FileInfo(target).Length != committed.Size)
        {
            return false;
        }

        string hash = await hasher.ComputeSha256Async(target, cancellationToken);
        return StringComparer.OrdinalIgnoreCase.Equals(hash, committed.Sha256);
    }

    public Task CleanupTemporaryFilesAsync(
        Guid operationId,
        LibraryFolders folders,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(folders);
        string operationDirectory = ResolveOperationDirectory(folders, operationId);
        string temporary = ControlledPathGuard.RequireDirectChild(
            folders.LibraryDirectory,
            Path.Combine(
                folders.LibraryDirectory,
                $".tracefold-{operationId:N}.tmp"));
        if (Directory.Exists(operationDirectory))
        {
            RejectReparseTree(folders.WorkDirectory, operationDirectory);
            Directory.Delete(operationDirectory, recursive: true);
        }

        if (File.Exists(temporary))
        {
            RejectReparsePoint(
                folders.LibraryDirectory,
                temporary,
                includeCandidate: true);
            File.Delete(temporary);
        }

        return Task.CompletedTask;
    }

    private static string ResolveTarget(
        LibraryFolders folders,
        string targetFilename)
    {
        ArgumentNullException.ThrowIfNull(folders);
        string filename = RequireTargetFilename(targetFilename);
        return ControlledPathGuard.RequireDirectChild(
            folders.LibraryDirectory,
            Path.Combine(folders.LibraryDirectory, filename));
    }

    private static string ResolveOperationDirectory(
        LibraryFolders folders,
        Guid operationId) =>
        ControlledPathGuard.RequireDescendant(
            folders.WorkDirectory,
            Path.Combine(
                folders.WorkDirectory,
                ".tracefold",
                "ingest",
                operationId.ToString("N")));

    private static string RequireTargetFilename(string targetFilename)
    {
        if (string.IsNullOrWhiteSpace(targetFilename)
            || Path.IsPathRooted(targetFilename)
            || targetFilename is "." or ".."
            || targetFilename.Contains(':', StringComparison.Ordinal)
            || targetFilename.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0
            || targetFilename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !StringComparer.Ordinal.Equals(
                Path.GetFileName(targetFilename),
                targetFilename))
        {
            throw new ControlledPathException("The target filename is not safe.");
        }

        return targetFilename;
    }

    private static string RequireSha256(string value)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new StorageIntegrityException("The expected SHA-256 hash is invalid.");
        }

        return value.ToUpperInvariant();
    }

    private static async Task CopyAndFlushAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
        await using var output = new FileStream(
            destination,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    private async Task VerifyFileAsync(
        string path,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != expectedSize)
        {
            throw new StorageIntegrityException(
                "The file size does not match the persisted storage record.");
        }

        string hash = await hasher.ComputeSha256Async(path, cancellationToken);
        if (!StringComparer.OrdinalIgnoreCase.Equals(hash, expectedSha256))
        {
            throw new StorageIntegrityException(
                "The file hash does not match the persisted storage record.");
        }
    }

    private static void RejectReparsePoint(
        string root,
        string candidate,
        bool includeCandidate)
    {
        string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string canonicalCandidate = Path.GetFullPath(candidate);
        string stop = includeCandidate
            ? canonicalCandidate
            : Path.GetDirectoryName(canonicalCandidate) ?? canonicalRoot;
        string? current = stop;
        while (current is not null)
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                FileAttributes attributes = File.GetAttributes(current);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new ControlledPathException(
                        "A controlled storage path contains a reparse point.");
                }
            }

            if (StringComparer.OrdinalIgnoreCase.Equals(
                    Path.TrimEndingDirectorySeparator(current),
                    canonicalRoot))
            {
                return;
            }

            current = Path.GetDirectoryName(current);
        }

        throw new ControlledPathException(
            "The controlled storage path does not descend from its root.");
    }

    private static void RejectReparseTree(string root, string operationDirectory)
    {
        RejectReparsePoint(root, operationDirectory, includeCandidate: true);
        var pending = new Stack<string>();
        pending.Push(operationDirectory);
        while (pending.TryPop(out string? directory))
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(
                         directory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new ControlledPathException(
                        "A temporary operation directory contains a reparse point.");
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    pending.Push(path);
                }
            }
        }
    }
}
