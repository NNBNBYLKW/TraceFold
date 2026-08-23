using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Ingest;

namespace TraceFold.Media.Application.Storage;

public sealed record StagedFile(string FullPath, long Size, string Sha256);

public sealed record CommittedFile(
    string FullPath,
    string RelativePath,
    long Size,
    string Sha256);

public enum SourceDeleteOutcome
{
    Deleted,
    SourceMissing,
    SourceChanged
}

public sealed record SourceDeleteResult(SourceDeleteOutcome Outcome);

public sealed class StorageConflictException : IOException
{
    public StorageConflictException(string message)
        : base(message)
    {
    }

    public StorageConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class StorageIntegrityException : IOException
{
    public StorageIntegrityException(string message)
        : base(message)
    {
    }
}

public interface IFileHasher
{
    Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken);
}

public interface IControlledMediaStorage
{
    Task<bool> TargetExistsAsync(
        LibraryFolders folders,
        string targetFilename,
        CancellationToken cancellationToken);

    Task<StagedFile> StageAsync(
        Guid operationId,
        LibraryFolders folders,
        string sourcePath,
        string expectedSha256,
        CancellationToken cancellationToken);

    Task<StagedFile> StageAsync(
        Guid operationId,
        LibraryFolders folders,
        PreparedIngestInput input,
        CancellationToken cancellationToken) =>
        StageAsync(
            operationId,
            folders,
            input.FullPath,
            input.Sha256,
            cancellationToken);

    Task<CommittedFile> CommitAsync(
        Guid operationId,
        LibraryFolders folders,
        StagedFile staged,
        string targetFilename,
        CancellationToken cancellationToken);

    Task<SourceDeleteResult> DeleteSourceIfMatchesAsync(
        LibraryFolders folders,
        string sourcePath,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken);

    Task<SourceDeleteResult> DeleteCleanupFileIfMatchesAsync(
        LibraryFolders folders,
        IngestCleanupFile cleanupFile,
        CancellationToken cancellationToken) =>
        DeleteSourceIfMatchesAsync(
            folders,
            cleanupFile.FullPath,
            cleanupFile.Size,
            cleanupFile.Sha256,
            cancellationToken);

    Task<bool> VerifyCommittedAsync(
        LibraryFolders folders,
        CommittedFile committed,
        CancellationToken cancellationToken);

    Task CleanupTemporaryFilesAsync(
        Guid operationId,
        LibraryFolders folders,
        CancellationToken cancellationToken);
}
