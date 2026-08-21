using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Serilog;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Startup;
using TraceFold.Media.Desktop.Composition;
using TraceFold.Media.Desktop.Services;
using TraceFold.Media.Infrastructure.Paths;

namespace TraceFold.Media.Desktop;

public sealed partial class App : Microsoft.UI.Xaml.Application
{
    private IHost? _host;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            AppDataPaths paths = new LocalAppDataPathProvider().GetPaths();
            _host = DesktopCompositionRoot.BuildHost(paths);
            await _host.StartAsync();

            MainWindow window = _host.Services.GetRequiredService<MainWindow>();
            _host.Services.GetRequiredService<WindowContext>().SetMainWindow(window);
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
}
