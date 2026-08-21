using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TraceFold.Media.Application.Common;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Infrastructure.Filesystem;
using TraceFold.Media.Infrastructure.Paths;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Settings;

namespace TraceFold.Media.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMediaInfrastructure(
        this IServiceCollection services,
        string? dataRootOverride = null)
    {
        var pathProvider = new LocalAppDataPathProvider(dataRootOverride);
        AppDataPaths paths = pathProvider.GetPaths();

        services.AddSingleton<IAppDataPathProvider>(pathProvider);
        services.AddDbContextFactory<MediaDesktopDbContext>(options =>
            options.UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = paths.DatabasePath,
                Pooling = false
            }.ToString()));
        services.AddSingleton<IDirectorySettingsStore, EfDirectorySettingsStore>();
        services.AddSingleton<IDirectoryProbe, LocalDirectoryProbe>();
        services.AddSingleton<IInboxFileCatalog, LocalInboxFileCatalog>();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        return services;
    }
}
