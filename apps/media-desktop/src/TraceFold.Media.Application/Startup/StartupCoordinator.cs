using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Settings;

namespace TraceFold.Media.Application.Startup;

public sealed class StartupCoordinator(
    IDatabaseInitializer databaseInitializer,
    DirectorySettingsService settingsService,
    ILogger<StartupCoordinator> logger) : IStartupCoordinator
{
    public StartupStatus Current { get; private set; } = new(
        StartupState.Initializing,
        "软件正在初始化。",
        []);

    public event EventHandler<StartupStatus>? StatusChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await databaseInitializer.InitializeAsync(cancellationToken);
            LibraryFolders? saved =
                await settingsService.LoadAsync(cancellationToken);
            if (saved is null)
            {
                Publish(new StartupStatus(
                    StartupState.SetupRequired,
                    "请先配置三个工作目录。",
                    []));
                return;
            }

            DirectorySettingsResult validation =
                await settingsService.ValidateAsync(saved, cancellationToken);
            if (!validation.Succeeded)
            {
                Publish(new StartupStatus(
                    StartupState.SetupRequired,
                    "目录配置无效，请重新配置。",
                    validation.Errors));
                return;
            }

            Publish(new StartupStatus(
                StartupState.Ready,
                "软件已就绪。",
                []));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "DesktopStartupFailed");
            Publish(new StartupStatus(
                StartupState.Failed,
                "软件初始化失败，请查看本地日志。",
                []));
        }
    }

    private void Publish(StartupStatus status)
    {
        Current = status;
        StatusChanged?.Invoke(this, status);
    }
}
