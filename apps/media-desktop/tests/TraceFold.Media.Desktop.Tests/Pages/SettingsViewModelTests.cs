using TraceFold.Media.Application.Settings;
using TraceFold.Media.Desktop.Pages;
using TraceFold.Media.Desktop.Services;
using TraceFold.Media.Desktop.Tests.Support;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task Folder_commands_update_only_the_matching_property()
    {
        var viewModel = CreateViewModel(
            new QueueFolderPicker("C:\\picked-inbox", "C:\\picked-library", "C:\\picked-work"));

        await viewModel.ChooseInboxCommand.ExecuteAsync(null);
        Assert.Equal("C:\\picked-inbox", viewModel.InboxDirectory);
        Assert.Empty(viewModel.LibraryDirectory);
        Assert.Empty(viewModel.WorkDirectory);

        await viewModel.ChooseLibraryCommand.ExecuteAsync(null);
        Assert.Equal("C:\\picked-inbox", viewModel.InboxDirectory);
        Assert.Equal("C:\\picked-library", viewModel.LibraryDirectory);
        Assert.Empty(viewModel.WorkDirectory);

        await viewModel.ChooseWorkCommand.ExecuteAsync(null);
        Assert.Equal("C:\\picked-inbox", viewModel.InboxDirectory);
        Assert.Equal("C:\\picked-library", viewModel.LibraryDirectory);
        Assert.Equal("C:\\picked-work", viewModel.WorkDirectory);
    }

    [Fact]
    public async Task SaveCommand_exposes_validation_errors_without_success()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var store = new InMemorySettingsStore();
        var startup = new FakeStartupCoordinator();
        var viewModel = new SettingsViewModel(
            new QueueFolderPicker(),
            new DirectorySettingsService(new AlwaysWritableProbe(), store),
            startup)
        {
            InboxDirectory = Path.Combine(root, "shared"),
            LibraryDirectory = Path.Combine(root, "shared"),
            WorkDirectory = Path.Combine(root, "work")
        };

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Contains(viewModel.Errors, error => error.Code == "directory.overlap");
        Assert.NotEqual("设置已保存。", viewModel.StatusMessage);
        Assert.Null(store.Saved);
        Assert.Equal(0, startup.InitializeCallCount);
    }

    [Fact]
    public async Task SaveCommand_saves_valid_folders_and_rechecks_startup()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var store = new InMemorySettingsStore();
        var startup = new FakeStartupCoordinator();
        var viewModel = new SettingsViewModel(
            new QueueFolderPicker(),
            new DirectorySettingsService(new AlwaysWritableProbe(), store),
            startup)
        {
            InboxDirectory = Path.Combine(root, "inbox"),
            LibraryDirectory = Path.Combine(root, "library"),
            WorkDirectory = Path.Combine(root, "work")
        };

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("设置已保存。", viewModel.StatusMessage);
        Assert.Empty(viewModel.Errors);
        Assert.NotNull(store.Saved);
        Assert.Equal(1, startup.InitializeCallCount);
    }

    [Fact]
    public async Task LoadAsync_populates_all_saved_folder_paths()
    {
        var saved = new LibraryFolders(
            "C:\\saved-inbox",
            "C:\\saved-library",
            "C:\\saved-work");
        var store = new InMemorySettingsStore();
        await store.SaveAsync(saved, CancellationToken.None);
        var viewModel = new SettingsViewModel(
            new QueueFolderPicker(),
            new DirectorySettingsService(new AlwaysWritableProbe(), store),
            new FakeStartupCoordinator());

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(saved.InboxDirectory, viewModel.InboxDirectory);
        Assert.Equal(saved.LibraryDirectory, viewModel.LibraryDirectory);
        Assert.Equal(saved.WorkDirectory, viewModel.WorkDirectory);
    }

    [Fact]
    public void Busy_state_disables_folder_and_save_commands()
    {
        var viewModel = CreateViewModel(new QueueFolderPicker());

        viewModel.IsBusy = true;

        Assert.False(viewModel.ChooseInboxCommand.CanExecute(null));
        Assert.False(viewModel.ChooseLibraryCommand.CanExecute(null));
        Assert.False(viewModel.ChooseWorkCommand.CanExecute(null));
        Assert.False(viewModel.SaveCommand.CanExecute(null));
    }

    private static SettingsViewModel CreateViewModel(IFolderPicker picker) =>
        new(
            picker,
            new DirectorySettingsService(
                new AlwaysWritableProbe(),
                new InMemorySettingsStore()),
            new FakeStartupCoordinator());
}
