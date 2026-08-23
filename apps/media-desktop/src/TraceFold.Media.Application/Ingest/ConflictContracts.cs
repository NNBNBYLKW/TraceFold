using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Application.Ingest;

public sealed record ConflictInputSnapshot(
    string FullPath,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256,
    string TargetFilename,
    long MetadataVersion);

public sealed record ConflictMetadataSnapshot(
    string? DisplayName,
    IReadOnlyList<string> Authors,
    string? Series,
    IReadOnlyList<string> Tags);

public sealed record ConflictTechnicalMetadata(
    string Format,
    int? PixelWidth,
    int? PixelHeight,
    TimeSpan? Duration);

public sealed record RegisteredConflictCandidate(
    Guid AssetId,
    long AssetMetadataVersion,
    string RelativePath,
    string ActualFilename,
    string? DisplayName,
    MediaKind MediaKind,
    long StoredSize,
    string StoredSha256,
    ConflictReason DatabaseReason,
    ConflictMetadataSnapshot Metadata);

public sealed record ConflictMatchRecord(
    Guid Id,
    ConflictReason Reason,
    bool IsRegistered,
    Guid? AssetId,
    long? AssetMetadataVersion,
    string RelativePath,
    string ActualFilename,
    string? DisplayName,
    MediaKind MediaKind,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256,
    ConflictMetadataSnapshot Metadata,
    ConflictTechnicalMetadata Technical);

public sealed record ConflictCaseRecord(
    Guid Id,
    Guid InboxItemId,
    ConflictCaseStatus Status,
    ConflictInputSnapshot Input,
    ConflictResolution? Resolution,
    Guid? SelectedMatchId,
    string? InvalidationReason,
    IReadOnlyList<ConflictMatchRecord> Matches);

public sealed record ConflictMediaDto(
    Guid Id,
    bool IsIncoming,
    bool IsRegistered,
    Guid? AssetId,
    string ActualFilename,
    string DisplayName,
    MediaKind MediaKind,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256,
    ConflictReason Reason,
    ConflictMetadataSnapshot Metadata,
    ConflictTechnicalMetadata Technical,
    PreviewDescriptor? Preview);

public sealed record ConflictCaseDto(
    Guid Id,
    Guid InboxItemId,
    ConflictMediaDto Incoming,
    IReadOnlyList<ConflictMediaDto> Matches,
    ConflictResolution? PersistedResolution,
    Guid? PersistedSelectedMatchId,
    Guid? DefaultReplaceMatchId,
    bool CanReplace);

public interface IConflictCaseStore
{
    Task<IReadOnlyList<RegisteredConflictCandidate>> FindRegisteredCandidatesAsync(
        string targetFilename,
        string sha256,
        CancellationToken cancellationToken);

    Task<ConflictCaseRecord> ReplaceOpenAsync(
        Guid inboxItemId,
        ConflictInputSnapshot input,
        IReadOnlyList<ConflictMatchRecord> matches,
        CancellationToken cancellationToken);

    Task<ConflictCaseRecord?> LoadAsync(
        Guid conflictCaseId,
        CancellationToken cancellationToken);

    Task<ConflictCaseRecord?> LoadOpenForInboxAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);

    Task ChooseAsync(
        Guid conflictCaseId,
        ConflictResolution resolution,
        Guid? matchId,
        CancellationToken cancellationToken);

    Task SkipAsync(
        Guid conflictCaseId,
        CancellationToken cancellationToken);

    Task InvalidateAsync(
        Guid conflictCaseId,
        string reason,
        CancellationToken cancellationToken);
}

public interface IConflictDetectionService
{
    Task<ConflictCaseRecord?> DetectAsync(
        IngestPreparation preparation,
        LibraryFolders folders,
        CancellationToken cancellationToken);

    Task<ConflictRevalidationResult> RevalidateAsync(
        ConflictCaseRecord conflict,
        IngestPreparation current,
        LibraryFolders folders,
        CancellationToken cancellationToken);
}

public interface IConflictQueryService
{
    Task<ConflictCaseDto?> GetOpenAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);
}

public interface IConflictResolutionService
{
    Task<IngestResult> ResolveAsync(
        Guid conflictCaseId,
        ConflictResolution resolution,
        Guid? selectedMatchId,
        CancellationToken cancellationToken);
}
