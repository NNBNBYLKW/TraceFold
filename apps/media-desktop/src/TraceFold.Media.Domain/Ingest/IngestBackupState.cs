namespace TraceFold.Media.Domain.Ingest;

public enum IngestBackupState
{
    NotRequired,
    Pending,
    Created,
    Restored,
    Deleted
}
