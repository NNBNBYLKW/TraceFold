using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Intake;

namespace TraceFold.Media.Infrastructure.Workers;

public sealed class CandidateStabilityWorker(
    IInboxScanner scanner,
    IInboxScanStore store,
    IInboxActivityNotifier activity,
    TimeProvider timeProvider,
    ILogger<CandidateStabilityWorker> logger) : BackgroundService
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
