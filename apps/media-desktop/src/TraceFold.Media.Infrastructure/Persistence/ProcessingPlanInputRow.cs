namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class ProcessingPlanInputRow
{
    public Guid Id { get; set; }

    public Guid PlanId { get; set; }

    public ProcessingPlanRow Plan { get; set; } = null!;

    public Guid SourceFileId { get; set; }

    public required string Role { get; set; }

    public required string FullPath { get; set; }

    public required string Filename { get; set; }

    public long Size { get; set; }

    public DateTimeOffset LastWriteTimeUtc { get; set; }

    public required string Sha256 { get; set; }
}
