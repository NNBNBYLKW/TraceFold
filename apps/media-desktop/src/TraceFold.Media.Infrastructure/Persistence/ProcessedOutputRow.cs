namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class ProcessedOutputRow
{
    public Guid JobId { get; set; }

    public ProcessingJobRow Job { get; set; } = null!;

    public Guid InboxItemId { get; set; }

    public Guid? ActiveInboxItemId { get; set; }

    public required string RelativePath { get; set; }

    public long Size { get; set; }

    public DateTimeOffset LastWriteTimeUtc { get; set; }

    public required string Sha256 { get; set; }

    public long DurationTicks { get; set; }

    public bool HasVideo { get; set; }

    public bool HasAudio { get; set; }

    public bool IsValid { get; set; }
}
