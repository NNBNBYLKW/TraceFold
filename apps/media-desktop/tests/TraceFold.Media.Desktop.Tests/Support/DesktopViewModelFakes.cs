using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Diagnostics;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Startup;
using TraceFold.Media.Desktop.Services;

namespace TraceFold.Media.Desktop.Tests.Support;

internal sealed class FakeStartupCoordinator : IStartupCoordinator
{
    public StartupStatus Current { get; private set; } = new(
        StartupState.Initializing,
        "软件正在初始化。",
        []);

    public int InitializeCallCount { get; private set; }

    public event EventHandler<StartupStatus>? StatusChanged;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        InitializeCallCount++;
        return Task.CompletedTask;
    }

    public void Publish(StartupStatus status)
    {
        Current = status;
        StatusChanged?.Invoke(this, status);
    }
}

internal sealed class InMemorySettingsStore : IDirectorySettingsStore
{
    public LibraryFolders? Saved { get; private set; }

    public Task<LibraryFolders?> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Saved);

    public Task SaveAsync(
        LibraryFolders folders,
        CancellationToken cancellationToken)
    {
        Saved = folders;
        return Task.CompletedTask;
    }
}

internal sealed class AlwaysWritableProbe : IDirectoryProbe
{
    public ValueTask<bool> ExistsAsync(
        string path,
        CancellationToken cancellationToken) => ValueTask.FromResult(true);

    public ValueTask<bool> CanWriteAsync(
        string path,
        CancellationToken cancellationToken) => ValueTask.FromResult(true);
}

internal sealed class QueueFolderPicker(params string?[] selections) : IFolderPicker
{
    private readonly Queue<string?> _selections = new(selections);

    public Task<string?> PickFolderAsync(
        string title,
        CancellationToken cancellationToken) =>
        Task.FromResult(_selections.Count == 0 ? null : _selections.Dequeue());
}

internal sealed class FakeDatabaseInitializer : IDatabaseInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

internal sealed class RecordingDiagnosticClipboardService
    : IDiagnosticClipboardService
{
    public DiagnosticDesktopActionResult Result { get; set; } =
        DiagnosticDesktopActionResult.Success();

    public string? CopiedText { get; private set; }

    public int CallCount { get; private set; }

    public Task<DiagnosticDesktopActionResult> CopyAsync(
        string text,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        CopiedText = text;
        return Task.FromResult(Result);
    }
}

internal sealed class RecordingRegisteredLogFolderService
    : IRegisteredLogFolderService
{
    public DiagnosticDesktopActionResult Result { get; set; } =
        DiagnosticDesktopActionResult.Success();

    public int CallCount { get; private set; }

    public Task<DiagnosticDesktopActionResult> OpenAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        return Task.FromResult(Result);
    }
}

internal sealed class RecordingDiagnosticSummaryFormatter(string summary)
    : IDiagnosticSummaryFormatter
{
    public SystemDiagnosticsSnapshot? Snapshot { get; private set; }

    public TimeZoneInfo? TimeZone { get; private set; }

    public string Format(
        SystemDiagnosticsSnapshot snapshot,
        TimeZoneInfo timeZone)
    {
        Snapshot = snapshot;
        TimeZone = timeZone;
        return summary;
    }
}
