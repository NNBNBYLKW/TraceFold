using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Application.Ingest;

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
        ControlledReparseGuard.RejectPath(
            folders.LibraryDirectory,
            target,
            includeCandidate: false);
        return Task.FromResult(File.Exists(target));
    }

    public async Task<StagedFile> StageAsync(
        Guid operationId,
        LibraryFolders folders,
        string sourcePath,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        var source = new FileInfo(sourcePath);
        var input = new PreparedIngestInput(
            IngestCleanupKind.InboxSource,
            sourcePath,
            source.Exists ? source.Length : 0,
            source.Exists ? source.LastWriteTimeUtc : DateTimeOffset.MinValue,
            expectedSha256,
            []);
        return await StageAsync(
            operationId,
            folders,
            input,
            cancellationToken);
    }

    public async Task<StagedFile> StageAsync(
        Guid operationId,
        LibraryFolders folders,
        PreparedIngestInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(input);
        string expectedHash = RequireSha256(input.Sha256);
        string source = ResolvePreparedInput(folders, input);
        ControlledReparseGuard.RejectPath(
            InputRoot(folders, input.Kind),
            source,
            includeCandidate: true);
        var sourceInfo = new FileInfo(source);
        if (!sourceInfo.Exists || sourceInfo.Length != input.Size)
        {
            throw new StorageIntegrityException(
                "The prepared input no longer matches its expected size.");
        }

        string operationDirectory = ResolveOperationDirectory(folders, operationId);
        ControlledReparseGuard.RejectPath(
            folders.WorkDirectory,
            operationDirectory,
            includeCandidate: false);
        Directory.CreateDirectory(operationDirectory);
        ControlledReparseGuard.RejectPath(
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
            if (size != input.Size)
            {
                throw new StorageIntegrityException(
                    "The staged payload does not match the expected size.");
            }

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

        ControlledReparseGuard.RejectPath(
            folders.WorkDirectory,
            stagedPath,
            includeCandidate: true);
        string target = ResolveTarget(folders, targetFilename);
        string temporary = ControlledPathGuard.RequireDirectChild(
            folders.LibraryDirectory,
            Path.Combine(
                folders.LibraryDirectory,
                $".tracefold-{operationId:N}.tmp"));
        ControlledReparseGuard.RejectPath(
            folders.LibraryDirectory,
            target,
            includeCandidate: false);
        ControlledReparseGuard.RejectPath(
            folders.LibraryDirectory,
            temporary,
            includeCandidate: false);
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

        ControlledReparseGuard.RejectPath(
            folders.InboxDirectory,
            source,
            includeCandidate: true);
        if (new FileInfo(source).Length != expectedSize)
        {
            return new SourceDeleteResult(SourceDeleteOutcome.SourceChanged);
        }

        string quarantine = ControlledPathGuard.RequireDirectChild(
            folders.InboxDirectory,
            Path.Combine(
                folders.InboxDirectory,
                $".tracefold-delete-{Guid.NewGuid():N}.tmp"));
        ControlledReparseGuard.RejectPath(
            folders.InboxDirectory,
            quarantine,
            includeCandidate: false);
        try
        {
            File.Move(source, quarantine, overwrite: false);
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
            ControlledReparseGuard.RejectPath(
                folders.InboxDirectory,
                quarantine,
                includeCandidate: true);
            if (new FileInfo(quarantine).Length != expectedSize)
            {
                return new SourceDeleteResult(SourceDeleteOutcome.SourceChanged);
            }

            string actualHash = await hasher.ComputeSha256Async(
                quarantine,
                cancellationToken);
            if (!StringComparer.OrdinalIgnoreCase.Equals(actualHash, expectedHash)
                || !File.Exists(quarantine)
                || new FileInfo(quarantine).Length != expectedSize)
            {
                return new SourceDeleteResult(SourceDeleteOutcome.SourceChanged);
            }

            File.Delete(quarantine);
            return new SourceDeleteResult(SourceDeleteOutcome.Deleted);
        }
        finally
        {
            if (File.Exists(quarantine) && !File.Exists(source))
            {
                try
                {
                    File.Move(quarantine, source, overwrite: false);
                }
                catch (IOException) when (File.Exists(source))
                {
                    // A new inbox file won the name while the verified original
                    // was quarantined. Keep the .tmp recovery copy rather than
                    // deleting either file.
                }
            }
        }
    }

    public Task<SourceDeleteResult> DeleteCleanupFileIfMatchesAsync(
        LibraryFolders folders,
        IngestCleanupFile cleanupFile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cleanupFile);
        return cleanupFile.Kind switch
        {
            IngestCleanupKind.InboxSource => DeleteSourceIfMatchesAsync(
                folders,
                cleanupFile.FullPath,
                cleanupFile.Size,
                cleanupFile.Sha256,
                cancellationToken),
            IngestCleanupKind.ProcessingOutput =>
                DeleteProcessingOutputIfMatchesAsync(
                    folders,
                    cleanupFile,
                    cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(cleanupFile))
        };
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

        ControlledReparseGuard.RejectPath(
            folders.LibraryDirectory,
            target,
            includeCandidate: true);
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
            ControlledReparseGuard.RejectTree(
                folders.WorkDirectory,
                operationDirectory);
            Directory.Delete(operationDirectory, recursive: true);
        }

        if (File.Exists(temporary))
        {
            ControlledReparseGuard.RejectPath(
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

    private async Task<SourceDeleteResult> DeleteProcessingOutputIfMatchesAsync(
        LibraryFolders folders,
        IngestCleanupFile cleanupFile,
        CancellationToken cancellationToken)
    {
        string expectedHash = RequireSha256(cleanupFile.Sha256);
        string processingRoot = ProcessingRoot(folders);
        string source = RequireProcessingOutput(
            folders,
            processingRoot,
            cleanupFile.FullPath);
        if (!File.Exists(source))
        {
            return new SourceDeleteResult(SourceDeleteOutcome.SourceMissing);
        }

        ControlledReparseGuard.RejectPath(
            folders.WorkDirectory,
            source,
            includeCandidate: true);
        if (new FileInfo(source).Length != cleanupFile.Size)
        {
            return new SourceDeleteResult(SourceDeleteOutcome.SourceChanged);
        }

        string jobDirectory = Path.GetDirectoryName(source)!;
        string quarantine = ControlledPathGuard.RequireDescendant(
            processingRoot,
            Path.Combine(
                jobDirectory,
                $".tracefold-delete-{Guid.NewGuid():N}.tmp"));
        ControlledReparseGuard.RejectPath(
            folders.WorkDirectory,
            quarantine,
            includeCandidate: false);
        try
        {
            File.Move(source, quarantine, overwrite: false);
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
            ControlledReparseGuard.RejectPath(
                folders.WorkDirectory,
                quarantine,
                includeCandidate: true);
            if (new FileInfo(quarantine).Length != cleanupFile.Size)
            {
                return new SourceDeleteResult(SourceDeleteOutcome.SourceChanged);
            }

            string actualHash = await hasher.ComputeSha256Async(
                quarantine,
                cancellationToken);
            if (!StringComparer.OrdinalIgnoreCase.Equals(actualHash, expectedHash)
                || !File.Exists(quarantine)
                || new FileInfo(quarantine).Length != cleanupFile.Size)
            {
                return new SourceDeleteResult(SourceDeleteOutcome.SourceChanged);
            }

            File.Delete(quarantine);
            TryDeleteEmptyJobDirectory(
                folders,
                processingRoot,
                jobDirectory);
            return new SourceDeleteResult(SourceDeleteOutcome.Deleted);
        }
        finally
        {
            if (File.Exists(quarantine) && !File.Exists(source))
            {
                try
                {
                    File.Move(quarantine, source, overwrite: false);
                }
                catch (IOException) when (File.Exists(source))
                {
                    // Preserve both the verified recovery copy and a new winner.
                }
            }
        }
    }

    private static string ResolvePreparedInput(
        LibraryFolders folders,
        PreparedIngestInput input) => input.Kind switch
    {
        IngestCleanupKind.InboxSource => ControlledPathGuard.RequireDirectChild(
            folders.InboxDirectory,
            input.FullPath),
        IngestCleanupKind.ProcessingOutput => RequireProcessingOutput(
            folders,
            ProcessingRoot(folders),
            input.FullPath),
        _ => throw new ArgumentOutOfRangeException(nameof(input))
    };

    private static string InputRoot(
        LibraryFolders folders,
        IngestCleanupKind kind) => kind switch
    {
        IngestCleanupKind.InboxSource => folders.InboxDirectory,
        IngestCleanupKind.ProcessingOutput => ProcessingRoot(folders),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string RequireProcessingOutput(
        LibraryFolders folders,
        string processingRoot,
        string candidate)
    {
        string output = ControlledPathGuard.RequireDescendant(
            processingRoot,
            candidate);
        string? jobDirectory = Path.GetDirectoryName(output);
        if (jobDirectory is null
            || !StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetDirectoryName(jobDirectory),
                processingRoot)
            || !Guid.TryParseExact(
                Path.GetFileName(jobDirectory),
                "N",
                out _))
        {
            throw new ControlledPathException(
                "A processing output must belong to one controlled job directory.");
        }

        ControlledReparseGuard.RejectPath(
            folders.WorkDirectory,
            output,
            includeCandidate: File.Exists(output));
        return output;
    }

    private static string ProcessingRoot(LibraryFolders folders) =>
        ControlledPathGuard.RequireDescendant(
            folders.WorkDirectory,
            Path.Combine(
                folders.WorkDirectory,
                ".tracefold",
                "processing"));

    private static void TryDeleteEmptyJobDirectory(
        LibraryFolders folders,
        string processingRoot,
        string jobDirectory)
    {
        if (!Directory.Exists(jobDirectory)
            || !StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetDirectoryName(jobDirectory),
                processingRoot)
            || !Guid.TryParseExact(Path.GetFileName(jobDirectory), "N", out _)
            || Directory.EnumerateFileSystemEntries(jobDirectory).Any())
        {
            return;
        }

        ControlledReparseGuard.RejectPath(
            folders.WorkDirectory,
            jobDirectory,
            includeCandidate: true);
        Directory.Delete(jobDirectory, recursive: false);
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

}
