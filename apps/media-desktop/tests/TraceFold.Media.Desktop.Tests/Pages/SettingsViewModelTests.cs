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
            startup,
            new FakeScanPreferencesService(ScanPreferences.Default))
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
            startup,
            new FakeScanPreferencesService(ScanPreferences.Default))
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
            new FakeStartupCoordinator(),
            new FakeScanPreferencesService(ScanPreferences.Default));

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal(saved.InboxDirectory, viewModel.InboxDirectory);
        Assert.Equal(saved.LibraryDirectory, viewModel.LibraryDirectory);
        Assert.Equal(saved.WorkDirectory, viewModel.WorkDirectory);
    }

    [Fact]
    public async Task LoadAsync_shows_auto_scan_off_and_last_result()
    {
        DateTimeOffset instant = new(2026, 8, 22, 8, 30, 0, TimeSpan.Zero);
        var preferences = new FakeScanPreferencesService(
            new ScanPreferences(
                false,
                instant,
                "扫描完成：发现 2 个文件，新增 1 项。"));
        SettingsViewModel viewModel = CreateViewModel(
            new QueueFolderPicker(),
            preferences);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.False(viewModel.AutoScanEnabled);
        Assert.Contains("扫描完成", viewModel.LastScanSummary);
        Assert.NotEqual("尚未扫描", viewModel.LastScanAtText);
    }

    [Fact]
    public async Task Auto_scan_change_is_reverted_until_directories_are_saved()
    {
        var preferences = new FakeScanPreferencesService(ScanPreferences.Default);
        SettingsViewModel viewModel = CreateViewModel(
            new QueueFolderPicker(),
            preferences);

        await viewModel.SaveAutoScanAsync(true, CancellationToken.None);

        Assert.False(viewModel.AutoScanEnabled);
        Assert.Equal(0, preferences.SaveCallCount);
        Assert.Equal("请先保存三个工作目录。", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Auto_scan_change_is_persisted_after_directories_are_saved()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var store = new InMemorySettingsStore();
        await store.SaveAsync(
            new LibraryFolders(
                Path.Combine(root, "inbox"),
                Path.Combine(root, "library"),
                Path.Combine(root, "work")),
            CancellationToken.None);
        var preferences = new FakeScanPreferencesService(ScanPreferences.Default);
        var viewModel = new SettingsViewModel(
            new QueueFolderPicker(),
            new DirectorySettingsService(new AlwaysWritableProbe(), store),
            new FakeStartupCoordinator(),
            preferences);

        await viewModel.SaveAutoScanAsync(true, CancellationToken.None);

        Assert.True(viewModel.AutoScanEnabled);
        Assert.Equal(1, preferences.SaveCallCount);
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

    private static SettingsViewModel CreateViewModel(
        IFolderPicker picker,
        IScanPreferencesService? preferences = null) =>
        new(
            picker,
            new DirectorySettingsService(
                new AlwaysWritableProbe(),
                new InMemorySettingsStore()),
            new FakeStartupCoordinator(),
            preferences ?? new FakeScanPreferencesService(ScanPreferences.Default));

    private sealed class FakeScanPreferencesService(ScanPreferences preferences)
        : IScanPreferencesService
    {
        private ScanPreferences _preferences = preferences;

        public int SaveCallCount { get; private set; }

        public Task<ScanPreferences> LoadAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult(_preferences);
        }

        public Task<ScanPreferenceResult> SetAutoScanEnabledAsync(
            bool enabled,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            SaveCallCount++;
            _preferences = _preferences with { AutoScanEnabled = enabled };
            return Task.FromResult(new ScanPreferenceResult(
                true,
                _preferences,
                null));
        }
    }
}
