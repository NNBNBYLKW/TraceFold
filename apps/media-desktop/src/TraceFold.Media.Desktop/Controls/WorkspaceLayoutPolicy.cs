namespace TraceFold.Media.Desktop.Controls;

public enum WorkspaceLayoutMode
{
    Compact,
    Wide,
}

/// <summary>
/// Pure presentation policy for the responsive workspace: the wide/compact decision and the
/// DIP to physical-pixel conversion live here so they can be verified exactly and so the
/// controls contain no layout arithmetic of their own.
/// </summary>
public static class WorkspaceLayoutPolicy
{
    public const double WideBreakpoint = 1280d;

    public static WorkspaceLayoutMode GetMode(double widthDip)
    {
        if (!double.IsFinite(widthDip) || widthDip < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthDip));
        }

        return widthDip >= WideBreakpoint
            ? WorkspaceLayoutMode.Wide
            : WorkspaceLayoutMode.Compact;
    }

    /// <summary>
    /// Converts an effective-pixel size to the physical pixels WinUI's AppWindow expects at
    /// the given rasterization scale, so an initial window size stays the approved DIP size
    /// on scaled displays.
    /// </summary>
    public static int PhysicalPixels(double dip, double rasterizationScale)
    {
        if (!double.IsFinite(dip) || dip < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dip));
        }

        if (!double.IsFinite(rasterizationScale) || rasterizationScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rasterizationScale));
        }

        return (int)Math.Round(
            dip * rasterizationScale,
            MidpointRounding.AwayFromZero);
    }
}
