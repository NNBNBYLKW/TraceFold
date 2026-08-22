using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Startup;
using TraceFold.Media.Desktop.Services;

namespace TraceFold.Media.Desktop.Pages;

public sealed partial class SettingsViewModel(
    IFolderPicker folderPicker,
    DirectorySettingsService settingsService,
    IStartupCoordinator startupCoordinator,
    IScanPreferencesService scanPreferencesService) : ObservableObject
{
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private bool _hasLoaded;

    [ObservableProperty]
    public partial string InboxDirectory { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LibraryDirectory { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WorkDirectory { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<SettingsError> Errors { get; set; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool AutoScanEnabled { get; private set; }

    [ObservableProperty]
    public partial string LastScanAtText { get; private set; } = "尚未扫描";

    [ObservableProperty]
    public partial string LastScanSummary { get; private set; } = "尚无扫描结果。";

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            IsBusy = true;
            try
            {
                if (!_hasLoaded)
                {
                    LibraryFolders? saved =
                        await settingsService.LoadAsync(cancellationToken);
                    if (saved is not null)
                    {
                        InboxDirectory = saved.InboxDirectory;
                        LibraryDirectory = saved.LibraryDirectory;
                        WorkDirectory = saved.WorkDirectory;
                    }

                    _hasLoaded = true;
                }

                ScanPreferences preferences =
                    await scanPreferencesService.LoadAsync(cancellationToken);
                ApplyScanPreferences(preferences);
            }
            finally
            {
                IsBusy = false;
            }
        }
        finally
        {
            _loadGate.Release();
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task ChooseInboxAsync(CancellationToken cancellationToken)
    {
        await ChooseFolderAsync(
            "选择待入库目录",
            value => InboxDirectory = value,
            cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task ChooseLibraryAsync(CancellationToken cancellationToken)
    {
        await ChooseFolderAsync(
            "选择正式文件库目录",
            value => LibraryDirectory = value,
            cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task ChooseWorkAsync(CancellationToken cancellationToken)
    {
        await ChooseFolderAsync(
            "选择工作目录",
            value => WorkDirectory = value,
            cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            DirectorySettingsResult result = await settingsService.SaveAsync(
                new LibraryFolders(
                    InboxDirectory,
                    LibraryDirectory,
                    WorkDirectory),
                cancellationToken);
            Errors = result.Errors;
            if (!result.Succeeded)
            {
                StatusMessage = "请修正目录设置。";
                return;
            }

            StatusMessage = "设置已保存。";
            await startupCoordinator.InitializeAsync(cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    public async Task SaveAutoScanAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            LibraryFolders? saved = await settingsService.LoadAsync(cancellationToken);
            if (saved is null)
            {
                SynchronizeAutoScanEnabled(!enabled);
                StatusMessage = "请先保存三个工作目录。";
                return;
            }

            ScanPreferenceResult result =
                await scanPreferencesService.SetAutoScanEnabledAsync(
                    enabled,
                    cancellationToken);
            ApplyScanPreferences(result.Preferences);
            StatusMessage = result.Succeeded
                ? enabled ? "已开启自动扫描。" : "已关闭自动扫描。"
                : "自动扫描设置保存失败。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanInteract() => !IsBusy;

    private async Task ChooseFolderAsync(
        string title,
        Action<string> applySelection,
        CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            string? selected =
                await folderPicker.PickFolderAsync(title, cancellationToken);
            if (selected is not null)
            {
                applySelection(selected);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        ChooseInboxCommand.NotifyCanExecuteChanged();
        ChooseLibraryCommand.NotifyCanExecuteChanged();
        ChooseWorkCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        SaveAutoScanCommand.NotifyCanExecuteChanged();
    }

    private void ApplyScanPreferences(ScanPreferences preferences)
    {
        SynchronizeAutoScanEnabled(preferences.AutoScanEnabled);
        LastScanAtText = preferences.LastScanAtUtc is DateTimeOffset completedAt
            ? $"上次扫描：{completedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
            : "尚未扫描";
        LastScanSummary = string.IsNullOrWhiteSpace(preferences.LastScanSummary)
            ? "尚无扫描结果。"
            : preferences.LastScanSummary;
    }

    private void SynchronizeAutoScanEnabled(bool enabled)
    {
        if (AutoScanEnabled == enabled)
        {
            OnPropertyChanged(nameof(AutoScanEnabled));
            return;
        }

        AutoScanEnabled = enabled;
    }
}
