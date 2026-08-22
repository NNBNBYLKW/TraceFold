using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace TraceFold.Media.Desktop.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _autoScanReady;
    private bool _handlingAutoScan;

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
            _autoScanReady = false;
            await ViewModel.LoadAsync(CancellationToken.None);
            _autoScanReady = true;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "DesktopSettingsLoadFailed");
            ViewModel.StatusMessage = "读取设置失败，请查看本地日志。";
        }
    }

    private async void OnAutoScanToggled(
        object sender,
        Microsoft.UI.Xaml.RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (!_autoScanReady || _handlingAutoScan)
        {
            return;
        }

        _handlingAutoScan = true;
        try
        {
            await ViewModel.SaveAutoScanAsync(
                AutoScanToggle.IsOn,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "DesktopAutoScanPreferenceSaveFailed");
            ViewModel.StatusMessage = "自动扫描设置保存失败。";
        }
        finally
        {
            _handlingAutoScan = false;
        }
    }
}
