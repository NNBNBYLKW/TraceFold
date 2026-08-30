using Microsoft.Extensions.DependencyInjection;
using TraceFold.Media.Application;
using TraceFold.Media.Application.Diagnostics;
using TraceFold.Media.Infrastructure;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Diagnostics;

public sealed class SystemDiagnosticsCompositionTests
{
    [Fact]
    public void Infrastructure_registers_exactly_one_probe_per_category()
    {
        using TestDirectory root = TestDirectory.Create();
        using ServiceProvider provider = new ServiceCollection()
            .AddLogging()
            .AddMediaApplication()
            .AddMediaInfrastructure(root.Path, mediaToolRootOverride: null)
            .BuildServiceProvider();

        ISystemDiagnosticProbe[] probes =
            [.. provider.GetServices<ISystemDiagnosticProbe>()];

        Assert.Equal(5, probes.Length);
        Assert.Equal(
            Enum.GetValues<SystemDiagnosticCategory>(),
            probes.Select(probe => probe.Descriptor.Category)
                .Order()
                .ToArray());
        Assert.Equal(
            probes.Length,
            probes.Select(probe => probe.Descriptor.Id)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.NotNull(provider.GetRequiredService<ISystemDiagnosticsService>());
        Assert.NotNull(provider.GetRequiredService<IDiagnosticSummaryFormatter>());
    }
}
