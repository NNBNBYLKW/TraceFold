using CommunityToolkit.Mvvm.ComponentModel;
using TraceFold.Media.Application.Startup;

namespace TraceFold.Media.Desktop.Shell;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IStartupCoordinator _startupCoordinator;

    public ShellViewModel(IStartupCoordinator startupCoordinator)
    {
        _startupCoordinator = startupCoordinator;
        _startupCoordinator.StatusChanged += OnStatusChanged;
        ApplyStatus(_startupCoordinator.Current);
    }

    [ObservableProperty]
    public partial string SelectedPageKey { get; set; } = "inbox";

    [ObservableProperty]
    public partial StartupState StartupState { get; set; }

    [ObservableProperty]
    public partial string StartupMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsStartupInfoBarOpen { get; set; }

    private void OnStatusChanged(object? sender, StartupStatus status) =>
        ApplyStatus(status);

    private void ApplyStatus(StartupStatus status)
    {
        StartupState = status.State;
        StartupMessage = status.Message;
        IsStartupInfoBarOpen = status.State != Application.Startup.StartupState.Ready;
        if (status.State == Application.Startup.StartupState.SetupRequired)
        {
            SelectedPageKey = "settings";
        }
    }
}
