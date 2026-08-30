using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Common;

namespace TraceFold.Media.Infrastructure.Persistence;

public interface IReadOnlyDiagnosticDbContextFactory
{
    ValueTask<MediaDesktopDbContext> CreateDbContextAsync(
        CancellationToken cancellationToken = default);
}

public sealed class ReadOnlyDiagnosticDbContextFactory
    : IReadOnlyDiagnosticDbContextFactory
{
    private readonly DbContextOptions<MediaDesktopDbContext> _options;

    public ReadOnlyDiagnosticDbContextFactory(IAppDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = pathProvider.GetPaths().DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();
        _options = new DbContextOptionsBuilder<MediaDesktopDbContext>()
            .UseSqlite(connectionString)
            .Options;
    }

    public ValueTask<MediaDesktopDbContext> CreateDbContextAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new MediaDesktopDbContext(_options));
    }
}
