using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;

namespace TraceFold.Media.Infrastructure.Workers;

public sealed class AutomaticInboxScanWorker(
    IInboxScanner scanner,
    IScanPreferencesService preferences,
    TimeProvider timeProvider,
    ILogger<AutomaticInboxScanWorker> logger) : BackgroundService
{
    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        ScanPreferences current = await preferences.LoadAsync(cancellationToken);
        if (current.AutoScanEnabled)
        {
            await scanner.ScanAsync(ScanTrigger.Automatic, cancellationToken);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), timeProvider, stoppingToken);
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "AutomaticInboxScanCycleFailed");
            }
        }
    }
}
