namespace TraceFold.Media.Desktop.Services;

public sealed class WindowContext
{
    public MainWindow? MainWindow { get; private set; }

    public void SetMainWindow(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        MainWindow = window;
    }

    public nint GetWindowHandle() => MainWindow is null
        ? throw new InvalidOperationException("Main window is not available.")
        : WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
}
