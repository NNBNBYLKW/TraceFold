using Microsoft.UI.Xaml;
using TraceFold.Media.Desktop.Controls;
using TraceFold.Media.Desktop.Shell;

namespace TraceFold.Media.Desktop;

public sealed partial class MainWindow : Window
{
    // The approved initial size is expressed in effective pixels (DIP). WinUI's AppWindow
    // takes physical pixels, so the resize converts with the window's own rasterization
    // scale and a scaled display still opens at 1280x800 DIP. The minimum viewport lives in
    // MainWindow.xaml so it is applied before the window is shown.
    private const double InitialWidthDip = 1280d;
    private const double InitialHeightDip = 800d;

    public MainWindow(ShellPage shellPage)
    {
        InitializeComponent();
        ShellRoot.Children.Add(shellPage);
        Title = "TraceFold 媒体库";
        ResizeToApprovedInitialSize();
    }

    private void ResizeToApprovedInitialSize()
    {
        double scale = RasterizationScale();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            WorkspaceLayoutPolicy.PhysicalPixels(InitialWidthDip, scale),
            WorkspaceLayoutPolicy.PhysicalPixels(InitialHeightDip, scale)));
    }

    private double RasterizationScale()
    {
        uint dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        return dpi == 0 ? 1d : dpi / 96d;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);
}
