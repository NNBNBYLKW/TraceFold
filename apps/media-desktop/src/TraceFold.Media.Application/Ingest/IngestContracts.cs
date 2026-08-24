using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Application.Ingest;

public enum IngestOutcome
{
    Completed,
    ConflictBlocked,
    NotReady,
    RequiresProcessing,
    SourceMissing,
    SourceChanged,
    CleanupPending,
    InProgress,
    ShuttingDown,
    Failed,
    Skipped,
    ConflictChanged
}

public sealed record IngestResult(
    IngestOutcome Outcome,
    Guid? OperationId,
    Guid? AssetId,
    Guid? ConflictCaseId,
    string Message);

public sealed record ConflictRevalidationResult(
    bool IsValid,
    string? ErrorCode,
    IReadOnlyList<ConflictMatchRecord> CurrentMatches)
{
    public static ConflictRevalidationResult Valid(
        IReadOnlyList<ConflictMatchRecord> matches) =>
        new(true, null, matches);

    public static ConflictRevalidationResult Invalid(string errorCode) =>
        new(false, errorCode, []);
}

public enum IngestDisposition
{
    Create,
    KeepBoth,
    ReplaceRegistered,
    ReplaceUnregistered
}

public sealed record IngestExecutionPlan
{
    private IngestExecutionPlan(
        Guid? conflictCaseId,
        IngestDisposition disposition,
        string requestedTargetFilename,
        Guid? replacementAssetId,
        long? replacementMetadataVersion,
        LibraryFileSnapshot? target)
    {
        ConflictCaseId = conflictCaseId;
        Disposition = disposition;
        RequestedTargetFilename = requestedTargetFilename;
        ReplacementAssetId = replacementAssetId;
        ReplacementMetadataVersion = replacementMetadataVersion;
        Target = target;
    }

    public Guid? ConflictCaseId { get; }

    public IngestDisposition Disposition { get; }

    public string RequestedTargetFilename { get; }

    public Guid? ReplacementAssetId { get; }

    public long? ReplacementMetadataVersion { get; }

    public LibraryFileSnapshot? Target { get; }

    public static IngestExecutionPlan Create(string requestedTargetFilename) =>
        new(
            null,
            IngestDisposition.Create,
            RequireFilename(requestedTargetFilename),
            null,
            null,
            null);

    public static IngestExecutionPlan KeepBoth(
        Guid conflictCaseId,
        string requestedTargetFilename) =>
        new(
            RequireId(conflictCaseId),
            IngestDisposition.KeepBoth,
            RequireFilename(requestedTargetFilename),
            null,
            null,
            null);

    public static IngestExecutionPlan ReplaceRegistered(
        Guid conflictCaseId,
        Guid replacementAssetId,
        long replacementMetadataVersion,
        LibraryFileSnapshot target)
    {
        if (replacementMetadataVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(replacementMetadataVersion));
        }

        LibraryFileSnapshot requiredTarget = RequireTarget(target);
        return new IngestExecutionPlan(
            RequireId(conflictCaseId),
            IngestDisposition.ReplaceRegistered,
            RequireFilename(requiredTarget.RelativePath),
            RequireId(replacementAssetId),
            replacementMetadataVersion,
            requiredTarget);
    }

    public static IngestExecutionPlan ReplaceUnregistered(
        Guid conflictCaseId,
        LibraryFileSnapshot target)
    {
        LibraryFileSnapshot requiredTarget = RequireTarget(target);
        return new IngestExecutionPlan(
            RequireId(conflictCaseId),
            IngestDisposition.ReplaceUnregistered,
            RequireFilename(requiredTarget.RelativePath),
            null,
            null,
            requiredTarget);
    }

    private static Guid RequireId(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("A persisted id is required.")
            : value;

    private static LibraryFileSnapshot RequireTarget(LibraryFileSnapshot? target) =>
        target ?? throw new ArgumentException("A replacement target is required.");

    private static string RequireFilename(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || Path.IsPathRooted(value)
            || !StringComparer.Ordinal.Equals(Path.GetFileName(value), value)
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("A safe target filename is required.");
        }

        return value;
    }
}

