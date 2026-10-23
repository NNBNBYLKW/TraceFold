using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Serilog;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Ingest;
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
    private long _activationObservationOrdinal;
    private ActivationObservedWindow? _activationObservedWindow;

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
            _shutdownCoordinator = new AppShutdownCoordinator(
                _host.Services.GetRequiredService<IMetadataEditorRegistry>(),
                _host.Services.GetRequiredService<IMediaOperationCoordinator>(),
                new DesktopHostStopper(_host),
                _host.Services.GetRequiredService<
                    ILogger<AppShutdownCoordinator>>());
            await _host.StartAsync();

            MainWindow window = _host.Services.GetRequiredService<MainWindow>();
            _windowContext = _host.Services.GetRequiredService<WindowContext>();
            _windowContext.SetMainWindow(window);
            window.Closed += OnMainWindowClosed;
            window.Activate();
            TryObserveActivation("WindowReady", 0, true, uiWindow: window);

            await _host.Services.GetRequiredService<IStartupCoordinator>()
                .InitializeAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "DesktopLaunchFailed");
            await ShutdownAsync();
            Environment.Exit(1);
        }
    }

    private void OnActivated(object? sender, AppActivationArguments args)
    {
        // Receiver-local ordinal only; sender/args do not establish a secondary process identity.
        long activationOrdinal = Interlocked.Increment(ref _activationObservationOrdinal);
        MainWindow? existingWindow = _windowContext?.MainWindow;
        TryObserveActivation("Arrived", activationOrdinal, existingWindow is not null);
        if (existingWindow is null)
        {
            Log.Warning("DesktopActivationArrivedBeforeWindowReady");
            return;
        }

        bool accepted;
        TryObserveActivation("EnqueueBegin", activationOrdinal, true);
        try
        {
            accepted = existingWindow.DispatcherQueue.TryEnqueue(() =>
            {
                // Only this UI delegate may ask a WinUI Window for its fresh HWND.
                TryObserveActivation("UiEntered", activationOrdinal, _windowContext?.MainWindow is not null);
                MainWindow? observedWindow = null;
                try
                {
                    MainWindow window = _windowContext?.MainWindow
                        ?? throw new InvalidOperationException(
                            "Main window is not available.");
                    observedWindow = window;
                    if (window.AppWindow.Presenter is OverlappedPresenter presenter
                        && presenter.State == OverlappedPresenterState.Minimized)
                    {
                        TryObserveActivation("RestoreBegin", activationOrdinal, true, uiWindow: window);
                        presenter.Restore();
                        TryObserveActivation("RestoreReturned", activationOrdinal, true, uiWindow: window);
                    }

                    TryObserveActivation("ActivateBegin", activationOrdinal, true, uiWindow: window);
                    window.Activate();
                    TryObserveActivation("ActivateReturned", activationOrdinal, true, uiWindow: window);
                    TryObserveActivation("UiCompleted", activationOrdinal, true, uiWindow: window);
                }
                catch (Exception exception)
                {
                    TryObserveActivation("UiException", activationOrdinal, observedWindow is not null,
                        operationException: exception, uiWindow: observedWindow);
                    throw;
                }
                finally
                {
                    TryObserveActivation("UiExited", activationOrdinal, observedWindow is not null,
                        uiWindow: observedWindow);
                }
            });
        }
        catch (Exception exception)
        {
            TryObserveActivation("EnqueueException", activationOrdinal, true,
                operationException: exception);
            throw;
        }
        TryObserveActivation("EnqueueReturned", activationOrdinal, true, accepted);
        if (!accepted)
        {
            Log.Warning("DesktopActivationDispatchFailed");
        }
    }

    private sealed record ActivationObservedWindow(long Hwnd, DateTimeOffset CapturedUtc);

    private void TryObserveActivation(
        string stage,
        long activationOrdinal,
        bool windowObjectReady,
        bool? enqueueAccepted = null,
        Exception? operationException = null,
        MainWindow? uiWindow = null)
    {
        try
        {
            DateTimeOffset observationBeginUtc = DateTimeOffset.UtcNow;
            ActivationObservedWindow? primary = Volatile.Read(ref _activationObservedWindow);
            var source = primary is null
                ? ActivationNativeObservation.PrimaryHandleSource.Unknown
                : ActivationNativeObservation.PrimaryHandleSource.CachedUi;
            string? windowReadErrorType = null;
            int? windowReadErrorHResult = null;
            if (uiWindow is not null)
            {
                try
                {
                    long hwnd = WinRT.Interop.WindowNative.GetWindowHandle(uiWindow).ToInt64();
                    primary = hwnd > 0 ? new ActivationObservedWindow(hwnd, DateTimeOffset.UtcNow) : null;
                    source = primary is null
                        ? ActivationNativeObservation.PrimaryHandleSource.Unknown
                        : ActivationNativeObservation.PrimaryHandleSource.FreshUi;
                    Volatile.Write(ref _activationObservedWindow, primary);
                }
                catch (Exception exception)
                {
                    primary = null;
                    source = ActivationNativeObservation.PrimaryHandleSource.Unknown;
                    windowReadErrorType = exception.GetType().FullName;
                    windowReadErrorHResult = exception.HResult;
                }
            }

            var observation = ActivationNativeObservation.Capture(
                primary?.Hwnd, source, primary?.CapturedUtc);
            DateTimeOffset observationEndUtc = DateTimeOffset.UtcNow;
            // No args.Data, sender PID, titles, paths, tokens, or exception objects/messages/stacks.
            // Existing logger may not yet be initialized: absence then remains Unknown, not no event.
            Log.Information(
                "DesktopActivationObservation Stage={Stage} ActivationOrdinal={ActivationOrdinal} " +
                "ObservationBeginUtc={ObservationBeginUtc:o} ObservationEndUtc={ObservationEndUtc:o} " +
                "WindowObjectReady={WindowObjectReady} EnqueueAccepted={EnqueueAccepted} " +
                "Observation={@Observation} WindowReadErrorType={WindowReadErrorType} " +
                "WindowReadErrorHResult={WindowReadErrorHResult} " +
                "OperationExceptionType={OperationExceptionType} OperationExceptionHResult={OperationExceptionHResult}",
                stage, activationOrdinal, observationBeginUtc, observationEndUtc,
                windowObjectReady, enqueueAccepted, observation,
                windowReadErrorType, windowReadErrorHResult,
                operationException?.GetType().FullName, operationException?.HResult);
        }
        catch
        {
            // Observation (including the existing sink) must not change activation or exception flow.
        }
    }

    private async void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        await ShutdownAsync();
    }

    private async Task ShutdownAsync()
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
            else
            {
                _host?.Dispose();
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
