using TraceFold.Media.Application.Intake;
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
    long MetadataVersion);

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
}

public interface IOrdinaryIngestService
{
    Task<IngestResult> IngestAsync(
        Guid inboxItemId,
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
