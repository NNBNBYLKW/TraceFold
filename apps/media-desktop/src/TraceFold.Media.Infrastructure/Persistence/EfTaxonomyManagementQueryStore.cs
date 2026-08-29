using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfTaxonomyManagementQueryStore : ITaxonomyManagementQueryStore
{
    private readonly IDbContextFactory<MediaDesktopDbContext> _contextFactory;

    public EfTaxonomyManagementQueryStore(
        IDbContextFactory<MediaDesktopDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<TaxonomyManagementTermDto>> QueryAsync(
        TaxonomyManagementQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using MediaDesktopDbContext context =
            await _contextFactory.CreateDbContextAsync(cancellationToken);
        string kindKey = query.Kind.ToString().ToLowerInvariant();
        IQueryable<TaxonomyTermRow> rows = context.TaxonomyTerms
            .AsNoTracking()
            .Where(row => row.Kind == kindKey);

        if (query.SearchText is not null)
        {
            string pattern = $"%{EscapeLike(query.SearchText)}%";
            rows = rows.Where(row =>
                EF.Functions.Like(row.DisplayName, pattern, "\\"));
        }

        IQueryable<Projection> projected = query.Kind switch
        {
            TaxonomyKind.Author => rows.Select(term => new Projection
            {
                Id = term.Id,
                DisplayName = term.DisplayName,
                NormalizedName = term.NormalizedName,
                UpdatedAtUtc = term.UpdatedAtUtc,
                InboxReferenceCount = context.InboxDraftAuthors.Count(
                    link => link.AuthorId == term.Id),
                LibraryReferenceCount = context.MediaAssetAuthors.Count(
                    link => link.AuthorId == term.Id),
            }),
            TaxonomyKind.Series => rows.Select(term => new Projection
            {
                Id = term.Id,
                DisplayName = term.DisplayName,
                NormalizedName = term.NormalizedName,
                UpdatedAtUtc = term.UpdatedAtUtc,
                InboxReferenceCount = context.MetadataDrafts.Count(
                    draft => draft.SeriesId == term.Id),
                LibraryReferenceCount = context.MediaAssets.Count(
                    asset => asset.SeriesId == term.Id),
            }),
            TaxonomyKind.Tag => rows.Select(term => new Projection
            {
                Id = term.Id,
                DisplayName = term.DisplayName,
                NormalizedName = term.NormalizedName,
                UpdatedAtUtc = term.UpdatedAtUtc,
                InboxReferenceCount = context.InboxDraftTags.Count(
                    link => link.TagId == term.Id),
                LibraryReferenceCount = context.MediaAssetTags.Count(
                    link => link.TagId == term.Id),
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(query)),
        };

        projected = query.ReferenceFilter switch
        {
            TaxonomyReferenceFilter.All => projected,
            TaxonomyReferenceFilter.Referenced => projected.Where(term =>
                term.InboxReferenceCount + term.LibraryReferenceCount > 0),
            TaxonomyReferenceFilter.Unused => projected.Where(term =>
                term.InboxReferenceCount + term.LibraryReferenceCount == 0),
            _ => throw new ArgumentOutOfRangeException(nameof(query)),
        };

        IOrderedQueryable<Projection> ordered =
            (query.SortField, query.SortDirection) switch
            {
                (TaxonomySortField.DisplayName, TaxonomySortDirection.Ascending) =>
                    projected.OrderBy(term => term.DisplayName),
                (TaxonomySortField.DisplayName, TaxonomySortDirection.Descending) =>
                    projected.OrderByDescending(term => term.DisplayName),
                (TaxonomySortField.ReferenceCount, TaxonomySortDirection.Ascending) =>
                    projected
                        .OrderBy(term =>
                            term.InboxReferenceCount + term.LibraryReferenceCount)
                        .ThenBy(term => term.DisplayName),
                (TaxonomySortField.ReferenceCount, TaxonomySortDirection.Descending) =>
                    projected
                        .OrderByDescending(term =>
                            term.InboxReferenceCount + term.LibraryReferenceCount)
                        .ThenBy(term => term.DisplayName),
                _ => throw new ArgumentOutOfRangeException(nameof(query)),
            };

        Projection[] materialized = await ordered
            .ThenBy(term => term.Id)
            .ToArrayAsync(cancellationToken);
        return materialized
            .Select(term => new TaxonomyManagementTermDto(
                term.Id,
                query.Kind,
                term.DisplayName,
                term.NormalizedName,
                term.UpdatedAtUtc,
                term.InboxReferenceCount,
                term.LibraryReferenceCount))
            .ToArray();
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private sealed class Projection
    {
        public Guid Id { get; init; }

        public required string DisplayName { get; init; }

        public required string NormalizedName { get; init; }

        public DateTimeOffset UpdatedAtUtc { get; init; }

        public int InboxReferenceCount { get; init; }

        public int LibraryReferenceCount { get; init; }
    }
}
