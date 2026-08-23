namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class ProcessingPlanRow
{
    public Guid Id { get; set; }

    public Guid InboxItemId { get; set; }

    public required string ProcessorKey { get; set; }

    public required string ProcessorVersion { get; set; }

    public required string ProcessorDisplayName { get; set; }

    public required string Capability { get; set; }

    public required string OutputFilename { get; set; }

    public required string WorkDirectoryRelativePath { get; set; }

    public required string StepKeysJson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public ICollection<ProcessingPlanInputRow> Inputs { get; } = [];

    public ProcessingJobRow Job { get; set; } = null!;
}
