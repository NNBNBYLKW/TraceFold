using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfTaxonomyStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory)
    : ITaxonomyStore, ITaxonomyLookup
{
    public async Task<IReadOnlyList<TaxonomyTermDto>> SearchAsync(
        TaxonomyKind kind,
        string? query,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        string kindKey = KindKey(kind);
        IQueryable<TaxonomyTermRow> rows = context.TaxonomyTerms
            .AsNoTracking()
            .Where(row => row.Kind == kindKey);
        if (!string.IsNullOrWhiteSpace(query))
        {
            string pattern = $"%{EscapeLike(query.Trim())}%";
            rows = rows.Where(row =>
                EF.Functions.Like(row.DisplayName, pattern, "\\"));
        }

        TaxonomyTermRow[] materialized = await rows
            .OrderBy(row => row.DisplayName)
            .ToArrayAsync(cancellationToken);
        var result = new List<TaxonomyTermDto>(materialized.Length);
        foreach (TaxonomyTermRow row in materialized)
        {
            result.Add(ToDto(
                row,
                await CountReferencesAsync(context, row.Id, cancellationToken)));
        }

        return result;
    }

    public async Task<TaxonomyTermDto> CreateOrGetAsync(
        TaxonomyKind kind,
        TaxonomyName name,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        string kindKey = KindKey(kind);
        TaxonomyTermRow? existing = await context.TaxonomyTerms
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.Kind == kindKey && row.NormalizedName == name.Normalized,
                cancellationToken);
        if (existing is not null)
        {
            return ToDto(
                existing,
                await CountReferencesAsync(context, existing.Id, cancellationToken));
        }

        var created = new TaxonomyTermRow
        {
            Id = Guid.NewGuid(),
            Kind = kindKey,
            DisplayName = name.Display,
            NormalizedName = name.Normalized,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        context.TaxonomyTerms.Add(created);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return ToDto(created, 0);
        }
        catch (DbUpdateException error) when (IsUniqueConstraint(error))
        {
            context.ChangeTracker.Clear();
            TaxonomyTermRow raced = await context.TaxonomyTerms
                .AsNoTracking()
                .SingleAsync(
                    row => row.Kind == kindKey && row.NormalizedName == name.Normalized,
                    cancellationToken);
            return ToDto(
                raced,
                await CountReferencesAsync(context, raced.Id, cancellationToken));
        }
    }

    public async Task<TaxonomyMutationResult> RenameAsync(
        Guid id,
        TaxonomyName name,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        TaxonomyTermRow? row = await context.TaxonomyTerms
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return new TaxonomyMutationResult(false, "taxonomy.not_found");
        }

        bool collision = await context.TaxonomyTerms.AnyAsync(
            item => item.Id != id
                && item.Kind == row.Kind
                && item.NormalizedName == name.Normalized,
            cancellationToken);
        if (collision)
        {
            return new TaxonomyMutationResult(false, "taxonomy.name_conflict");
        }

        row.DisplayName = name.Display;
        row.NormalizedName = name.Normalized;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new TaxonomyMutationResult(true, null);
        }
        catch (DbUpdateException error) when (IsUniqueConstraint(error))
        {
            return new TaxonomyMutationResult(false, "taxonomy.name_conflict");
        }
    }

    public async Task<TaxonomyMutationResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        TaxonomyTermRow? row = await context.TaxonomyTerms
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
        {
            return new TaxonomyMutationResult(false, "taxonomy.not_found");
        }

        if (await CountReferencesAsync(context, id, cancellationToken) > 0)
        {
            return new TaxonomyMutationResult(false, "taxonomy.in_use");
        }

        context.TaxonomyTerms.Remove(row);
        await context.SaveChangesAsync(cancellationToken);
        return new TaxonomyMutationResult(true, null);
    }

    public async Task<TaxonomyValidationResult> ValidateAsync(
        MetadataSelection selection,
        CancellationToken cancellationToken)
    {
        Guid[] requested = selection.AuthorIds
            .Concat(selection.TagIds)
            .Concat(selection.SeriesId is Guid seriesId ? [seriesId] : [])
            .Distinct()
            .ToArray();
        if (requested.Length == 0)
        {
            return TaxonomyValidationResult.Success;
        }

        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        TaxonomyTermRow[] terms = await context.TaxonomyTerms
            .AsNoTracking()
            .Where(row => requested.Contains(row.Id))
            .ToArrayAsync(cancellationToken);
        if (terms.Length != requested.Length)
        {
            return new TaxonomyValidationResult(false, "metadata.taxonomy_missing");
        }

        var kinds = terms.ToDictionary(row => row.Id, row => row.Kind);
        bool mismatch = selection.AuthorIds.Any(id => kinds[id] != KindKey(TaxonomyKind.Author))
            || selection.TagIds.Any(id => kinds[id] != KindKey(TaxonomyKind.Tag))
            || selection.SeriesId is Guid id && kinds[id] != KindKey(TaxonomyKind.Series);
        return mismatch
            ? new TaxonomyValidationResult(false, "metadata.taxonomy_kind_mismatch")
            : TaxonomyValidationResult.Success;
    }

    private static async Task<int> CountReferencesAsync(
        MediaDesktopDbContext context,
        Guid id,
        CancellationToken cancellationToken) =>
        await context.InboxDraftAuthors.CountAsync(row => row.AuthorId == id, cancellationToken)
        + await context.InboxDraftTags.CountAsync(row => row.TagId == id, cancellationToken)
        + await context.MetadataDrafts.CountAsync(row => row.SeriesId == id, cancellationToken)
        + await context.MediaAssetAuthors.CountAsync(row => row.AuthorId == id, cancellationToken)
        + await context.MediaAssetTags.CountAsync(row => row.TagId == id, cancellationToken)
        + await context.MediaAssets.CountAsync(row => row.SeriesId == id, cancellationToken);

    private static TaxonomyTermDto ToDto(TaxonomyTermRow row, int references) =>
        new(
            row.Id,
            Enum.Parse<TaxonomyKind>(row.Kind, true),
            row.DisplayName,
            row.NormalizedName,
            references);

    private static string KindKey(TaxonomyKind kind) =>
        kind.ToString().ToLowerInvariant();

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static bool IsUniqueConstraint(DbUpdateException error) =>
        error.InnerException is SqliteException sqlite
        && sqlite.SqliteErrorCode == 19
        && sqlite.SqliteExtendedErrorCode == 2067;
}
