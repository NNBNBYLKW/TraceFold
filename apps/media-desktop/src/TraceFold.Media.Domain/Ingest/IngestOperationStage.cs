namespace TraceFold.Media.Domain.Ingest;

public enum IngestOperationStage
{
    Created,
    Staged,
    FileCommitted,
    DatabaseCommitted,
    CleanupPending,
    Completed,
    Failed,
    RecoveryBlocked
}
