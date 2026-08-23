using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Desktop.Composition;
using TraceFold.Media.Infrastructure.Workers;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Composition;

public sealed class DesktopCompositionRootTests
{
    [Fact]
    public void Composition_has_one_processor_and_one_processing_worker()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"tracefold-composition-{Guid.NewGuid():N}");
        var paths = new AppDataPaths(
            root,
            Path.Combine(root, "media-desktop.db"),
            Path.Combine(root, "logs"),
            Path.Combine(root, "work"));

        try
        {
            using IHost host = DesktopCompositionRoot.BuildHost(paths);

            IMediaProcessor processor = Assert.Single(
                host.Services.GetServices<IMediaProcessor>());
            Assert.IsType<Mp4M4aMergeProcessor>(processor);
            Assert.Single(
                host.Services.GetServices<IHostedService>(),
                service => service is ProcessingQueueWorker);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
