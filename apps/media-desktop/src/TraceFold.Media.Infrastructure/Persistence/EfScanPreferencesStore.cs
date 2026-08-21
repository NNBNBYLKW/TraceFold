using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Settings;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfScanPreferencesStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory)
    : IScanPreferencesStore
{
    public async Task<ScanPreferences> LoadAsync(CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        AppSettingsRow? row = await context.AppSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        return row is null ? ScanPreferences.Default : ToPreferences(row);
    }

    public async Task<ScanPreferenceStoreResult> SaveAutoScanEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        AppSettingsRow? row = await context.AppSettings
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (row is null)
        {
            return new ScanPreferenceStoreResult(
                false,
                ScanPreferences.Default,
                "settings.directories_required");
        }

        row.AutoScanEnabled = enabled;
        await context.SaveChangesAsync(cancellationToken);
        return new ScanPreferenceStoreResult(true, ToPreferences(row), null);
    }

    public async Task SaveLastScanAsync(
        DateTimeOffset completedAtUtc,
        string summary,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        AppSettingsRow? row = await context.AppSettings
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.LastScanAtUtc = completedAtUtc;
        row.LastScanSummary = summary;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static ScanPreferences ToPreferences(AppSettingsRow row) =>
        new(row.AutoScanEnabled, row.LastScanAtUtc, row.LastScanSummary);
}
