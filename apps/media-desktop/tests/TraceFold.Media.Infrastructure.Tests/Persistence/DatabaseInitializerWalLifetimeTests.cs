using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class DatabaseInitializerWalLifetimeTests
{
    [Fact]
    public async Task Initialized_database_keeps_wal_open_across_short_lived_contexts()
    {
        using TestDirectory root = TestDirectory.Create();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediaInfrastructure(Path.Combine(root.Path, "data"));
        using ServiceProvider provider = services.BuildServiceProvider();

        IDatabaseInitializer initializer = provider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync(TestContext.Current.CancellationToken);
        string databasePath = provider.GetRequiredService<IAppDataPathProvider>()
            .GetPaths().DatabasePath;

        IDbContextFactory<MediaDesktopDbContext> factory =
            provider.GetRequiredService<IDbContextFactory<MediaDesktopDbContext>>();
        await using (MediaDesktopDbContext context =
            await factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            Assert.True(await context.Database.CanConnectAsync(
                TestContext.Current.CancellationToken));
        }

        Assert.True(File.Exists(databasePath + "-wal"));
        Assert.True(File.Exists(databasePath + "-shm"));
    }
}
