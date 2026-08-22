using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Startup;
using Xunit;

namespace TraceFold.Media.Application.Tests.Startup;

public sealed class StartupCoordinatorTests
{
    [Fact]
    public async Task InitializeAsync_initializes_database_before_loading_settings()
    {
        var calls = new List<string>();
        var initializer = new FakeDatabaseInitializer(() => calls.Add("database"));
        var store = new FakeSettingsStore(null, () => calls.Add("settings"));
        var coordinator = new StartupCoordinator(
            initializer,
            new DirectorySettingsService(new AlwaysWritableProbe(), store),
            new RecordingLogger<StartupCoordinator>());

        await coordinator.InitializeAsync(CancellationToken.None);

        Assert.Equal(["database", "settings"], calls);
        Assert.Equal(StartupState.SetupRequired, coordinator.Current.State);
        Assert.Equal("请先配置三个工作目录。", coordinator.Current.Message);
    }

    [Theory]
    [MemberData(nameof(ValidFolders))]
    public async Task InitializeAsync_publishes_ready_for_valid_saved_folders(
        LibraryFolders folders)
    {
        var coordinator = CreateCoordinator(folders);

        await coordinator.InitializeAsync(CancellationToken.None);

        Assert.Equal(StartupState.Ready, coordinator.Current.State);
        Assert.Empty(coordinator.Current.Errors);
    }

    [Theory]
    [MemberData(nameof(InvalidFolders))]
    public async Task InitializeAsync_requires_setup_for_invalid_saved_folders(
        LibraryFolders folders,
        string expectedErrorCode)
    {
        var coordinator = CreateCoordinator(folders);

        await coordinator.InitializeAsync(CancellationToken.None);

        Assert.Equal(StartupState.SetupRequired, coordinator.Current.State);
        Assert.Contains(
            coordinator.Current.Errors,
            error => error.Code == expectedErrorCode);
    }

    [Fact]
    public async Task InitializeAsync_converts_initializer_exception_to_safe_failed_status()
    {
        var logger = new RecordingLogger<StartupCoordinator>();
        var initializer = new FakeDatabaseInitializer(
            () => throw new InvalidOperationException("sensitive stack detail"));
        var coordinator = new StartupCoordinator(
            initializer,
            new DirectorySettingsService(
                new AlwaysWritableProbe(),
                new FakeSettingsStore(null)),
            logger);

        await coordinator.InitializeAsync(CancellationToken.None);

        Assert.Equal(StartupState.Failed, coordinator.Current.State);
        Assert.Equal("软件初始化失败，请查看本地日志。", coordinator.Current.Message);
        Assert.DoesNotContain(
            "sensitive",
            coordinator.Current.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            logger.Events,
            item => item.Contains("DesktopStartupFailed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InitializeAsync_recovers_ingest_before_opening_runtime_gate()
    {
        var calls = new List<string>();
        LibraryFolders folders = CreateSiblingFolders();
        var coordinator = new StartupCoordinator(
            new FakeDatabaseInitializer(() => calls.Add("database")),
            new DirectorySettingsService(
                new AlwaysWritableProbe(),
                new FakeSettingsStore(folders)),
            new RecordingLogger<StartupCoordinator>(),
            new RecordingRecovery(() => calls.Add("recovery")),
            new RecordingRuntimeGate(() => calls.Add("gate")));

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["database", "recovery", "gate"], calls);
        Assert.Equal(StartupState.Ready, coordinator.Current.State);
    }

    public static TheoryData<LibraryFolders> ValidFolders =>
        new()
        {
            CreateSiblingFolders()
        };

    public static TheoryData<LibraryFolders, string> InvalidFolders
    {
        get
        {
            LibraryFolders valid = CreateSiblingFolders();
            return new TheoryData<LibraryFolders, string>
            {
                {
                    valid with { LibraryDirectory = valid.InboxDirectory },
                    "directory.overlap"
                }
            };
        }
    }

    private static StartupCoordinator CreateCoordinator(LibraryFolders? folders) =>
        new(
            new FakeDatabaseInitializer(() => { }),
            new DirectorySettingsService(
                new AlwaysWritableProbe(),
                new FakeSettingsStore(folders)),
            new RecordingLogger<StartupCoordinator>());

    private static LibraryFolders CreateSiblingFolders()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"tracefold-startup-{Guid.NewGuid():N}");
        return new LibraryFolders(
            Path.Combine(root, "inbox"),
            Path.Combine(root, "library"),
            Path.Combine(root, "work"));
    }

    private sealed class FakeDatabaseInitializer(Action callback)
        : IDatabaseInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            callback();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSettingsStore(
        LibraryFolders? folders,
        Action? loadCallback = null) : IDirectorySettingsStore
    {
        public Task<LibraryFolders?> LoadAsync(CancellationToken cancellationToken)
        {
            loadCallback?.Invoke();
            return Task.FromResult(folders);
        }

        public Task SaveAsync(
            LibraryFolders value,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class AlwaysWritableProbe : IDirectoryProbe
    {
        public ValueTask<bool> ExistsAsync(
            string path,
            CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public ValueTask<bool> CanWriteAsync(
            string path,
            CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Events { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Events.Add(formatter(state, exception));
        }
    }

    private sealed class RecordingRecovery(Action callback) : IIngestRecoveryService
    {
        public Task ReconcileAsync(CancellationToken cancellationToken)
        {
            callback();
            return Task.CompletedTask;
        }

        public Task<RecoveryResult> RetryAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RecoveryResult(true, "ok"));
    }

    private sealed class RecordingRuntimeGate(Action callback) : IMediaRuntimeGate
    {
        public Task WaitUntilReadyAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Open() => callback();
    }
}
