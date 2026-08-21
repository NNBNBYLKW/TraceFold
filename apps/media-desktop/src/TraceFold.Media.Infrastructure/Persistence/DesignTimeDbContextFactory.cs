using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Data.Sqlite;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<MediaDesktopDbContext>
{
    public MediaDesktopDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MediaDesktopDbContext>();
        options.UseSqlite(new SqliteConnectionStringBuilder
        {
            DataSource = "media-desktop.design.db",
            Pooling = false
        }.ToString());
        return new MediaDesktopDbContext(options.Options);
    }
}
