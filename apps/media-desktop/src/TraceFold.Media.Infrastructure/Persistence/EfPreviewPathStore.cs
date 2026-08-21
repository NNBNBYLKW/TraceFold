using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Infrastructure.Filesystem;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfPreviewPathStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory) : IPreviewPathStore
{
    public async Task<PreviewPathRecord?> FindAsync(
        MetadataOwner owner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        AppSettingsRow? settings = await context.AppSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == 1, cancellationToken);
        if (settings is null)
        {
            return null;
        }

        return owner.Kind switch
        {
            MetadataOwnerKind.InboxItem => await FindInboxAsync(
                context,
                settings.InboxDirectory,
                owner.Id,
                cancellationToken),
            MetadataOwnerKind.MediaAsset => await FindAssetAsync(
                context,
                settings.LibraryDirectory,
                owner.Id,
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(owner))
        };
    }

    private static async Task<PreviewPathRecord?> FindInboxAsync(
        MediaDesktopDbContext context,
        string inboxRoot,
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        var record = await (
                from item in context.InboxItems.AsNoTracking()
                join source in context.SourceFiles.AsNoTracking()
                    on item.Id equals source.InboxItemId
                where item.Id == inboxItemId && source.IsPrimary && source.IsPresent
                select new { item.MediaKind, source.NormalizedPath })
            .SingleOrDefaultAsync(cancellationToken);
        if (record is null)
        {
            return null;
        }

        string trustedPath = ControlledPathGuard.RequireDirectChild(
            inboxRoot,
            record.NormalizedPath);
        return new PreviewPathRecord(record.MediaKind, trustedPath);
    }

    private static async Task<PreviewPathRecord?> FindAssetAsync(
        MediaDesktopDbContext context,
        string libraryRoot,
        Guid assetId,
        CancellationToken cancellationToken)
    {
        var record = await context.MediaAssets
            .AsNoTracking()
            .Where(row => row.Id == assetId)
            .Select(row => new { row.MediaKind, row.RelativePath })
            .SingleOrDefaultAsync(cancellationToken);
        if (record is null)
        {
            return null;
        }

        string trustedPath = ControlledPathGuard.RequireDescendant(
            libraryRoot,
            Path.Combine(libraryRoot, record.RelativePath));
        return new PreviewPathRecord(record.MediaKind, trustedPath);
    }
}
