namespace TraceFold.Media.Domain.Processing;

public sealed class ProcessingJob
{
    private ProcessingJob(Guid id, Guid inboxItemId, int attempt)
    {
        Id = id;
        InboxItemId = inboxItemId;
        Attempt = attempt;
        Status = ProcessingJobStatus.Queued;
    }

    public Guid Id { get; }

    public Guid InboxItemId { get; }

    public int Attempt { get; }

    public ProcessingJobStatus Status { get; private set; }

    public string? CurrentStepKey { get; private set; }

    public int ProgressPercent { get; private set; }

    public string? ErrorCode { get; private set; }

    public bool IsRetryable { get; private set; }

    public static ProcessingJob Queue(Guid id, Guid inboxItemId, int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        return new ProcessingJob(id, inboxItemId, attempt);
    }

    public void Start()
    {
        RequireStatus(ProcessingJobStatus.Queued);
        Status = ProcessingJobStatus.Running;
    }

    public void Report(string stepKey, int progressPercent)
    {
        RequireStatus(ProcessingJobStatus.Running);
        ArgumentException.ThrowIfNullOrWhiteSpace(stepKey);
        ArgumentOutOfRangeException.ThrowIfLessThan(progressPercent, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(progressPercent, 100);
        if (progressPercent < ProgressPercent)
        {
            throw new InvalidOperationException("Processing progress cannot decrease.");
        }

        CurrentStepKey = stepKey;
        ProgressPercent = progressPercent;
    }

    public void Succeed()
    {
        RequireStatus(ProcessingJobStatus.Running);
        Status = ProcessingJobStatus.Succeeded;
        ProgressPercent = 100;
        ErrorCode = null;
        IsRetryable = false;
    }

    public void Cancel()
    {
        RequireStatus(ProcessingJobStatus.Queued);
        Status = ProcessingJobStatus.Cancelled;
    }

    public void Interrupt(string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        RequireStatus(ProcessingJobStatus.Queued, ProcessingJobStatus.Running);
        Status = ProcessingJobStatus.Interrupted;
        ErrorCode = errorCode;
        IsRetryable = true;
    }

    public void Fail(string errorCode, bool isRetryable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        RequireStatus(ProcessingJobStatus.Running);
        Status = ProcessingJobStatus.Failed;
        ErrorCode = errorCode;
        IsRetryable = isRetryable;
    }

    private void RequireStatus(params ProcessingJobStatus[] allowedStatuses)
    {
        if (!allowedStatuses.Contains(Status))
        {
            throw new InvalidOperationException(
                $"Cannot transition processing job from {Status}.");
        }
    }
}
