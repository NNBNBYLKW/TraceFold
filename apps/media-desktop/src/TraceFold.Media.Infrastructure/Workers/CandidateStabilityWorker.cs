using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Startup;

namespace TraceFold.Media.Infrastructure.Workers;

public sealed class CandidateStabilityWorker(
    IInboxScanner scanner,
    IInboxScanStore store,
    IInboxActivityNotifier activity,
    TimeProvider timeProvider,
    ILogger<CandidateStabilityWorker> logger,
    IMediaRuntimeGate? runtimeGate = null) : BackgroundService
{
    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        if (!await store.HasWaitingCandidatesAsync(cancellationToken))
        {
            await activity.WaitForCandidateAsync(cancellationToken);
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, cancellationToken);
        await scanner.ScanAsync(ScanTrigger.StabilityCheck, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (runtimeGate is not null)
        {
            await runtimeGate.WaitUntilReadyAsync(stoppingToken);
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
                logger.LogError(error, "CandidateStabilityCycleFailed");
                await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, stoppingToken);
            }
        }
    }
}
