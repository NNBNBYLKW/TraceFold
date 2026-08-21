using TraceFold.Media.Application.Settings;

namespace TraceFold.Media.Application.Startup;

public enum StartupState
{
    Initializing,
    SetupRequired,
    Ready,
    Failed
}

public sealed record StartupStatus(
    StartupState State,
    string Message,
    IReadOnlyList<SettingsError> Errors);

public interface IStartupCoordinator
{
    StartupStatus Current { get; }

    event EventHandler<StartupStatus>? StatusChanged;

    Task InitializeAsync(CancellationToken cancellationToken);
}
