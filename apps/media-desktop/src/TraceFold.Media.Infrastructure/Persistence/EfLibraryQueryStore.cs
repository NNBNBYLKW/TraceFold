using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfLibraryQueryStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory) : ILibraryQueryStore
{
    public async Task<IReadOnlyList<LibraryAssetDto>> SearchAsync(
        LibraryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<MediaAssetRow> rows = context.MediaAssets.AsNoTracking();
        if (query.SearchText is not null)
        {
            string pattern = $"%{EscapeLike(query.SearchText)}%";
            rows = rows.Where(row =>
                EF.Functions.Like(row.ActualFilename, pattern, "\\")
                || row.DisplayName != null
                    && EF.Functions.Like(row.DisplayName, pattern, "\\"));
        }

        rows = query.Media switch
        {
            LibraryMediaFilter.All => rows,
            LibraryMediaFilter.Image => rows.Where(
                row => row.MediaKind == MediaKind.Image),
            LibraryMediaFilter.Video => rows.Where(
                row => row.MediaKind == MediaKind.Video),
            _ => throw new ArgumentOutOfRangeException(nameof(query))
        };

        foreach (Guid authorId in query.AuthorIds)
        {
            Guid captured = authorId;
            rows = rows.Where(asset =>
                asset.Authors.Any(link => link.AuthorId == captured));
        }

        foreach (Guid tagId in query.TagIds)
        {
            Guid captured = tagId;
            rows = rows.Where(asset =>
                asset.Tags.Any(link => link.TagId == captured));
        }

        if (query.SeriesId.HasValue)
        {
            Guid seriesId = query.SeriesId.Value;
            rows = rows.Where(asset => asset.SeriesId == seriesId);
        }

        if (query.MissingAuthors)
        {
            rows = rows.Where(asset => !asset.Authors.Any());
        }

        if (query.MissingSeries)
        {
            rows = rows.Where(asset => asset.SeriesId == null);
        }

        if (query.MissingTags)
        {
            rows = rows.Where(asset => !asset.Tags.Any());
        }

        rows = query.DisplayName switch
        {
            LibraryDisplayNameFilter.All => rows,
            LibraryDisplayNameFilter.Customized => rows.Where(
                asset => asset.DisplayName != null),
            LibraryDisplayNameFilter.ActualFilename => rows.Where(
                asset => asset.DisplayName == null),
            _ => throw new ArgumentOutOfRangeException(nameof(query))
        };

        IOrderedQueryable<MediaAssetRow> ordered = ApplyPrimarySort(rows, query);
        ordered = ordered
            .ThenBy(row => EF.Functions.Collate(row.ActualFilename, "NOCASE"))
            .ThenBy(row => row.Id);

        return await ordered
            .Select(row => new LibraryAssetDto(
                row.Id,
                row.ActualFilename,
                row.DisplayName ?? row.ActualFilename,
                row.MediaKind,
                row.CreatedAtUtc,
                row.MetadataVersion,
                row.DisplayName != null))
            .ToArrayAsync(cancellationToken);
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
            row.CreatedAtUtc,
            row.MetadataVersion,
            row.DisplayName is not null);
        return new LibraryAssetDetailsDto(card, metadata, row.Size, row.Sha256);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static IOrderedQueryable<MediaAssetRow> ApplyPrimarySort(
        IQueryable<MediaAssetRow> rows,
        LibraryQuery query) =>
        (query.SortField, query.SortDirection) switch
        {
            (LibrarySortField.CreatedAt, LibrarySortDirection.Ascending) =>
                rows.OrderBy(row => row.CreatedAtUtc.ToString()),
            (LibrarySortField.CreatedAt, LibrarySortDirection.Descending) =>
                rows.OrderByDescending(row => row.CreatedAtUtc.ToString()),
            (LibrarySortField.DisplayName, LibrarySortDirection.Ascending) =>
                rows.OrderBy(row => EF.Functions.Collate(
                    row.DisplayName ?? row.ActualFilename,
                    "NOCASE")),
            (LibrarySortField.DisplayName, LibrarySortDirection.Descending) =>
                rows.OrderByDescending(row => EF.Functions.Collate(
                    row.DisplayName ?? row.ActualFilename,
                    "NOCASE")),
            (LibrarySortField.ActualFilename, LibrarySortDirection.Ascending) =>
                rows.OrderBy(row => EF.Functions.Collate(
                    row.ActualFilename,
                    "NOCASE")),
            (LibrarySortField.ActualFilename, LibrarySortDirection.Descending) =>
                rows.OrderByDescending(row => EF.Functions.Collate(
                    row.ActualFilename,
                    "NOCASE")),
            (LibrarySortField.MediaKind, LibrarySortDirection.Ascending) =>
                rows.OrderBy(row => row.MediaKind),
            (LibrarySortField.MediaKind, LibrarySortDirection.Descending) =>
                rows.OrderByDescending(row => row.MediaKind),
            _ => throw new ArgumentOutOfRangeException(nameof(query))
        };
}
