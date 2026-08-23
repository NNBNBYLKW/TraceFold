namespace TraceFold.Media.Domain.Inbox;

public enum InboxItemState
{
    WaitingForStability,
    Analyzing,
    ReadyToTag,
    RequiresProcessing,
    Ingesting,
    ConflictBlocked,
    CleanupPending,
    Completed,
    Failed,
    SourceMissing,
    ProcessingQueued,
    Processing,
    ProcessingInterrupted
}
