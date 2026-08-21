using Microsoft.Extensions.DependencyInjection;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Infrastructure;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class DatabaseInitializationTests
{
    [Fact]
    public async Task Initialize_and_settings_round_trip_survive_a_new_service_provider()
    {
        using var temp = TestDirectory.Create();
        var folders = new LibraryFolders(
            Path.Combine(temp.Path, "selected-inbox"),
            Path.Combine(temp.Path, "selected-library"),
            Path.Combine(temp.Path, "selected-work"));

        await using (ServiceProvider first = BuildServices(temp.Path))
        {
            await first.GetRequiredService<IDatabaseInitializer>()
                .InitializeAsync(CancellationToken.None);
            await first.GetRequiredService<IDirectorySettingsStore>()
                .SaveAsync(folders, CancellationToken.None);
        }

        Assert.True(File.Exists(Path.Combine(temp.Path, "media-desktop.db")));
        Assert.False(Directory.Exists(folders.InboxDirectory));
        Assert.False(Directory.Exists(folders.LibraryDirectory));

        await using ServiceProvider second = BuildServices(temp.Path);
        LibraryFolders? loaded = await second
            .GetRequiredService<IDirectorySettingsStore>()
            .LoadAsync(CancellationToken.None);
        Assert.Equal(folders, loaded);
    }

    private static ServiceProvider BuildServices(string root) =>
        new ServiceCollection()
            .AddLogging()
            .AddMediaInfrastructure(root)
            .BuildServiceProvider(validateScopes: true);
}
