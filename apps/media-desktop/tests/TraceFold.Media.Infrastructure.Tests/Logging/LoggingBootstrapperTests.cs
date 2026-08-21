using TraceFold.Media.Application.Common;
using TraceFold.Media.Infrastructure.Logging;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Logging;

public sealed class LoggingBootstrapperTests
{
    [Fact]
    public void CreateLogger_writes_and_flushes_a_rolling_file_under_app_data()
    {
        using var temp = TestDirectory.Create();
        var paths = new AppDataPaths(
            temp.Path,
            Path.Combine(temp.Path, "media-desktop.db"),
            Path.Combine(temp.Path, "logs"),
            Path.Combine(temp.Path, "work"));

        using (Serilog.Core.Logger logger = LoggingBootstrapper.CreateLogger(paths))
        {
            logger.Information("stage-one-log-probe");
        }

        string[] logs = Directory.GetFiles(paths.LogsDirectory, "tracefold-*.log");
        Assert.Single(logs);
        Assert.Contains(
            "stage-one-log-probe",
            File.ReadAllText(logs[0]),
            StringComparison.Ordinal);
    }
}
