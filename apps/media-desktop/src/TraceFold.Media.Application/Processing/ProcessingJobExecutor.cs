using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;

namespace TraceFold.Media.Application.Processing;

public sealed class ProcessingJobExecutor(
    IProcessingStore store,
    IMediaProcessorRegistry registry,
    IProcessingExecutionController controller,
    ProcessingProgressBuffer progressBuffer,
    IProcessingWorkspace workspace,
    IDirectorySettingsStore settingsStore,
    IInboxActivityNotifier activity,
    ILogger<ProcessingJobExecutor>? logger = null) : IProcessingJobExecutor
{
    public async Task<bool> ExecuteNextAsync(CancellationToken cancellationToken)
    {
        ProcessingJobRecord? job = await store.TryClaimNextAsync(cancellationToken);
        if (job is null)
        {
            return false;
        }

        LibraryFolders? folders = null;
        using var jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        using IDisposable registration = controller.Register(job.Id, jobCancellation);
        try
        {
            IMediaProcessor processor = registry.Resolve(job.Plan.Capability);
            if (!StringComparer.Ordinal.Equals(
                    processor.Descriptor.Key,
                    job.Plan.ProcessorKey)
                || !StringComparer.Ordinal.Equals(
                    processor.Descriptor.Version,
                    job.Plan.ProcessorVersion))
            {
                throw new ProcessingException(
                    "processing.processor_version_unavailable",
                    isRetryable: false);
            }

            folders = await settingsStore.LoadAsync(jobCancellation.Token)
                ?? throw new ProcessingException(
                    "processing.folders_unavailable",
                    isRetryable: true);
            var progress = new InlineProgress(
                update => progressBuffer.Publish(job.Id, update));
            ProcessedOutputDraft output = await processor.ExecuteAsync(
                new ProcessingExecutionContext(job, folders),
                progress,
                jobCancellation.Token);
            await progressBuffer.FlushAsync(job.Id, CancellationToken.None);
            await store.CompleteAsync(job.Id, output, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            await CleanupAsync(job.Id, folders);
            await store.InterruptAsync(
                job.Id,
                "processing.interrupted",
                CancellationToken.None);
        }
        catch (ProcessingException error)
        {
            await CleanupAsync(job.Id, folders);
            await store.FailAsync(
                job.Id,
                error.ErrorCode,
                error.IsRetryable,
                CancellationToken.None);
        }
        catch (Exception error)
        {
            logger?.LogError(
                error,
                "ProcessingJobFailed JobId={JobId} CorrelationId={CorrelationId}",
                job.Id,
                job.CorrelationId);
            await CleanupAsync(job.Id, folders);
            await store.FailAsync(
                job.Id,
                "processing.internal_error",
                isRetryable: true,
                CancellationToken.None);
        }
        finally
        {
            activity.Publish(InboxActivityKind.ItemChanged);
        }

        return true;
    }

    private async Task CleanupAsync(Guid jobId, LibraryFolders? folders)
    {
        if (folders is null)
        {
            return;
        }

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
                "ProcessingWorkspaceCleanupFailed JobId={JobId}",
                jobId);
        }
    }

    private sealed class InlineProgress(
        Action<ProcessingProgressUpdate> publish)
        : IProgress<ProcessingProgressUpdate>
    {
        public void Report(ProcessingProgressUpdate value) => publish(value);
    }
}
