using TraceFold.Media.Domain.Processing;

namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class ProcessingJobRow
{
    public Guid Id { get; set; }

    public Guid PlanId { get; set; }

    public ProcessingPlanRow Plan { get; set; } = null!;

    public Guid InboxItemId { get; set; }

    public InboxItemRow InboxItem { get; set; } = null!;

    public Guid? ActiveInboxItemId { get; set; }

    public int Attempt { get; set; }

    public ProcessingJobStatus Status { get; set; }

    public string? CurrentStepKey { get; set; }

    public int ProgressPercent { get; set; }

    public string? ErrorCode { get; set; }

    public bool IsRetryable { get; set; }

    public required string CorrelationId { get; set; }

    public DateTimeOffset QueuedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ProcessedOutputRow? Output { get; set; }
}
