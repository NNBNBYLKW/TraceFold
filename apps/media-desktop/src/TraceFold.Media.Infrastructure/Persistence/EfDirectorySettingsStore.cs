using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Settings;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfDirectorySettingsStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory)
    : IDirectorySettingsStore
{
    public async Task<LibraryFolders?> LoadAsync(
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        AppSettingsRow? row = await context.AppSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        return row is null
            ? null
            : new LibraryFolders(
                row.InboxDirectory,
                row.LibraryDirectory,
                row.WorkDirectory);
    }

    public async Task SaveAsync(
        LibraryFolders folders,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        AppSettingsRow? row = await context.AppSettings.FindAsync(
            [1],
            cancellationToken);
        if (row is null)
        {
            row = new AppSettingsRow
            {
                InboxDirectory = folders.InboxDirectory,
                LibraryDirectory = folders.LibraryDirectory,
                WorkDirectory = folders.WorkDirectory
            };
            context.AppSettings.Add(row);
        }
        else
        {
            row.InboxDirectory = folders.InboxDirectory;
            row.LibraryDirectory = folders.LibraryDirectory;
            row.WorkDirectory = folders.WorkDirectory;
        }

        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }
}
