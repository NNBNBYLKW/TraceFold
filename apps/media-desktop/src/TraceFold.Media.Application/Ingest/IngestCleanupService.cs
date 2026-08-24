using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;

namespace TraceFold.Media.Application.Ingest;

public sealed class IngestCleanupService(
    IIngestStore store,
    IControlledMediaStorage storage,
    ILogger<IngestCleanupService>? logger = null,
    IIngestFaultInjector? faultInjector = null) : IIngestCleanupService
{
    private readonly IIngestFaultInjector faultInjector =
        faultInjector ?? new NoOpIngestFaultInjector();

    public async Task<CleanupResult> CleanupAsync(
        IngestOperationRecord operation,
        LibraryFolders folders,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IngestCleanupEntryRecord> persisted =
            await store.LoadCleanupEntriesAsync(
                operation.Id,
                cancellationToken);
        IReadOnlyList<IngestCleanupEntryRecord> entries = persisted.Count == 0
            ? [LegacyEntry(operation)]
            : persisted;
        foreach (IngestCleanupEntryRecord entry in entries.Where(item => !item.Completed))
        {
            SourceDeleteResult result;
            try
            {
                result = await storage.DeleteCleanupFileIfMatchesAsync(
                    folders,
                    entry.File,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                logger?.LogError(
                    error,
                    "IngestCleanupEntryFailed OperationId={OperationId} EntryId={EntryId}",
                    operation.Id,
                    entry.Id);
                return await PendingAsync(
                    operation.Id,
                    "ingest.cleanup_failed",
                    cancellationToken);
            }

            if (result.Outcome == SourceDeleteOutcome.SourceChanged)
            {
                return await PendingAsync(
                    operation.Id,
                    "ingest.cleanup_source_changed",
                    cancellationToken);
            }

            if (entry.Id != Guid.Empty)
            {
                await store.MarkCleanupEntryCompletedAsync(
                    operation.Id,
                    entry.Id,
                    cancellationToken);
                await faultInjector.ReachAsync(
                    IngestFaultPoint.AfterCleanupEntry,
                    operation.Id,
                    cancellationToken);
            }
        }

        try
        {
            await storage.CleanupTemporaryFilesAsync(
                operation.Id,
                folders,
                cancellationToken);
            await store.CompleteAsync(operation.Id, cancellationToken);
            return new CleanupResult(true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            logger?.LogError(
                error,
                "IngestTemporaryCleanupFailed OperationId={OperationId}",
                operation.Id);
            return await PendingAsync(
                operation.Id,
                "ingest.cleanup_failed",
                cancellationToken);
        }
    }

    private async Task<CleanupResult> PendingAsync(
        Guid operationId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        await store.MarkCleanupPendingAsync(
            operationId,
            errorCode,
            cancellationToken);
        return new CleanupResult(false, errorCode);
    }

    private static IngestCleanupEntryRecord LegacyEntry(
        IngestOperationRecord operation) =>
        new(
            Guid.Empty,
            operation.Id,
            new IngestCleanupFile(
                IngestCleanupKind.InboxSource,
                operation.SourcePath,
                operation.SourceSize,
                operation.SourceSha256),
            false);
}
