using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Common;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class DatabaseInitializer(
    IAppDataPathProvider pathProvider,
    IDbContextFactory<MediaDesktopDbContext> contextFactory)
    : IDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        AppDataPaths paths = pathProvider.GetPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        Directory.CreateDirectory(paths.LogsDirectory);
        Directory.CreateDirectory(paths.DefaultWorkDirectory);

        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
    }
}
