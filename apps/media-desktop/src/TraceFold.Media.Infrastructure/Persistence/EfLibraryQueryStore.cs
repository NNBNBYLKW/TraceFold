using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfLibraryQueryStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory) : ILibraryQueryStore
{
    public async Task<IReadOnlyList<LibraryAssetDto>> SearchAsync(
        string? query,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<MediaAssetRow> rows = context.MediaAssets.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            string pattern = $"%{EscapeLike(query.Trim())}%";
            rows = rows.Where(row =>
                EF.Functions.Like(row.ActualFilename, pattern, "\\")
                || row.DisplayName != null
                    && EF.Functions.Like(row.DisplayName, pattern, "\\"));
        }

        MediaAssetRow[] materialized = await rows.ToArrayAsync(cancellationToken);
        return materialized
            .OrderByDescending(row => row.CreatedAtUtc)
            .ThenBy(row => row.ActualFilename, StringComparer.OrdinalIgnoreCase)
            .Select(row => new LibraryAssetDto(
                row.Id,
                row.ActualFilename,
                row.DisplayName ?? row.ActualFilename,
                row.MediaKind,
                row.CreatedAtUtc))
            .ToArray();
    }

    public async Task<LibraryAssetDetailsDto?> GetDetailsAsync(
        Guid assetId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        MediaAssetRow? row = await context.MediaAssets
            .AsNoTracking()
            .SingleOrDefaultAsync(asset => asset.Id == assetId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var metadataStore = new EfMetadataStore(contextFactory);
        MetadataDocument? metadata = await metadataStore.LoadAsync(
            new MetadataOwner(MetadataOwnerKind.MediaAsset, assetId),
            cancellationToken);
        if (metadata is null)
        {
            return null;
        }

        var card = new LibraryAssetDto(
            row.Id,
            row.ActualFilename,
            row.DisplayName ?? row.ActualFilename,
            row.MediaKind,
            row.CreatedAtUtc);
        return new LibraryAssetDetailsDto(card, metadata, row.Size, row.Sha256);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
