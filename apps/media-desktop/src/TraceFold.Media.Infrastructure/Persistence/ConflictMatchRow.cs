using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class ConflictMatchRow
{
    public Guid Id { get; set; }

    public Guid ConflictCaseId { get; set; }

    public ConflictCaseRow ConflictCase { get; set; } = null!;

    public ConflictReason Reason { get; set; }

    public bool IsRegistered { get; set; }

    public Guid? AssetId { get; set; }

    public long? AssetMetadataVersion { get; set; }

    public required string RelativePath { get; set; }

    public required string ActualFilename { get; set; }

    public string? DisplayName { get; set; }

    public MediaKind MediaKind { get; set; }

    public long Size { get; set; }

    public DateTimeOffset LastWriteTimeUtc { get; set; }

    public required string Sha256 { get; set; }

    public required string MetadataSnapshotJson { get; set; }

    public required string TechnicalSnapshotJson { get; set; }
}
