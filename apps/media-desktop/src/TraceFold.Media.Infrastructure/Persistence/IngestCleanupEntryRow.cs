namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class IngestCleanupEntryRow
{
    public Guid Id { get; set; }

    public Guid OperationId { get; set; }

    public IngestOperationRow Operation { get; set; } = null!;

    public int Ordinal { get; set; }

    public required string CleanupKind { get; set; }

    public required string ControlledPath { get; set; }

    public long Size { get; set; }

    public required string Sha256 { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public DateTimeOffset? LastAttemptAtUtc { get; set; }

    public string? LastErrorCode { get; set; }
}
