using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Diagnostics;
using TraceFold.Media.Desktop.Services;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Services;

public sealed class DiagnosticDesktopServicesTests
{
    [Fact]
    public async Task OpenLogFolder_uses_only_the_registered_log_root()
    {
        using TemporaryDirectory root = TemporaryDirectory.Create();
        string logs = Directory.CreateDirectory(
            Path.Combine(root.Path, "logs")).FullName;
        var starter = new RecordingProcessStarter();
        var service = new WindowsRegisteredLogFolderService(
            new FixedPathProvider(root.Path, logs),
            starter);

        DiagnosticDesktopActionResult result = await service.OpenAsync(
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
        Assert.Equal("explorer.exe", starter.StartInfo!.FileName);
        Assert.False(starter.StartInfo.UseShellExecute);
        Assert.Equal(new[] { logs }, starter.StartInfo.ArgumentList);
    }

    [Fact]
    public async Task OpenLogFolder_does_not_launch_when_registered_root_is_missing()
    {
        using TemporaryDirectory root = TemporaryDirectory.Create();
        var starter = new RecordingProcessStarter();
        var service = new WindowsRegisteredLogFolderService(
            new FixedPathProvider(
                root.Path,
                Path.Combine(root.Path, "missing")),
            starter);

        DiagnosticDesktopActionResult result = await service.OpenAsync(
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("diagnostics.logs.open_failed", result.ErrorCode);
        Assert.Null(starter.StartInfo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative-logs")]
    public async Task OpenLogFolder_rejects_invalid_registered_roots(string logs)
    {
        using TemporaryDirectory root = TemporaryDirectory.Create();
        var starter = new RecordingProcessStarter();
        var service = new WindowsRegisteredLogFolderService(
            new FixedPathProvider(root.Path, logs),
            starter);

        DiagnosticDesktopActionResult result = await service.OpenAsync(
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("diagnostics.logs.open_failed", result.ErrorCode);
        Assert.Null(starter.StartInfo);
    }

    [Fact]
    public async Task OpenLogFolder_maps_process_start_failure_to_a_stable_code()
    {
        using TemporaryDirectory root = TemporaryDirectory.Create();
        string logs = Directory.CreateDirectory(
            Path.Combine(root.Path, "logs")).FullName;
        var service = new WindowsRegisteredLogFolderService(
            new FixedPathProvider(root.Path, logs),
            new FailingProcessStarter(
                new Win32Exception("C:\\private\\logs")));

        DiagnosticDesktopActionResult result = await service.OpenAsync(
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("diagnostics.logs.open_failed", result.ErrorCode);
    }

    [Fact]
    public void Registered_log_service_has_no_caller_path_or_fact_api()
    {
        MethodInfo method = Assert.Single(typeof(IRegisteredLogFolderService)
            .GetMethods());

        Assert.Equal(nameof(IRegisteredLogFolderService.OpenAsync), method.Name);
        Assert.Equal(
            new[] { typeof(CancellationToken) },
            method.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.DoesNotContain(
            typeof(WindowsRegisteredLogFolderService).GetMethods(
                BindingFlags.Instance | BindingFlags.Public),
            candidate => candidate.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(SystemDiagnosticFact)));
    }

    [Fact]
    public async Task Clipboard_copies_plain_text_through_the_platform_boundary()
    {
        var platform = new RecordingClipboardPlatform();
        var service = new WindowsDiagnosticClipboardService(platform);

        DiagnosticDesktopActionResult result = await service.CopyAsync(
            "shareable diagnostics",
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("shareable diagnostics", platform.Text);
        Assert.DoesNotContain(
            typeof(WindowsDiagnosticClipboardService).GetProperties(),
            property => property.PropertyType == typeof(string));
    }

    [Fact]
    public async Task Clipboard_rejects_blank_input_without_calling_the_platform()
    {
        var platform = new RecordingClipboardPlatform();
        var service = new WindowsDiagnosticClipboardService(platform);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.CopyAsync(
                "   ",
                TestContext.Current.CancellationToken));

        Assert.Null(platform.Text);
    }

    [Fact]
    public async Task Clipboard_maps_expected_platform_failure_to_a_stable_code()
    {
        var service = new WindowsDiagnosticClipboardService(
            new FailingClipboardPlatform(
                new COMException("private copied text")));

        DiagnosticDesktopActionResult result = await service.CopyAsync(
            "private copied text",
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("diagnostics.clipboard.copy_failed", result.ErrorCode);
        Assert.DoesNotContain(
            "private copied text",
            result.ErrorCode,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clipboard_honors_pre_cancelled_token_without_platform_access()
    {
        var platform = new RecordingClipboardPlatform();
        var service = new WindowsDiagnosticClipboardService(platform);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await service.CopyAsync("diagnostics", cancellation.Token));

        Assert.Null(platform.Text);
    }

    private sealed class FixedPathProvider(string root, string logs)
        : IAppDataPathProvider
    {
        public AppDataPaths GetPaths() => new(
            root,
            Path.Combine(root, "media-desktop.db"),
            logs,
            Path.Combine(root, "work"));
    }

    private sealed class RecordingProcessStarter : IProcessStarter
    {
        public ProcessStartInfo? StartInfo { get; private set; }

        public void Start(ProcessStartInfo startInfo) => StartInfo = startInfo;
    }

    private sealed class FailingProcessStarter(Exception failure) : IProcessStarter
    {
        public void Start(ProcessStartInfo startInfo) => throw failure;
    }

    private sealed class RecordingClipboardPlatform : IDiagnosticClipboardPlatform
    {
        public string? Text { get; private set; }

        public void SetText(string text) => Text = text;
    }

    private sealed class FailingClipboardPlatform(Exception failure)
        : IDiagnosticClipboardPlatform
    {
        public void SetText(string text) => throw failure;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"tracefold-diagnostic-desktop-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
