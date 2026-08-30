using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using TraceFold.Media.Application.Diagnostics;

namespace TraceFold.Media.Desktop.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _autoScanReady;
    private bool _handlingAutoScan;

    public SettingsPage(
        SettingsViewModel viewModel,
        SystemDiagnosticsViewModel diagnosticsViewModel)
    {
        ViewModel = viewModel;
        DiagnosticsViewModel = diagnosticsViewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DiagnosticsViewModel.FolderSettingsNavigationRequested +=
            OnFolderSettingsNavigationRequested;
    }

    public SettingsViewModel ViewModel { get; }

    public SystemDiagnosticsViewModel DiagnosticsViewModel { get; }

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

    private async void OnSettingsTabSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (!ReferenceEquals(SettingsTabs.SelectedItem, SystemDiagnosticsTab))
        {
            return;
        }

        try
        {
            await DiagnosticsViewModel.ActivateAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            Log.Error("DesktopSystemDiagnosticsActivationFailed");
            DiagnosticsViewModel.ReportUiBoundaryFailure();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        DiagnosticsViewModel.CancelCurrentRun();
    }

    private void OnFolderSettingsNavigationRequested(
        object? sender,
        SystemDiagnosticActionTarget target)
    {
        _ = sender;
        TextBox? folderField = target switch
        {
            SystemDiagnosticActionTarget.InboxDirectory => InboxFolderPath,
            SystemDiagnosticActionTarget.LibraryDirectory => LibraryFolderPath,
            SystemDiagnosticActionTarget.WorkDirectory => WorkFolderPath,
            _ => null
        };
        if (folderField is null)
        {
            return;
        }

        SettingsTabs.SelectedItem = FolderSettingsTab;
        folderField.Focus(FocusState.Programmatic);
        folderField.SelectAll();
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
