using Serilog;
using TraceFold.Media.Application.Common;

namespace TraceFold.Media.Infrastructure.Logging;

public static class LoggingBootstrapper
{
    public static Serilog.Core.Logger CreateLogger(AppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Directory.CreateDirectory(paths.LogsDirectory);

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("Application", "TraceFold.MediaDesktop")
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "tracefold-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                flushToDiskInterval: TimeSpan.FromSeconds(1))
            .CreateLogger();
    }
}
