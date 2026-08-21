namespace TraceFold.Media.Domain.Inbox;

[Flags]
public enum InboxAction
{
    None = 0,
    Preview = 1,
    EditMetadata = 2,
    Ingest = 4,
    Retry = 8,
    ClearMissing = 16,
    OpenFolder = 32,
    RetryCleanup = 64
}
