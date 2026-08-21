using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class IngestOperationRow
{
    public Guid Id { get; set; }

    public Guid InboxItemId { get; set; }

    public InboxItemRow InboxItem { get; set; } = null!;

    public Guid? ActiveInboxItemId { get; set; }

    public IngestOperationStage Stage { get; set; }

    public required string SourcePath { get; set; }

    public long SourceSize { get; set; }

    public DateTimeOffset SourceLastWriteTimeUtc { get; set; }

    public required string SourceSha256 { get; set; }

    public required string TargetFilename { get; set; }

    public string? StagedPath { get; set; }

    public string? TargetTemporaryPath { get; set; }

    public required string MetadataSnapshotJson { get; set; }

    public string? ErrorCode { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
