using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using TraceFold.Media.Application.Common;

namespace TraceFold.Media.Desktop.Services;

public sealed record DiagnosticDesktopActionResult
{
    private DiagnosticDesktopActionResult(bool succeeded, string? errorCode)
    {
        Succeeded = succeeded;
        ErrorCode = errorCode;
    }

    public bool Succeeded { get; }

    public string? ErrorCode { get; }

    public static DiagnosticDesktopActionResult Success() => new(true, null);

    public static DiagnosticDesktopActionResult Failed(string errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException(
                "The action error code cannot be blank.",
                nameof(errorCode));
        }

        return new DiagnosticDesktopActionResult(false, errorCode);
    }
}

public interface IDiagnosticClipboardService
{
    Task<DiagnosticDesktopActionResult> CopyAsync(
        string text,
        CancellationToken cancellationToken);
}

public interface IRegisteredLogFolderService
{
    Task<DiagnosticDesktopActionResult> OpenAsync(
        CancellationToken cancellationToken);
}

public interface IDiagnosticClipboardPlatform
{
    void SetText(string text);
}

public sealed class WindowsDiagnosticClipboardPlatform
    : IDiagnosticClipboardPlatform
{
    public void SetText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}

public sealed class WindowsDiagnosticClipboardService(
    IDiagnosticClipboardPlatform platform) : IDiagnosticClipboardService
{
    public Task<DiagnosticDesktopActionResult> CopyAsync(
        string text,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException(
                "Diagnostic clipboard text cannot be blank.",
                nameof(text));
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            platform.SetText(text);
            return Task.FromResult(DiagnosticDesktopActionResult.Success());
        }
        catch (Exception error) when (error is COMException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            return Task.FromResult(DiagnosticDesktopActionResult.Failed(
                "diagnostics.clipboard.copy_failed"));
        }
    }
}

public sealed class WindowsRegisteredLogFolderService(
    IAppDataPathProvider pathProvider,
    IProcessStarter processStarter) : IRegisteredLogFolderService
{
    public Task<DiagnosticDesktopActionResult> OpenAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            string logsDirectory = pathProvider.GetPaths().LogsDirectory;
            if (string.IsNullOrWhiteSpace(logsDirectory)
                || !Path.IsPathFullyQualified(logsDirectory))
            {
                return Failed();
            }

            string canonicalLogsDirectory = Path.GetFullPath(logsDirectory);
            if (!Directory.Exists(canonicalLogsDirectory))
            {
                return Failed();
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(canonicalLogsDirectory);
            processStarter.Start(startInfo);
            return Task.FromResult(DiagnosticDesktopActionResult.Success());
        }
        catch (Exception error) when (error is Win32Exception
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return Failed();
        }
    }

    private static Task<DiagnosticDesktopActionResult> Failed() =>
        Task.FromResult(DiagnosticDesktopActionResult.Failed(
            "diagnostics.logs.open_failed"));
}
