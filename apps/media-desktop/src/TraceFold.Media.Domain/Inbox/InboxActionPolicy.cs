namespace TraceFold.Media.Domain.Inbox;

public static class InboxActionPolicy
{
    public static InboxAction For(
        InboxItemState state,
        bool isRetryable,
        string? requiredCapability) => state switch
    {
        InboxItemState.WaitingForStability => InboxAction.Preview,
        InboxItemState.ReadyToTag => InboxAction.Preview | InboxAction.EditMetadata
            | InboxAction.Ingest | InboxAction.OpenFolder,
        InboxItemState.RequiresProcessing => InboxAction.Preview
            | InboxAction.EditMetadata | InboxAction.OpenFolder
            | InboxAction.StartProcessing,
        InboxItemState.ProcessingQueued => InboxAction.Preview
            | InboxAction.EditMetadata | InboxAction.OpenFolder
            | InboxAction.CancelProcessing,
        InboxItemState.Processing => InboxAction.Preview
            | InboxAction.EditMetadata | InboxAction.OpenFolder
            | InboxAction.StopProcessing,
        InboxItemState.ProcessingInterrupted => InboxAction.Preview
            | InboxAction.EditMetadata | InboxAction.OpenFolder
            | InboxAction.RetryProcessing,
        InboxItemState.ConflictBlocked => InboxAction.Preview
            | InboxAction.EditMetadata | InboxAction.OpenFolder
            | InboxAction.ResolveConflict,
        InboxItemState.RecoveryBlocked => InboxAction.Preview
            | InboxAction.OpenFolder | InboxAction.RecheckRecovery,
        InboxItemState.CleanupPending => InboxAction.Preview
            | InboxAction.RetryCleanup | InboxAction.OpenFolder,
        InboxItemState.Failed when isRetryable
            && !string.IsNullOrWhiteSpace(requiredCapability) => InboxAction.Preview
            | InboxAction.EditMetadata | InboxAction.OpenFolder
            | InboxAction.RetryProcessing,
        InboxItemState.Failed when isRetryable => InboxAction.Preview
            | InboxAction.Retry | InboxAction.OpenFolder,
        InboxItemState.SourceMissing => InboxAction.ClearMissing,
        _ => InboxAction.None
    };
}
