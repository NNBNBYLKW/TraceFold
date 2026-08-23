namespace TraceFold.Media.Domain.Ingest;

public sealed class IngestOperation
{
    public IngestOperation(Guid id, Guid inboxItemId)
    {
        Id = id;
        InboxItemId = inboxItemId;
        Stage = IngestOperationStage.Created;
    }

    public Guid Id { get; }

    public Guid InboxItemId { get; }

    public IngestOperationStage Stage { get; private set; }

    public IngestOperationStage? BlockedFromStage { get; private set; }

    public void AdvanceTo(IngestOperationStage next)
    {
        bool allowed = (Stage, next) switch
        {
            (IngestOperationStage.Created, IngestOperationStage.Staged) => true,
            (IngestOperationStage.Staged, IngestOperationStage.FileCommitted) => true,
            (IngestOperationStage.FileCommitted, IngestOperationStage.DatabaseCommitted) => true,
            (IngestOperationStage.DatabaseCommitted, IngestOperationStage.CleanupPending) => true,
            (IngestOperationStage.DatabaseCommitted, IngestOperationStage.Completed) => true,
            (IngestOperationStage.CleanupPending, IngestOperationStage.Completed) => true,
            (_, IngestOperationStage.Failed)
                when Stage is not IngestOperationStage.Completed
                    and not IngestOperationStage.Failed
                    and not IngestOperationStage.RecoveryBlocked => true,
            _ => false
        };

        if (!allowed)
        {
            throw new InvalidOperationException(
                $"Cannot advance ingest operation from {Stage} to {next}.");
        }

        Stage = next;
    }

    public void BlockRecovery()
    {
        if (Stage is IngestOperationStage.Completed
            or IngestOperationStage.Failed
            or IngestOperationStage.RecoveryBlocked)
        {
            throw new InvalidOperationException(
                $"Cannot block recovery for an ingest operation in {Stage} state.");
        }

        BlockedFromStage = Stage;
        Stage = IngestOperationStage.RecoveryBlocked;
    }

    public void ResumeRecovery()
    {
        if (Stage != IngestOperationStage.RecoveryBlocked || BlockedFromStage is null)
        {
            throw new InvalidOperationException(
                "Only a recovery-blocked ingest operation can resume.");
        }

        Stage = BlockedFromStage.Value;
        BlockedFromStage = null;
    }
}
