using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfMetadataStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory) : IMetadataStore
{
    public async Task<MetadataDocument?> LoadAsync(
        MetadataOwner owner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        return owner.Kind switch
        {
            MetadataOwnerKind.InboxItem =>
                await LoadInboxAsync(context, owner, cancellationToken),
            MetadataOwnerKind.MediaAsset =>
                await LoadAssetAsync(context, owner, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(owner))
        };
    }

    public async Task<MetadataSaveResult> TrySaveAsync(
        MetadataOwner owner,
        MetadataSelection selection,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(selection);
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);

        TaxonomyValidationResult validation =
            await ValidateSelectionAsync(context, selection, cancellationToken);
        if (!validation.Succeeded)
        {
            return new MetadataSaveResult(false, expectedVersion, validation.ErrorCode);
        }

        DateTimeOffset updatedAtUtc = DateTimeOffset.UtcNow;
        MetadataSaveResult result = owner.Kind switch
        {
            MetadataOwnerKind.InboxItem => await SaveInboxAsync(
                context,
                owner.Id,
                selection,
                expectedVersion,
                updatedAtUtc,
                cancellationToken),
            MetadataOwnerKind.MediaAsset => await SaveAssetAsync(
                context,
                owner.Id,
                selection,
                expectedVersion,
                updatedAtUtc,
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(owner))
        };

        if (!result.Succeeded)
        {
            return result;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<MetadataDocument?> LoadInboxAsync(
        MediaDesktopDbContext context,
        MetadataOwner owner,
        CancellationToken cancellationToken)
    {
        MetadataDraftRow? draft = await context.MetadataDrafts
            .AsNoTracking()
            .Include(row => row.Series)
            .SingleOrDefaultAsync(row => row.InboxItemId == owner.Id, cancellationToken);
        if (draft is null)
        {
            return null;
        }

        TaxonomyTermRow[] authors = await context.InboxDraftAuthors
            .AsNoTracking()
            .Where(row => row.InboxItemId == owner.Id)
            .OrderBy(row => row.Ordinal)
            .Select(row => row.Author)
            .ToArrayAsync(cancellationToken);
        TaxonomyTermRow[] tags = await context.InboxDraftTags
            .AsNoTracking()
            .Where(row => row.InboxItemId == owner.Id)
            .OrderBy(row => row.Ordinal)
            .Select(row => row.Tag)
            .ToArrayAsync(cancellationToken);
        return new MetadataDocument(
            owner,
            draft.DisplayName,
            authors.Select(ToDto).ToArray(),
            draft.Series is null ? null : ToDto(draft.Series),
            tags.Select(ToDto).ToArray(),
            draft.Version);
    }

    private static async Task<MetadataDocument?> LoadAssetAsync(
        MediaDesktopDbContext context,
        MetadataOwner owner,
        CancellationToken cancellationToken)
    {
        MediaAssetRow? asset = await context.MediaAssets
            .AsNoTracking()
            .Include(row => row.Series)
            .SingleOrDefaultAsync(row => row.Id == owner.Id, cancellationToken);
        if (asset is null)
        {
            return null;
        }

        TaxonomyTermRow[] authors = await context.MediaAssetAuthors
            .AsNoTracking()
            .Where(row => row.MediaAssetId == owner.Id)
            .OrderBy(row => row.Ordinal)
            .Select(row => row.Author)
            .ToArrayAsync(cancellationToken);
        TaxonomyTermRow[] tags = await context.MediaAssetTags
            .AsNoTracking()
            .Where(row => row.MediaAssetId == owner.Id)
            .OrderBy(row => row.Ordinal)
            .Select(row => row.Tag)
            .ToArrayAsync(cancellationToken);
        return new MetadataDocument(
            owner,
            asset.DisplayName,
            authors.Select(ToDto).ToArray(),
            asset.Series is null ? null : ToDto(asset.Series),
            tags.Select(ToDto).ToArray(),
            asset.MetadataVersion);
    }

    private static async Task<MetadataSaveResult> SaveInboxAsync(
        MediaDesktopDbContext context,
        Guid inboxItemId,
        MetadataSelection selection,
        long expectedVersion,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        int updated = await context.MetadataDrafts
            .Where(row => row.InboxItemId == inboxItemId && row.Version == expectedVersion)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.DisplayName, selection.DisplayName)
                    .SetProperty(row => row.SeriesId, selection.SeriesId)
                    .SetProperty(row => row.Version, expectedVersion + 1)
                    .SetProperty(row => row.UpdatedAtUtc, updatedAtUtc),
                cancellationToken);
        if (updated == 0)
        {
            long? currentVersion = await context.MetadataDrafts
                .Where(row => row.InboxItemId == inboxItemId)
                .Select(row => (long?)row.Version)
                .SingleOrDefaultAsync(cancellationToken);
            return currentVersion.HasValue
                ? new MetadataSaveResult(false, currentVersion.Value, "metadata.version_conflict")
                : new MetadataSaveResult(false, expectedVersion, "metadata.owner_missing");
        }

        await context.InboxDraftAuthors
            .Where(row => row.InboxItemId == inboxItemId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.InboxDraftTags
            .Where(row => row.InboxItemId == inboxItemId)
            .ExecuteDeleteAsync(cancellationToken);
        context.InboxDraftAuthors.AddRange(selection.AuthorIds.Select(
            (id, index) => new InboxDraftAuthorRow
            {
                InboxItemId = inboxItemId,
                AuthorId = id,
                Ordinal = index
            }));
        context.InboxDraftTags.AddRange(selection.TagIds.Select(
            (id, index) => new InboxDraftTagRow
            {
                InboxItemId = inboxItemId,
                TagId = id,
                Ordinal = index
            }));
        return new MetadataSaveResult(true, expectedVersion + 1, null);
    }

    private static async Task<MetadataSaveResult> SaveAssetAsync(
        MediaDesktopDbContext context,
        Guid mediaAssetId,
        MetadataSelection selection,
        long expectedVersion,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        int updated = await context.MediaAssets
            .Where(row => row.Id == mediaAssetId && row.MetadataVersion == expectedVersion)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.DisplayName, selection.DisplayName)
                    .SetProperty(row => row.SeriesId, selection.SeriesId)
                    .SetProperty(row => row.MetadataVersion, expectedVersion + 1)
                    .SetProperty(row => row.UpdatedAtUtc, updatedAtUtc),
                cancellationToken);
        if (updated == 0)
        {
            long? currentVersion = await context.MediaAssets
                .Where(row => row.Id == mediaAssetId)
                .Select(row => (long?)row.MetadataVersion)
                .SingleOrDefaultAsync(cancellationToken);
            return currentVersion.HasValue
                ? new MetadataSaveResult(false, currentVersion.Value, "metadata.version_conflict")
                : new MetadataSaveResult(false, expectedVersion, "metadata.owner_missing");
        }

        await context.MediaAssetAuthors
            .Where(row => row.MediaAssetId == mediaAssetId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.MediaAssetTags
            .Where(row => row.MediaAssetId == mediaAssetId)
            .ExecuteDeleteAsync(cancellationToken);
        context.MediaAssetAuthors.AddRange(selection.AuthorIds.Select(
            (id, index) => new MediaAssetAuthorRow
            {
                MediaAssetId = mediaAssetId,
                AuthorId = id,
                Ordinal = index
            }));
        context.MediaAssetTags.AddRange(selection.TagIds.Select(
            (id, index) => new MediaAssetTagRow
            {
                MediaAssetId = mediaAssetId,
                TagId = id,
                Ordinal = index
            }));
        return new MetadataSaveResult(true, expectedVersion + 1, null);
    }

    private static async Task<TaxonomyValidationResult> ValidateSelectionAsync(
        MediaDesktopDbContext context,
        MetadataSelection selection,
        CancellationToken cancellationToken)
    {
        Guid[] requestedIds = selection.AuthorIds
            .Concat(selection.TagIds)
            .Concat(selection.SeriesId is Guid seriesId ? [seriesId] : [])
            .Distinct()
            .ToArray();
        if (requestedIds.Length == 0)
        {
            return TaxonomyValidationResult.Success;
        }

        var kinds = await context.TaxonomyTerms
            .AsNoTracking()
            .Where(row => requestedIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.Kind, cancellationToken);
        if (kinds.Count != requestedIds.Length)
        {
            return new TaxonomyValidationResult(false, "metadata.taxonomy_missing");
        }

        bool mismatch = selection.AuthorIds.Any(id => kinds[id] != "author")
            || selection.TagIds.Any(id => kinds[id] != "tag")
            || selection.SeriesId is Guid selectedSeriesId
                && kinds[selectedSeriesId] != "series";
        return mismatch
            ? new TaxonomyValidationResult(false, "metadata.taxonomy_kind_mismatch")
            : TaxonomyValidationResult.Success;
    }

    private static TaxonomyTermDto ToDto(TaxonomyTermRow row) =>
        new(
            row.Id,
            Enum.Parse<TaxonomyKind>(row.Kind, true),
            row.DisplayName,
            row.NormalizedName,
            0);
}
