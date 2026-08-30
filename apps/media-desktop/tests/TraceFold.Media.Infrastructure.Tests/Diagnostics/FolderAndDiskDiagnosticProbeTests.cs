using TraceFold.Media.Application.Diagnostics;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Infrastructure.Diagnostics;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Diagnostics;

public sealed class FolderAndDiskDiagnosticProbeTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Fact]
    public async Task Check_reports_three_saved_folders_and_queries_a_shared_volume_once()
    {
        LibraryFolders folders = SeparateFolders();
        var volumeReader = new RecordingVolumeSpaceReader(
            new VolumeSpace(Path.GetPathRoot(folders.InboxDirectory)!, 100 * GiB, 80 * GiB));
        var probe = new FolderAndDiskDiagnosticProbe(
            new FixedSettingsStore(folders),
            new RecordingDirectoryProbe(),
            volumeReader);

        IReadOnlyList<SystemDiagnosticItem> items = await probe.CheckAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { "folders.inbox", "folders.library", "folders.work" },
            items.Select(item => item.Id));
        Assert.All(items, item => Assert.Equal(
            SystemDiagnosticSeverity.Healthy,
            item.Severity));
        Assert.All(
            items.SelectMany(item => item.Facts)
                .Where(fact => fact.Key.EndsWith(".path", StringComparison.Ordinal)),
            fact => Assert.Equal(
                SystemDiagnosticFactSensitivity.LocalOnly,
                fact.Sensitivity));
        Assert.Equal(1, volumeReader.CallCount);
        Assert.All(items, item =>
        {
            Assert.Contains(item.Facts, fact =>
                fact.Key.EndsWith(".total_bytes", StringComparison.Ordinal)
                && fact.Sensitivity == SystemDiagnosticFactSensitivity.Shareable);
            Assert.Contains(item.Facts, fact =>
                fact.Key.EndsWith(".available_bytes", StringComparison.Ordinal)
                && fact.Sensitivity == SystemDiagnosticFactSensitivity.Shareable);
        });
    }

    [Fact]
    public async Task Check_reports_each_logical_folder_when_settings_are_missing()
    {
        var directoryProbe = new RecordingDirectoryProbe();
        var volumeReader = new RecordingVolumeSpaceReader(
            new VolumeSpace("T:\\", 100 * GiB, 80 * GiB));
        var probe = new FolderAndDiskDiagnosticProbe(
            new FixedSettingsStore(null),
            directoryProbe,
            volumeReader);

        IReadOnlyList<SystemDiagnosticItem> items = await probe.CheckAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(3, items.Count);
        Assert.All(items, item =>
        {
            Assert.Equal(SystemDiagnosticSeverity.Unavailable, item.Severity);
            Assert.Equal("diagnostics.folder.not_configured", item.ErrorCode);
        });
        Assert.Equal(0, directoryProbe.ExistsCallCount);
        Assert.Equal(0, volumeReader.CallCount);
    }

    [Fact]
    public async Task Check_maps_invalid_missing_and_nonwritable_folders_to_stable_codes()
    {
        LibraryFolders folders = SeparateFolders() with
        {
            InboxDirectory = "relative-inbox"
        };
        var directoryProbe = new RecordingDirectoryProbe(
            missing: [folders.LibraryDirectory],
            nonwritable: [folders.WorkDirectory]);
        var probe = new FolderAndDiskDiagnosticProbe(
            new FixedSettingsStore(folders),
            directoryProbe,
            new RecordingVolumeSpaceReader(
                new VolumeSpace("T:\\", 100 * GiB, 80 * GiB)));

        IReadOnlyList<SystemDiagnosticItem> items = await probe.CheckAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "diagnostics.folder.invalid_path",
            items.Single(item => item.Id == "folders.inbox").ErrorCode);
        Assert.Equal(
            "diagnostics.folder.missing",
            items.Single(item => item.Id == "folders.library").ErrorCode);
        Assert.Equal(
            "diagnostics.folder.not_writable",
            items.Single(item => item.Id == "folders.work").ErrorCode);
    }

    [Fact]
    public async Task Check_marks_every_folder_in_an_unsafe_relationship()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"tracefold-folders-{Guid.NewGuid():N}");
        var folders = new LibraryFolders(
            Path.Combine(root, "shared"),
            Path.Combine(root, "shared", "nested"),
            Path.Combine(root, "work"));
        var probe = new FolderAndDiskDiagnosticProbe(
            new FixedSettingsStore(folders),
            new RecordingDirectoryProbe(),
            new RecordingVolumeSpaceReader(
                new VolumeSpace(Path.GetPathRoot(root)!, 100 * GiB, 80 * GiB)));

        IReadOnlyList<SystemDiagnosticItem> items = await probe.CheckAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "diagnostics.folder.unsafe_relationship",
            items.Single(item => item.Id == "folders.inbox").ErrorCode);
        Assert.Equal(
            "diagnostics.folder.unsafe_relationship",
            items.Single(item => item.Id == "folders.library").ErrorCode);
        Assert.Equal(
            SystemDiagnosticSeverity.Healthy,
            items.Single(item => item.Id == "folders.work").Severity);
    }

    [Theory]
    [InlineData(9, 100, SystemDiagnosticSeverity.Attention)]
    [InlineData(10, 100, SystemDiagnosticSeverity.Healthy)]
    [InlineData(50, 1000, SystemDiagnosticSeverity.Attention)]
    [InlineData(100, 1000, SystemDiagnosticSeverity.Healthy)]
    public async Task Check_applies_both_low_space_boundaries(
        long freeGiB,
        long totalGiB,
        SystemDiagnosticSeverity expected)
    {
        LibraryFolders folders = SeparateFolders();
        var probe = new FolderAndDiskDiagnosticProbe(
            new FixedSettingsStore(folders),
            new RecordingDirectoryProbe(),
            new RecordingVolumeSpaceReader(new VolumeSpace(
                Path.GetPathRoot(folders.InboxDirectory)!,
                totalGiB * GiB,
                freeGiB * GiB)));

        IReadOnlyList<SystemDiagnosticItem> items = await probe.CheckAsync(
            TestContext.Current.CancellationToken);

        Assert.All(items, item => Assert.Equal(expected, item.Severity));
        Assert.All(items, item => Assert.Equal(
            expected == SystemDiagnosticSeverity.Attention
                ? "diagnostics.storage.space_low"
                : null,
            item.ErrorCode));
    }

    [Fact]
    public async Task Capacity_query_failure_is_attention_without_hiding_folder_writability()
    {
        LibraryFolders folders = SeparateFolders();
        var probe = new FolderAndDiskDiagnosticProbe(
            new FixedSettingsStore(folders),
            new RecordingDirectoryProbe(),
            new RecordingVolumeSpaceReader(new IOException("private path")));

        IReadOnlyList<SystemDiagnosticItem> items = await probe.CheckAsync(
            TestContext.Current.CancellationToken);

        Assert.All(items, item =>
        {
            Assert.Equal(SystemDiagnosticSeverity.Attention, item.Severity);
            Assert.Equal("diagnostics.storage.query_failed", item.ErrorCode);
            Assert.Contains(item.Facts, fact =>
                fact.Key.EndsWith(".writable", StringComparison.Ordinal)
                && fact.Value == "是");
        });
    }

    [Fact]
    public async Task Every_folder_action_uses_its_registered_focus_target()
    {
        LibraryFolders folders = SeparateFolders();
        var probe = new FolderAndDiskDiagnosticProbe(
            new FixedSettingsStore(folders),
            new RecordingDirectoryProbe(),
            new RecordingVolumeSpaceReader(new VolumeSpace(
                Path.GetPathRoot(folders.InboxDirectory)!,
                100 * GiB,
                80 * GiB)));

        IReadOnlyList<SystemDiagnosticItem> items = await probe.CheckAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new[]
            {
                SystemDiagnosticActionTarget.InboxDirectory,
                SystemDiagnosticActionTarget.LibraryDirectory,
                SystemDiagnosticActionTarget.WorkDirectory
            },
            items.Select(item => Assert.Single(item.Actions).Target));
        Assert.All(items, item => Assert.Equal(
            SystemDiagnosticActionKind.NavigateToFolderSettings,
            Assert.Single(item.Actions).Kind));
    }

    private static LibraryFolders SeparateFolders()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"tracefold-folders-{Guid.NewGuid():N}");
        return new LibraryFolders(
            Path.Combine(root, "inbox"),
            Path.Combine(root, "library"),
            Path.Combine(root, "work"));
    }

    private sealed class FixedSettingsStore(LibraryFolders? folders)
        : IDirectorySettingsStore
    {
        public Task<LibraryFolders?> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(folders);
        }

        public Task SaveAsync(
            LibraryFolders value,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Diagnostics cannot save settings.");
    }

    private sealed class RecordingDirectoryProbe(
        IReadOnlyList<string>? missing = null,
        IReadOnlyList<string>? nonwritable = null) : IDirectoryProbe
    {
        private readonly HashSet<string> _missing = new(
            missing ?? [],
            StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _nonwritable = new(
            nonwritable ?? [],
            StringComparer.OrdinalIgnoreCase);

        public int ExistsCallCount { get; private set; }

        public ValueTask<bool> ExistsAsync(
            string path,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExistsCallCount++;
            return ValueTask.FromResult(!_missing.Contains(path));
        }

        public ValueTask<bool> CanWriteAsync(
            string path,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(!_nonwritable.Contains(path));
        }
    }

    private sealed class RecordingVolumeSpaceReader : IVolumeSpaceReader
    {
        private readonly VolumeSpace? _result;
        private readonly Exception? _error;

        public RecordingVolumeSpaceReader(VolumeSpace result)
        {
            _result = result;
        }

        public RecordingVolumeSpaceReader(Exception error)
        {
            _error = error;
        }

        public int CallCount { get; private set; }

        public ValueTask<VolumeSpace> ReadAsync(
            string volumeRoot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            if (_error is not null)
            {
                throw _error;
            }

            return ValueTask.FromResult(_result!);
        }
    }
}
