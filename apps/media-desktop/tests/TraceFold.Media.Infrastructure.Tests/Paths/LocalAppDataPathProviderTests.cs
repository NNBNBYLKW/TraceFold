using TraceFold.Media.Application.Common;
using TraceFold.Media.Infrastructure.Paths;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Paths;

public sealed class LocalAppDataPathProviderTests
{
    [Fact]
    public void GetPaths_keeps_every_app_owned_path_under_the_override_root()
    {
        using var temp = TestDirectory.Create();

        AppDataPaths paths = new LocalAppDataPathProvider(temp.Path).GetPaths();

        Assert.Equal(Path.GetFullPath(temp.Path), paths.RootDirectory);
        Assert.Equal(
            Path.Combine(paths.RootDirectory, "media-desktop.db"),
            paths.DatabasePath);
        Assert.Equal(
            Path.Combine(paths.RootDirectory, "logs"),
            paths.LogsDirectory);
        Assert.Equal(
            Path.Combine(paths.RootDirectory, "work"),
            paths.DefaultWorkDirectory);
    }

    [Fact]
    public void GetPaths_rejects_a_relative_override()
    {
        var provider = new LocalAppDataPathProvider("relative-data-root");

        Assert.Throws<ArgumentException>(() => provider.GetPaths());
    }
}
