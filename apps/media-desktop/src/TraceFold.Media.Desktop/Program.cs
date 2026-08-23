using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using TraceFold.Media.Desktop.Diagnostics;
using TraceFold.Media.Desktop.Lifecycle;

namespace TraceFold.Media.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Stage3PackageProbeOptionResult probe =
            Stage3PackageProbeOptions.Parse(args);
        if (probe.State == Stage3PackageProbeOptionState.Invalid)
        {
            return 2;
        }

        if (probe.State == Stage3PackageProbeOptionState.Ready)
        {
            return Stage3PackageProbe.RunAsync(probe.Options!)
                .GetAwaiter()
                .GetResult();
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        AppInstance current = AppInstance.GetCurrent();
        AppActivationArguments activationArguments =
            current.GetActivatedEventArgs();
        AppInstance primary =
            AppInstance.FindOrRegisterForKey("TraceFold.MediaDesktop");

        if (!primary.IsCurrent)
        {
            return SingleInstanceCoordinator.RedirectToPrimary(
                primary,
                activationArguments);
        }

        Microsoft.UI.Xaml.Application.Start(initializationParameters =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = initializationParameters;
            var app = new App(primary);
            GC.KeepAlive(app);
        });
        return 0;
    }
}
