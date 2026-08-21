using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Startup;
using TraceFold.Media.Desktop.Services;

namespace TraceFold.Media.Desktop.Pages;

public sealed partial class SettingsViewModel(
    IFolderPicker folderPicker,
    DirectorySettingsService settingsService,
    IStartupCoordinator startupCoordinator) : ObservableObject
{
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
    }
}
