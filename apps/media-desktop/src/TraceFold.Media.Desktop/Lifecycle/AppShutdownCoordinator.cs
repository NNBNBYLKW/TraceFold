using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Desktop.Services;

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
    private readonly IMetadataEditorRegistry _editorRegistry;
    private readonly IMediaOperationCoordinator _mediaOperations;
    private readonly IDesktopHostStopper _hostStopper;
    private readonly ILogger<AppShutdownCoordinator> _logger;
    private readonly TimeSpan _timeout;
    private Task? _stopTask;

    public AppShutdownCoordinator(
        IMetadataEditorRegistry editorRegistry,
        IMediaOperationCoordinator mediaOperations,
        IDesktopHostStopper hostStopper,
        ILogger<AppShutdownCoordinator> logger,
        TimeSpan? timeout = null)
    {
        _editorRegistry = editorRegistry;
        _mediaOperations = mediaOperations;
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
            await _editorRegistry.FlushAllAsync(linked.Token);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "MetadataFlushDuringShutdownFailed");
        }

        try
        {
            await _mediaOperations.StopAcceptingAndDrainAsync(linked.Token);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            _logger.LogWarning("MediaOperationDrainTimedOut");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "MediaOperationDrainFailed");
        }

        try
        {
            await _hostStopper.StopAsync(linked.Token);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            _logger.LogWarning("DesktopShutdownTimedOut");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "DesktopShutdownFailed");
        }
    }
}
