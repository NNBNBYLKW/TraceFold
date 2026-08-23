namespace TraceFold.Media.Domain.Processing;

public enum ProcessingJobStatus
{
    Queued,
    Running,
    Succeeded,
    Cancelled,
    Interrupted,
    Failed
}
