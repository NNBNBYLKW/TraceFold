using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Startup;

namespace TraceFold.Media.Infrastructure.Workers;

public sealed class IngestCleanupWorker(
    IIngestCleanupWorkStore store,
    IIngestCleanupService cleanup,
    IDirectorySettingsStore settingsStore,
    IIngestCleanupSignal signal,
    IMediaRuntimeGate runtimeGate,
    TimeProvider timeProvider,
    ILogger<IngestCleanupWorker> logger) : BackgroundService
{
    public async Task RunDueAsync(CancellationToken cancellationToken)
    {
        LibraryFolders? folders = await settingsStore.LoadAsync(cancellationToken);
        if (folders is null)
        {
            return;
        }

        IReadOnlyList<IngestOperationRecord> due =
            await store.LoadDueCleanupAsync(
                timeProvider.GetUtcNow(),
                cancellationToken);
        foreach (IngestOperationRecord operation in due)
        {
            try
            {
                await cleanup.CleanupAsync(
                    operation,
                    folders,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                logger.LogError(
                    error,
                    "IngestCleanupWorkerFailed OperationId={OperationId}",
                    operation.Id);
            }
        }
    }

    public async Task WaitForWorkAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset? next = await store.FindNextCleanupDueAsync(
            cancellationToken);
        if (next is null)
        {
            await signal.WaitAsync(cancellationToken);
            return;
        }

        TimeSpan delay = next.Value - timeProvider.GetUtcNow();
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        using var waitCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task signalTask = signal.WaitAsync(waitCancellation.Token);
        Task delayTask = Task.Delay(
            delay,
            timeProvider,
            waitCancellation.Token);
        Task completed = await Task.WhenAny(signalTask, delayTask);
        await waitCancellation.CancelAsync();
        await completed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await runtimeGate.WaitUntilReadyAsync(stoppingToken);
            await store.ReactivateCleanupForStartupAsync(
                timeProvider.GetUtcNow(),
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDueAsync(stoppingToken);
                await WaitForWorkAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "IngestCleanupCycleFailed");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }
}
