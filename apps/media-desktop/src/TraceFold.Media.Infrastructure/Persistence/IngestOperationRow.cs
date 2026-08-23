using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Application.Ingest;

namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class IngestOperationRow
{
    public Guid Id { get; set; }

    public Guid InboxItemId { get; set; }

    public InboxItemRow InboxItem { get; set; } = null!;

    public Guid? ActiveInboxItemId { get; set; }

    public IngestOperationStage Stage { get; set; }

    public IngestDisposition Disposition { get; set; }

    public Guid? ConflictCaseId { get; set; }

    public required string SourcePath { get; set; }

    public long SourceSize { get; set; }

    public DateTimeOffset SourceLastWriteTimeUtc { get; set; }

    public required string SourceSha256 { get; set; }

    public required string TargetFilename { get; set; }

    public string? StagedPath { get; set; }

    public Guid? AssetId { get; set; }

    public Guid? ReplacementAssetId { get; set; }

    public long? ReplacementAssetMetadataVersion { get; set; }

    public string? ExpectedTargetRelativePath { get; set; }

    public long? ExpectedTargetSize { get; set; }

    public DateTimeOffset? ExpectedTargetLastWriteTimeUtc { get; set; }

    public string? ExpectedTargetSha256 { get; set; }

    public IngestBackupState BackupState { get; set; }

    public string? BackupRelativePath { get; set; }

    public long? BackupSize { get; set; }

    public string? BackupSha256 { get; set; }

    public IngestOperationStage? RecoveryBlockedFromStage { get; set; }

    public string? OldAssetSnapshotJson { get; set; }

    public int CleanupAttemptCount { get; set; }

    public DateTimeOffset? NextCleanupAttemptAtUtc { get; set; }

    public string? TargetTemporaryPath { get; set; }

    public required string MetadataSnapshotJson { get; set; }

    public string? ErrorCode { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<IngestCleanupEntryRow> CleanupEntries { get; } = [];
}
