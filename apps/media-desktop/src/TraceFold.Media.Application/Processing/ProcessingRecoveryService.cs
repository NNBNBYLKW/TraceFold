using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;

namespace TraceFold.Media.Application.Processing;

public sealed class ProcessingRecoveryService(
    IProcessingStore store,
    IProcessingWorkspace workspace,
    IDirectorySettingsStore settingsStore,
    IInboxActivityNotifier activity,
    ILogger<ProcessingRecoveryService>? logger = null) : IProcessingRecoveryService
{
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProcessingJobRecord> interrupted =
            await store.InterruptRecoverableAsync(
                "processing.startup_interrupted",
                cancellationToken);
        if (interrupted.Count == 0)
        {
            return;
        }

        LibraryFolders? folders = await settingsStore.LoadAsync(cancellationToken);
        if (folders is not null)
        {
            foreach (ProcessingJobRecord job in interrupted)
            {
                await CleanupAsync(job.Id, folders);
            }
        }

        activity.Publish(InboxActivityKind.ItemChanged);
    }

    private async Task CleanupAsync(Guid jobId, LibraryFolders folders)
    {
        try
        {
            await workspace.CleanupUnverifiedAsync(
                jobId,
                folders,
                CancellationToken.None);
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            logger?.LogWarning(
                error,
                "ProcessingRecoveryCleanupFailed JobId={JobId}",
                jobId);
        }
    }
}
