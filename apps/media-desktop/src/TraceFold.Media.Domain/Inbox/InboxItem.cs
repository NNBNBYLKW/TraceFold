namespace TraceFold.Media.Domain.Inbox;

public sealed class InboxItem
{
    private InboxItem(Guid id)
    {
        Id = id;
        State = InboxItemState.WaitingForStability;
    }

    public Guid Id { get; }

    public InboxItemState State { get; private set; }

    public string? RequiredCapability { get; private set; }

    public string? ErrorCode { get; private set; }

    public bool IsRetryable { get; private set; }

    public InboxAction AllowedActions => State switch
    {
        InboxItemState.WaitingForStability => InboxAction.Preview,
        InboxItemState.ReadyToTag =>
            InboxAction.Preview | InboxAction.EditMetadata |
            InboxAction.Ingest | InboxAction.OpenFolder,
        InboxItemState.RequiresProcessing =>
            InboxAction.Preview | InboxAction.EditMetadata | InboxAction.OpenFolder,
        InboxItemState.ConflictBlocked =>
            InboxAction.Preview | InboxAction.EditMetadata | InboxAction.OpenFolder,
        InboxItemState.CleanupPending =>
            InboxAction.Preview | InboxAction.RetryCleanup | InboxAction.OpenFolder,
        InboxItemState.Failed when IsRetryable =>
            InboxAction.Preview | InboxAction.Retry | InboxAction.OpenFolder,
        InboxItemState.SourceMissing => InboxAction.ClearMissing,
        _ => InboxAction.None
    };

    public static InboxItem Discover(Guid id) => new(id);

    public void MarkAnalyzing()
    {
        TransitionTo(InboxItemState.Analyzing, InboxItemState.WaitingForStability);
        ClearStatusDetails();
    }

    public void MarkReady()
    {
        TransitionTo(InboxItemState.ReadyToTag, InboxItemState.Analyzing);
        ClearStatusDetails();
    }

    public void MarkRequiresProcessing(string requiredCapability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredCapability);
        TransitionTo(InboxItemState.RequiresProcessing, InboxItemState.Analyzing);
        ClearError();
        RequiredCapability = requiredCapability;
    }

    public void BeginIngest()
    {
        TransitionTo(InboxItemState.Ingesting, InboxItemState.ReadyToTag);
        ClearStatusDetails();
    }

    public void BlockConflict(string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        TransitionTo(
            InboxItemState.ConflictBlocked,
            InboxItemState.ReadyToTag,
            InboxItemState.Ingesting);
        RequiredCapability = null;
        ErrorCode = errorCode;
        IsRetryable = false;
    }

    public void MarkCleanupPending()
    {
        TransitionTo(InboxItemState.CleanupPending, InboxItemState.Ingesting);
        RequiredCapability = null;
        ErrorCode = null;
        IsRetryable = false;
    }

    public void Complete()
    {
        TransitionTo(
            InboxItemState.Completed,
            InboxItemState.Ingesting,
            InboxItemState.CleanupPending);
        ClearStatusDetails();
    }

    public void Fail(string errorCode, bool isRetryable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        TransitionTo(
            InboxItemState.Failed,
            InboxItemState.WaitingForStability,
            InboxItemState.Analyzing,
            InboxItemState.ReadyToTag,
            InboxItemState.RequiresProcessing,
            InboxItemState.Ingesting,
            InboxItemState.ConflictBlocked,
            InboxItemState.CleanupPending,
            InboxItemState.SourceMissing);
        RequiredCapability = null;
        ErrorCode = errorCode;
        IsRetryable = isRetryable;
    }

    public void MarkSourceMissing()
    {
        TransitionTo(
            InboxItemState.SourceMissing,
            InboxItemState.WaitingForStability,
            InboxItemState.Analyzing,
            InboxItemState.ReadyToTag,
            InboxItemState.RequiresProcessing,
            InboxItemState.ConflictBlocked,
            InboxItemState.Failed);
        ClearStatusDetails();
    }

    public void ResetForSourceChange()
    {
        TransitionTo(
            InboxItemState.WaitingForStability,
            InboxItemState.WaitingForStability,
            InboxItemState.Analyzing,
            InboxItemState.ReadyToTag,
            InboxItemState.RequiresProcessing,
            InboxItemState.ConflictBlocked,
            InboxItemState.Failed,
            InboxItemState.SourceMissing);
        ClearStatusDetails();
    }

    private void TransitionTo(InboxItemState next, params InboxItemState[] allowedCurrentStates)
    {
        if (!allowedCurrentStates.Contains(State))
        {
            throw new InvalidOperationException(
                $"Cannot transition inbox item from {State} to {next}.");
        }

        State = next;
    }

    private void ClearStatusDetails()
    {
        RequiredCapability = null;
        ClearError();
    }

    private void ClearError()
    {
        ErrorCode = null;
        IsRetryable = false;
    }
}
