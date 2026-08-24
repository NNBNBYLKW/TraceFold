namespace TraceFold.Media.Application.Ingest;

public enum IngestFaultPoint
{
    AfterOperationCreated,
    AfterStaged,
    AfterBackupCreated,
    AfterFileCommitted,
    BeforeDatabaseCommit,
    AfterDatabaseCommit,
    BeforeCleanup,
    AfterCleanupEntry,
    AfterCleanup
}

public interface IIngestFaultInjector
{
    Task ReachAsync(
        IngestFaultPoint point,
        Guid operationId,
        CancellationToken cancellationToken);
}

public sealed class NoOpIngestFaultInjector : IIngestFaultInjector
{
    public Task ReachAsync(
        IngestFaultPoint point,
        Guid operationId,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
