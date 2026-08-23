using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Startup;

namespace TraceFold.Media.Infrastructure.Workers;

public sealed class ProcessingQueueWorker(
    IProcessingJobExecutor executor,
    IProcessingQueueSignal signal,
    IProcessingStore store,
    IMediaRuntimeGate runtimeGate,
    ILogger<ProcessingQueueWorker> logger) : BackgroundService
{
    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        if (!await executor.ExecuteNextAsync(cancellationToken))
        {
            await signal.WaitAsync(cancellationToken);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await runtimeGate.WaitUntilReadyAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "ProcessingQueueCycleFailed");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await store.InterruptRecoverableAsync(
            "processing.shutdown_interrupted",
            CancellationToken.None);
    }
}
