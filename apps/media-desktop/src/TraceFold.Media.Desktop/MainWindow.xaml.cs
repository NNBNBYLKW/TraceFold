using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using TraceFold.Media.Desktop.Controls;
using TraceFold.Media.Desktop.Shell;

namespace TraceFold.Media.Desktop;

public sealed partial class MainWindow : Window
{
    // The approved initial size is expressed in effective pixels (DIP). WinUI's AppWindow
    // takes physical pixels, so the resize converts with the window's own rasterization
    // scale and a scaled display still opens with a 1280x800 DIP client area.
    private const double InitialWidthDip = 1280d;
    private const double InitialHeightDip = 800d;

    public MainWindow(ShellPage shellPage)
    {
        InitializeComponent();
        ShellRoot.Children.Add(shellPage);
        Title = "TraceFold 媒体库";
        ResizeToApprovedInitialSize();
        SizeChanged += (_, _) => ApplyMinimumClientSize();
    }

    private void ResizeToApprovedInitialSize()
    {
        double scale = RasterizationScale();
        AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(
            WorkspaceLayoutPolicy.PhysicalPixels(InitialWidthDip, scale),
            WorkspaceLayoutPolicy.PhysicalPixels(InitialHeightDip, scale)));
        ApplyMinimumClientSize();
    }

    private void ApplyMinimumClientSize()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        double scale = RasterizationScale();
        int frameWidth = Math.Max(0, AppWindow.Size.Width - AppWindow.ClientSize.Width);
        int frameHeight = Math.Max(0, AppWindow.Size.Height - AppWindow.ClientSize.Height);
        int minimumWidth = WorkspaceLayoutPolicy.PhysicalPixels(
            ResourceDip("TraceFoldMinViewportWidth"), scale) + frameWidth;
        int minimumHeight = WorkspaceLayoutPolicy.PhysicalPixels(
            ResourceDip("TraceFoldMinViewportHeight"), scale) + frameHeight;
        if (presenter.PreferredMinimumWidth != minimumWidth)
        {
            presenter.PreferredMinimumWidth = minimumWidth;
        }

        if (presenter.PreferredMinimumHeight != minimumHeight)
        {
            presenter.PreferredMinimumHeight = minimumHeight;
        }
    }

    private static double ResourceDip(string key) =>
        Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key, out object? value)
            && value is double dip
            ? dip
            : throw new InvalidOperationException("Missing window size resource: " + key);

    private double RasterizationScale()
    {
        uint dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        return dpi == 0 ? 1d : dpi / 96d;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);
}