public interface IIngestExecutionService
{
    Task<IngestResult> ExecuteAsync(
        IngestPreparation preparation,
        IngestExecutionPlan plan,
        LibraryFolders folders,
        CancellationToken cancellationToken);

    Task<IngestResult> ResumeAsync(
        IngestOperationRecord operation,
        LibraryFolders folders,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "This ingest executor does not support persisted-operation recovery.");
}

public sealed record RecoveryResult(bool Succeeded, string Message);

public sealed record IngestSourceRecord(
    string FullPath,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    bool IsPresent,
    bool IsPrimary);

public sealed record IngestCandidate(
    Guid InboxItemId,
    InboxItemState State,
    MediaKind MediaKind,
    string ActualFilename,
    string? RequiredCapability,
    IReadOnlyList<IngestSourceRecord> Sources,
    MetadataSelection Metadata,
    long MetadataVersion);

public enum IngestCleanupKind
{
    InboxSource,
    ProcessingOutput,
    ReplacementBackup
}

public sealed record IngestCleanupFile(
    IngestCleanupKind Kind,
    string FullPath,
    long Size,
    string Sha256);

public sealed record PreparedIngestInput(
    IngestCleanupKind Kind,
    string FullPath,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256,
    IReadOnlyList<IngestCleanupFile> CleanupFiles);

public sealed record IngestCleanupEntryRecord(
    Guid Id,
    Guid OperationId,
    IngestCleanupFile File,
    bool Completed);

public sealed record CleanupResult(bool Completed, string? ErrorCode);

