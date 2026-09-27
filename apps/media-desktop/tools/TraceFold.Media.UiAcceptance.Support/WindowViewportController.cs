using System.Runtime.InteropServices;
using System.Globalization;

namespace TraceFold.Media.UiAcceptance.Support;

public enum ViewportRejection
{
    None,
    WidthBelowMinimum,
    HeightBelowMinimum,
    MissingProcessId,
    UnexpectedExecutable,
    UnownedProcess,
}

public sealed record ViewportRequest(int ProcessId, int WidthPixels, int HeightPixels);

public sealed record ViewportResizeResult(
    int RequestedWidthPixels,
    int RequestedHeightPixels,
    int ActualWidthPixels,
    int ActualHeightPixels);

public interface IWindowViewportNative
{
    int GetDpi(IntPtr window);

    bool TryAdjustWindowRect(IntPtr window, int clientWidth, int clientHeight,
        int dpi, out int outerWidth, out int outerHeight);

    bool SetWindowSize(IntPtr window, int outerWidth, int outerHeight);

    bool TryGetClientSize(IntPtr window, out int width, out int height);
}

public sealed class Win32WindowViewportNative : IWindowViewportNative
{
    public int GetDpi(IntPtr window) => checked((int)GetDpiForWindow(window));

    public bool TryAdjustWindowRect(IntPtr window, int clientWidth, int clientHeight,
        int dpi, out int outerWidth, out int outerHeight)
    {
        var rect = new NativeRect { Right = clientWidth, Bottom = clientHeight };
        uint style = unchecked((uint)GetWindowLongPtr(window, -16).ToInt64());
        uint exStyle = unchecked((uint)GetWindowLongPtr(window, -20).ToInt64());
        bool adjusted = AdjustWindowRectExForDpi(ref rect, style, false, exStyle,
            checked((uint)dpi));
        outerWidth = rect.Right - rect.Left;
        outerHeight = rect.Bottom - rect.Top;
        return adjusted && outerWidth > 0 && outerHeight > 0;
    }

    public bool SetWindowSize(IntPtr window, int outerWidth, int outerHeight) =>
        SetWindowPos(window, IntPtr.Zero, 0, 0, outerWidth, outerHeight,
            0x0002 | 0x0004 | 0x0010);

    public bool TryGetClientSize(IntPtr window, out int width, out int height)
    {
        bool found = GetClientRect(window, out NativeRect rect);
        width = rect.Right - rect.Left;
        height = rect.Bottom - rect.Top;
        return found;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustWindowRectExForDpi(ref NativeRect rect,
        uint style, [MarshalAs(UnmanagedType.Bool)] bool menu, uint exStyle, uint dpi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
}

/// <summary>
/// Pure viewport arithmetic plus the ownership gate. The Win32 calls live in the command
/// handler; the decisions here are exact and testable, and they refuse anything outside the
/// approved minimum viewport, a foreign executable, a missing process id, or a process the
/// current runner did not start.
/// </summary>
public static class WindowViewportController
{
    public const int MinimumWidthDip = 1024;

    public const int MinimumHeightDip = 720;

    public const string ExpectedExecutableName = "TraceFold.Media";

    public static bool IsRecordedProcess(
        IEnumerable<string> ledgerLines,
        int processId,
        long startTicks,
        string executablePath)
    {
        ArgumentNullException.ThrowIfNull(ledgerLines);
        if (processId <= 0 || startTicks <= 0
            || string.IsNullOrWhiteSpace(executablePath)
            || !Path.IsPathFullyQualified(executablePath))
        {
            return false;
        }

        string expectedPath = Path.GetFullPath(executablePath);
        foreach (string line in ledgerLines)
        {
            string[] fields = line.Split('|', 3);
            if (fields.Length != 3
                || !int.TryParse(fields[0], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int recordedProcessId)
                || !long.TryParse(fields[1], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out long recordedStartTicks)
                || recordedProcessId != processId
                || recordedStartTicks != startTicks)
            {
                continue;
            }

            try
            {
                if (Path.IsPathFullyQualified(fields[2])
                    && string.Equals(Path.GetFullPath(fields[2]), expectedPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        return false;
    }

    public static ViewportResizeResult? Resize(
        IntPtr window,
        int widthDip,
        int heightDip,
        IWindowViewportNative native)
    {
        ArgumentNullException.ThrowIfNull(native);
        if (window == IntPtr.Zero)
        {
            return null;
        }

        int dpi = native.GetDpi(window);
        if (dpi <= 0)
        {
            return null;
        }

        int requestedWidth = ToPixels(widthDip, dpi);
        int requestedHeight = ToPixels(heightDip, dpi);
        if (!native.TryAdjustWindowRect(window, requestedWidth, requestedHeight, dpi,
                out int outerWidth, out int outerHeight)
            || !native.SetWindowSize(window, outerWidth, outerHeight)
            || !native.TryGetClientSize(window, out int actualWidth, out int actualHeight)
            || Math.Abs(actualWidth - requestedWidth) > 2
            || Math.Abs(actualHeight - requestedHeight) > 2)
        {
            return null;
        }

        return new ViewportResizeResult(requestedWidth, requestedHeight,
            actualWidth, actualHeight);
    }

    public static int ToPixels(int dip, int dpi)
    {
        if (dip < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dip));
        }

        if (dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi));
        }

        return (int)((long)dip * dpi / 96);
    }

    public static int ExpandForNonClient(int clientDip, int frameDip, int dpi)
    {
        if (frameDip < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameDip));
        }

        return ToPixels(clientDip + frameDip, dpi);
    }

    public static ViewportRejection Validate(
        int widthDip,
        int heightDip,
        int processId,
        string executableName,
        bool ownedByRunner)
    {
        if (widthDip < MinimumWidthDip)
        {
            return ViewportRejection.WidthBelowMinimum;
        }

        if (heightDip < MinimumHeightDip)
        {
            return ViewportRejection.HeightBelowMinimum;
        }

        if (processId <= 0)
        {
            return ViewportRejection.MissingProcessId;
        }

        string name = executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? executableName[..^4]
            : executableName;
        if (!string.Equals(name, ExpectedExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            return ViewportRejection.UnexpectedExecutable;
        }

        if (!ownedByRunner)
        {
            return ViewportRejection.UnownedProcess;
        }

        return ViewportRejection.None;
    }

    public static ViewportRequest? TryCreateRequest(
        int widthDip,
        int heightDip,
        int processId,
        string executableName,
        bool ownedByRunner,
        int dpi,
        int frameWidthDip,
        int frameHeightDip)
    {
        if (Validate(widthDip, heightDip, processId, executableName, ownedByRunner)
            != ViewportRejection.None)
        {
            return null;
        }

        return new ViewportRequest(
            processId,
            ExpandForNonClient(widthDip, frameWidthDip, dpi),
            ExpandForNonClient(heightDip, frameHeightDip, dpi));
    }
}
