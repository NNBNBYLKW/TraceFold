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
