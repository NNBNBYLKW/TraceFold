using Microsoft.UI.Xaml;
using TraceFold.Media.Desktop.Shell;

namespace TraceFold.Media.Desktop;

public sealed partial class MainWindow : Window
{
    public MainWindow(ShellPage shellPage)
    {
        InitializeComponent();
        ShellRoot.Children.Add(shellPage);
        Title = "TraceFold 媒体库";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1360, 840));
        ShellRoot.MinWidth = 1100;
        ShellRoot.MinHeight = 700;
    }
}
