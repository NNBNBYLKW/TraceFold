namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class SourceFileRow
{
    public Guid Id { get; set; }

    public Guid InboxItemId { get; set; }

    public InboxItemRow InboxItem { get; set; } = null!;

    public required string NormalizedPath { get; set; }

    public string? ActivePathKey { get; set; }

    public required string Filename { get; set; }

    public required string Extension { get; set; }

    public long Size { get; set; }

    public DateTimeOffset LastWriteTimeUtc { get; set; }

    public DateTimeOffset FirstObservedAtUtc { get; set; }

    public int StableObservationCount { get; set; }

    public string? Sha256 { get; set; }

    public bool IsPresent { get; set; }

    public bool IsPrimary { get; set; }
}
