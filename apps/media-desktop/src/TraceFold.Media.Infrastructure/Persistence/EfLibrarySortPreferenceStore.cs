using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Library;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfLibrarySortPreferenceStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory)
    : ILibrarySortPreferenceStore
{
    public async Task<PersistedLibrarySortPreference> LoadAsync(
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        PersistedLibrarySortPreference? preference = await context.AppSettings
            .AsNoTracking()
            .Where(row => row.Id == 1)
            .Select(row => new PersistedLibrarySortPreference(
                row.LibrarySortField,
                row.LibrarySortDescending))
            .SingleOrDefaultAsync(cancellationToken);

        return preference
            ?? new PersistedLibrarySortPreference("created_at", true);
    }

    public async Task<string?> SaveAsync(
        LibrarySortPreference preference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preference);
        string field = preference.Field switch
        {
            LibrarySortField.CreatedAt => "created_at",
            LibrarySortField.DisplayName => "display_name",
            LibrarySortField.ActualFilename => "actual_filename",
            LibrarySortField.MediaKind => "media_kind",
            _ => throw new ArgumentOutOfRangeException(nameof(preference))
        };
        bool descending = preference.Direction switch
        {
            LibrarySortDirection.Ascending => false,
            LibrarySortDirection.Descending => true,
            _ => throw new ArgumentOutOfRangeException(nameof(preference))
        };

        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        AppSettingsRow? row = await context.AppSettings
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (row is null)
        {
            return "settings.directories_required";
        }

        row.LibrarySortField = field;
        row.LibrarySortDescending = descending;
        await context.SaveChangesAsync(cancellationToken);
        return null;
    }
}
