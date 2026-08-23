using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class ConflictCaseRow
{
    public Guid Id { get; set; }

    public Guid InboxItemId { get; set; }

    public InboxItemRow InboxItem { get; set; } = null!;

    public Guid? ActiveInboxItemId { get; set; }

    public ConflictCaseStatus Status { get; set; }

    public required string InputPath { get; set; }

    public long InputSize { get; set; }

    public DateTimeOffset InputLastWriteTimeUtc { get; set; }

    public required string InputSha256 { get; set; }

    public required string TargetFilename { get; set; }

    public long MetadataVersion { get; set; }

    public ConflictResolution? Resolution { get; set; }

    public Guid? SelectedMatchId { get; set; }

    public Guid? OperationId { get; set; }

    public string? InvalidationReason { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? ResolvedAtUtc { get; set; }

    public ICollection<ConflictMatchRow> Matches { get; } = [];
}
