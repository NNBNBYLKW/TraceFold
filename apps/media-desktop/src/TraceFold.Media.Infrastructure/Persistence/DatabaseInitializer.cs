using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Common;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class DatabaseInitializer(
    IAppDataPathProvider pathProvider,
    IDbContextFactory<MediaDesktopDbContext> contextFactory)
    : IDatabaseInitializer, IDisposable, IAsyncDisposable
{
    private readonly SemaphoreSlim initializeGate = new(1, 1);
    private SqliteConnection? walKeeper;
    private int disposeStarted;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await initializeGate.WaitAsync(cancellationToken);
        try
        {
            AppDataPaths paths = pathProvider.GetPaths();
            Directory.CreateDirectory(paths.RootDirectory);
            Directory.CreateDirectory(paths.LogsDirectory);
            Directory.CreateDirectory(paths.DefaultWorkDirectory);

            await using MediaDesktopDbContext context =
                await contextFactory.CreateDbContextAsync(cancellationToken);
            await context.Database.MigrateAsync(cancellationToken);

            if (walKeeper is null)
            {
                // Keep the WAL open until the service provider shuts down. With
                // pooling disabled, closing every short-lived context can make
                // SQLite remove and recreate the WAL between concurrent work.
                var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = paths.DatabasePath,
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false
                }.ToString());
                try
                {
                    await connection.OpenAsync(cancellationToken);
                    await using var command = connection.CreateCommand();
                    command.CommandText = "SELECT name FROM sqlite_master LIMIT 1";
                    await command.ExecuteScalarAsync(cancellationToken);
                    walKeeper = connection;
                }
                catch
                {
                    await connection.DisposeAsync();
                    throw;
                }
            }
        }
        finally
        {
            initializeGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
        {
            return;
        }

        walKeeper?.Dispose();
        initializeGate.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
        {
            return;
        }

        if (walKeeper is not null)
        {
            await walKeeper.DisposeAsync();
        }

        initializeGate.Dispose();
    }
}
