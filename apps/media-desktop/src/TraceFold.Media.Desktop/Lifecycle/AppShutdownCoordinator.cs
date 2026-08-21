using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TraceFold.Media.Desktop.Lifecycle;

public interface IDesktopHostStopper
{
    Task StopAsync(CancellationToken cancellationToken);
}

public sealed class DesktopHostStopper(IHost host) : IDesktopHostStopper
{
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await host.StopAsync(cancellationToken);
        }
        finally
        {
            host.Dispose();
        }
    }
}

public sealed class AppShutdownCoordinator
{
    private readonly object _sync = new();
    private readonly IDesktopHostStopper _hostStopper;
    private readonly ILogger<AppShutdownCoordinator> _logger;
    private readonly TimeSpan _timeout;
    private Task? _stopTask;

    public AppShutdownCoordinator(
        IDesktopHostStopper hostStopper,
        ILogger<AppShutdownCoordinator> logger,
        TimeSpan? timeout = null)
    {
        _hostStopper = hostStopper;
        _logger = logger;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return _stopTask ??= StopCoreAsync(cancellationToken);
        }
    }

    private async Task StopCoreAsync(CancellationToken callerToken)
    {
        using var timeout = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            callerToken,
            timeout.Token);
        try
        {
            await _hostStopper.StopAsync(linked.Token);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested
            && !callerToken.IsCancellationRequested)
        {
            _logger.LogWarning("DesktopShutdownTimedOut");
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "DesktopShutdownFailed");
        }
    }
}
