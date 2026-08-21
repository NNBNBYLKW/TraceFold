using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Serilog;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Startup;
using TraceFold.Media.Desktop.Composition;
using TraceFold.Media.Desktop.Lifecycle;
using TraceFold.Media.Desktop.Services;
using TraceFold.Media.Infrastructure.Paths;

namespace TraceFold.Media.Desktop;

public sealed partial class App : Microsoft.UI.Xaml.Application
{
    private readonly AppInstance _primaryInstance;
    private IHost? _host;
    private WindowContext? _windowContext;
    private AppShutdownCoordinator? _shutdownCoordinator;
    private int _shutdownStarted;

    public App()
        : this(AppInstance.GetCurrent())
    {
    }

    public App(AppInstance primaryInstance)
    {
        _primaryInstance = primaryInstance;
        InitializeComponent();
        _primaryInstance.Activated += OnActivated;
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            AppDataPaths paths = new LocalAppDataPathProvider().GetPaths();
            _host = DesktopCompositionRoot.BuildHost(paths);
            await _host.StartAsync();

            MainWindow window = _host.Services.GetRequiredService<MainWindow>();
            _windowContext = _host.Services.GetRequiredService<WindowContext>();
            _windowContext.SetMainWindow(window);
            _shutdownCoordinator = new AppShutdownCoordinator(
                new DesktopHostStopper(_host),
                _host.Services.GetRequiredService<
                    ILogger<AppShutdownCoordinator>>());
            window.Closed += OnMainWindowClosed;
            window.Activate();

            await _host.Services.GetRequiredService<IStartupCoordinator>()
                .InitializeAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "DesktopLaunchFailed");
            Log.CloseAndFlush();
            throw;
        }
    }

    private void OnActivated(object? sender, AppActivationArguments args)
    {
        MainWindow? existingWindow = _windowContext?.MainWindow;
        if (existingWindow is null)
        {
            Log.Warning("DesktopActivationArrivedBeforeWindowReady");
            return;
        }

        if (!existingWindow.DispatcherQueue.TryEnqueue(() =>
            {
                MainWindow window = _windowContext?.MainWindow
                    ?? throw new InvalidOperationException(
                        "Main window is not available.");
                if (window.AppWindow.Presenter is OverlappedPresenter presenter
                    && presenter.State == OverlappedPresenterState.Minimized)
                {
                    presenter.Restore();
                }

                window.Activate();
            }))
        {
            Log.Warning("DesktopActivationDispatchFailed");
        }
    }

    private async void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        _primaryInstance.Activated -= OnActivated;
        try
        {
            if (_shutdownCoordinator is not null)
            {
                await _shutdownCoordinator.StopAsync(CancellationToken.None);
            }
        }
        finally
        {
            try
            {
                AppInstance.GetCurrent().UnregisterKey();
            }
            catch (Exception exception)
            {
                Log.Error(exception, "DesktopInstanceKeyUnregisterFailed");
            }

            Log.CloseAndFlush();
        }
    }

    private static void OnUnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        Log.Error(args.Exception, "DesktopUnhandledUiException");
        args.Handled = true;
    }

    private static void OnDomainUnhandledException(
        object sender,
        System.UnhandledExceptionEventArgs args)
    {
        Log.Error(
            args.ExceptionObject as Exception,
            "DesktopUnhandledDomainException IsTerminating={IsTerminating}",
            args.IsTerminating);
    }

    private static void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs args)
    {
        Log.Error(args.Exception, "DesktopUnobservedTaskException");
        args.SetObserved();
    }
}
