using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Application.Ingest;

public enum ConflictKind
{
    Name,
    RegisteredContent
}

public sealed record ConflictCheck(ConflictKind? Kind)
{
    public bool HasConflict => Kind.HasValue;
}

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
    Failed
}

public sealed record IngestResult(
    IngestOutcome Outcome,
    Guid? OperationId,
    Guid? AssetId,
    string Message);

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
    ProcessingOutput
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
    string SourcePath,
    long SourceSize,
    DateTimeOffset SourceLastWriteTimeUtc,
    string SourceSha256,
    string TargetFilename,
    string? StagedPath,
    Guid? AssetId,
    string? ErrorCode);

public interface IIngestStore
{
    Task<IngestCandidate?> LoadCandidateAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);

    Task<IngestOperationRecord?> FindActiveAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);

    Task<ConflictCheck> CheckConflictsAsync(
        string targetFilename,
        string sourceSha256,
        CancellationToken cancellationToken);

    Task BlockConflictAsync(
        Guid inboxItemId,
        ConflictKind kind,
        CancellationToken cancellationToken);

    Task<IngestOperationRecord> CreateAsync(
        IngestPreparation preparation,
        CancellationToken cancellationToken);

    Task MarkStagedAsync(
        Guid operationId,
        StagedFile staged,
        CancellationToken cancellationToken);

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