public sealed class IngestInputException(string errorCode)
    : InvalidOperationException(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed record IngestPreparation(
    Guid InboxItemId,
    MediaKind MediaKind,
    string SourcePath,
    long SourceSize,
    DateTimeOffset SourceLastWriteTimeUtc,
    string SourceSha256,
    string TargetFilename,
    string? DisplayName,
    IReadOnlyList<Guid> AuthorIds,
    Guid? SeriesId,
    IReadOnlyList<Guid> TagIds,
    long MetadataVersion,
    PreparedIngestInput Input)
{
    public IngestPreparation(
        Guid inboxItemId,
        MediaKind mediaKind,
        string sourcePath,
        long sourceSize,
        DateTimeOffset sourceLastWriteTimeUtc,
        string sourceSha256,
        string targetFilename,
        string? displayName,
        IReadOnlyList<Guid> authorIds,
        Guid? seriesId,
        IReadOnlyList<Guid> tagIds,
        long metadataVersion)
        : this(
            inboxItemId,
            mediaKind,
            sourcePath,
            sourceSize,
            sourceLastWriteTimeUtc,
            sourceSha256,
            targetFilename,
            displayName,
            authorIds,
            seriesId,
            tagIds,
            metadataVersion,
            new PreparedIngestInput(
                IngestCleanupKind.InboxSource,
                sourcePath,
                sourceSize,
                sourceLastWriteTimeUtc,
                sourceSha256,
                [new IngestCleanupFile(
                    IngestCleanupKind.InboxSource,
                    sourcePath,
                    sourceSize,
                    sourceSha256)]))
    {
    }

    public static IngestPreparation From(
        IngestCandidate candidate,
        PreparedIngestInput input) =>
        new(
            candidate.InboxItemId,
            candidate.MediaKind,
            input.FullPath,
            input.Size,
            input.LastWriteTimeUtc,
            input.Sha256,
            candidate.ActualFilename,
            candidate.Metadata.DisplayName,
            candidate.Metadata.AuthorIds,
            candidate.Metadata.SeriesId,
            candidate.Metadata.TagIds,
            candidate.MetadataVersion,
            input);
}

public sealed record IngestOperationRecord(
    Guid Id,
    Guid InboxItemId,
    IngestOperationStage Stage,
    IngestDisposition Disposition,
    Guid? ConflictCaseId,
    string SourcePath,
    long SourceSize,
    DateTimeOffset SourceLastWriteTimeUtc,
    string SourceSha256,
    string TargetFilename,
    string? StagedPath,
    Guid? AssetId,
    Guid? ReplacementAssetId,
    long? ReplacementAssetMetadataVersion,
    string? ExpectedTargetRelativePath,
    long? ExpectedTargetSize,
    DateTimeOffset? ExpectedTargetLastWriteTimeUtc,
    string? ExpectedTargetSha256,
    IngestBackupState BackupState,
    string? BackupRelativePath,
    long? BackupSize,
    string? BackupSha256,
    IngestOperationStage? RecoveryBlockedFromStage,
    string? ErrorCode,
    int CleanupAttemptCount,
    DateTimeOffset? NextCleanupAttemptAtUtc);

public interface IIngestStore
{
    Task<IngestCandidate?> LoadCandidateAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);

    Task<IngestOperationRecord?> FindActiveAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);

    Task<IngestOperationRecord> CreateAsync(
        IngestPreparation preparation,
        IngestExecutionPlan plan,
        CancellationToken cancellationToken);

    Task MarkStagedAsync(
        Guid operationId,
        StagedFile staged,
        CancellationToken cancellationToken);

    Task MarkBackupCreatedAsync(
        Guid operationId,
        ReplacementBackup backup,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "This ingest store does not support replacement backups.");

    Task MarkBackupRestoredAsync(
        Guid operationId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "This ingest store does not support replacement restore.");

    Task MarkRecoveryBlockedAsync(
        Guid operationId,
        string errorCode,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "This ingest store does not support blocked recovery.");

    Task<bool> ReplacementDatabaseMatchesExpectedAsync(
        Guid operationId,
        CancellationToken cancellationToken) =>
        Task.FromResult(false);

    Task<bool> OperationDatabaseHasNewVersionAsync(
        Guid operationId,
        CancellationToken cancellationToken) => Task.FromResult(false);

    Task ResumeRecoveryAsync(
        Guid operationId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "This ingest store does not support resuming blocked recovery.");

    Task AbandonForRetryAsync(
        Guid operationId,
        CancellationToken cancellationToken);

    Task MarkFileCommittedAsync(
        Guid operationId,
        CommittedFile committed,
        CancellationToken cancellationToken);

    Task<Guid> CommitAssetAsync(
        Guid operationId,
        CommittedFile committed,
        CancellationToken cancellationToken);

    Task MarkCleanupPendingAsync(
        Guid operationId,
        string errorCode,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        Guid operationId,
        CancellationToken cancellationToken);

    Task FailAsync(
        Guid operationId,
        string errorCode,
        bool isRetryable,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IngestOperationRecord>> LoadIncompleteAsync(
        CancellationToken cancellationToken);

    Task MarkCleanupEntryCompletedAsync(
        Guid operationId,
        Guid entryId,
        CancellationToken cancellationToken) => Task.CompletedTask;

    Task<IReadOnlyList<IngestCleanupEntryRecord>> LoadCleanupEntriesAsync(
        Guid operationId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IngestCleanupEntryRecord>>([]);
}

public interface IIngestService
{
    Task<IngestResult> IngestAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);
}

public interface IIngestInputResolver
{
    Task<PreparedIngestInput> ResolveAsync(
        IngestCandidate candidate,
        CancellationToken cancellationToken);
}

public interface IIngestCleanupService
{
    Task<CleanupResult> CleanupAsync(
        IngestOperationRecord operation,
        LibraryFolders folders,
        CancellationToken cancellationToken);
}

public interface IIngestRecoveryService
{
    Task ReconcileAsync(CancellationToken cancellationToken);

    Task<RecoveryResult> RetryAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);
}

public sealed class MediaOperationRejectedException : InvalidOperationException
{
    public MediaOperationRejectedException()
        : base("The media runtime is no longer accepting new operations.")
    {
    }
}

public interface IMediaOperationLease : IAsyncDisposable
{
    CancellationToken CancellationToken { get; }
}

public interface IMediaOperationCoordinator
{
    IMediaOperationLease Begin(CancellationToken cancellationToken);

    Task StopAcceptingAndDrainAsync(CancellationToken cancellationToken);
}
