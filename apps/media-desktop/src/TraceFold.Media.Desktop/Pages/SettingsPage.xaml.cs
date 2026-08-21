using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace TraceFold.Media.Desktop.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public SettingsViewModel ViewModel { get; }

    private async void OnLoaded(
        object sender,
        Microsoft.UI.Xaml.RoutedEventArgs args)
    {
        try
        {
            await ViewModel.LoadAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "DesktopSettingsLoadFailed");
            ViewModel.StatusMessage = "读取设置失败，请查看本地日志。";
        }
    }
}
