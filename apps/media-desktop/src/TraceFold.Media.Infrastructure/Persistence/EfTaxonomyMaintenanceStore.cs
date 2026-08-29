using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfTaxonomyMaintenanceStore : ITaxonomyMaintenanceStore
{
    private readonly IDbContextFactory<MediaDesktopDbContext> _contextFactory;
    private readonly TimeProvider _timeProvider;

    public EfTaxonomyMaintenanceStore(
        IDbContextFactory<MediaDesktopDbContext> contextFactory,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _contextFactory = contextFactory;
        _timeProvider = timeProvider;
    }

    public async Task<TaxonomyMergePrepareResult> PrepareMergeAsync(
        TaxonomyMergeSelection selection,
        CancellationToken cancellationToken)
    {
        if (selection is null)
        {
            return MergePrepareFailure(
                TaxonomyMaintenanceErrorCodes.SelectionInvalid);
        }

        try
        {
            await using MediaDesktopDbContext context =
                await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await PrepareMergeInContextAsync(
                context,
                selection,
                cancellationToken);
        }
        catch (Exception error) when (error is SqliteException
            or DbUpdateException)
        {
            return MergePrepareFailure(
                TaxonomyMaintenanceErrorCodes.PersistenceFailed);
        }
    }

    public Task<TaxonomyMaintenanceStoreResult> ApplyMergeAsync(
        TaxonomyMergePreparation preparation,
        TaxonomyName? finalName,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public async Task<TaxonomyDeletePrepareResult> PrepareDeleteAsync(
        TaxonomyDeleteSelection selection,
        CancellationToken cancellationToken)
    {
        if (selection is null)
        {
            return DeletePrepareFailure(
                TaxonomyMaintenanceErrorCodes.SelectionInvalid);
        }

        try
        {
            await using MediaDesktopDbContext context =
                await _contextFactory.CreateDbContextAsync(cancellationToken);
            TaxonomyTermRow[] loaded = await context.TaxonomyTerms
                .AsNoTracking()
                .Where(row => selection.TermIds.Contains(row.Id))
                .ToArrayAsync(cancellationToken);
            if (loaded.Length != selection.TermIds.Count)
            {
                return DeletePrepareFailure(
                    TaxonomyMaintenanceErrorCodes.NotFound);
            }

            string kindKey = KindKey(selection.Kind);
            if (loaded.Any(row => row.Kind != kindKey))
            {
                return DeletePrepareFailure(
                    TaxonomyMaintenanceErrorCodes.KindMismatch);
            }

            IReadOnlyDictionary<Guid, ReferenceCounts> counts =
                await LoadReferenceCountsAsync(
                    context,
                    selection.Kind,
                    selection.TermIds,
                    cancellationToken);
            IReadOnlyDictionary<Guid, TaxonomyTermRow> byId =
                loaded.ToDictionary(row => row.Id);
            TaxonomyTermSnapshot[] terms = selection.TermIds
                .Select(id => ToSnapshot(byId[id]))
                .ToArray();
            TaxonomyDeleteBlocker[] blockers = terms
                .Where(term => counts[term.Id].Total > 0)
                .Select(term => new TaxonomyDeleteBlocker(
                    term,
                    counts[term.Id].Inbox,
                    counts[term.Id].Library))
                .ToArray();
            var preparation = new TaxonomyDeletePreparation(
                Guid.NewGuid(),
                selection,
                terms,
                blockers);
            return new TaxonomyDeletePrepareResult(true, preparation, null);
        }
        catch (Exception error) when (error is SqliteException
            or DbUpdateException)
        {
            return DeletePrepareFailure(
                TaxonomyMaintenanceErrorCodes.PersistenceFailed);
        }
    }

    public Task<TaxonomyMaintenanceStoreResult> DeleteUnusedAsync(
        TaxonomyDeletePreparation preparation,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<TaxonomyMaintenanceUndoResult> UndoAsync(
        TaxonomyMaintenanceUndoReceipt receipt,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    private static async Task<TaxonomyMergePrepareResult>
        PrepareMergeInContextAsync(
            MediaDesktopDbContext context,
            TaxonomyMergeSelection selection,
            CancellationToken cancellationToken)
    {
        Guid[] requestedIds = selection.SourceIds
            .Append(selection.TargetId)
            .Distinct()
            .ToArray();
        TaxonomyTermRow[] loaded = await context.TaxonomyTerms
            .AsNoTracking()
            .Where(row => requestedIds.Contains(row.Id))
            .ToArrayAsync(cancellationToken);
        if (loaded.Length != requestedIds.Length)
        {
            return MergePrepareFailure(TaxonomyMaintenanceErrorCodes.NotFound);
        }

        string kindKey = KindKey(selection.Kind);
        if (loaded.Any(row => row.Kind != kindKey))
        {
            return MergePrepareFailure(
                TaxonomyMaintenanceErrorCodes.KindMismatch);
        }

        IReadOnlyDictionary<Guid, TaxonomyTermRow> byId =
            loaded.ToDictionary(row => row.Id);
        TaxonomyTermSnapshot target = ToSnapshot(byId[selection.TargetId]);
        TaxonomyTermSnapshot[] sources = selection.SourceIds
            .Select(id => ToSnapshot(byId[id]))
            .ToArray();
        TaxonomyNameOwnerSnapshot[] sameKindNames =
            await context.TaxonomyTerms
                .AsNoTracking()
                .Where(row => row.Kind == kindKey)
                .OrderBy(row => row.NormalizedName)
                .ThenBy(row => row.Id)
                .Select(row => new TaxonomyNameOwnerSnapshot(
                    row.NormalizedName,
                    row.Id))
                .ToArrayAsync(cancellationToken);
        IReadOnlyList<TaxonomyOwnerSnapshot> owners =
            await LoadAffectedOwnersAsync(
                context,
                selection.Kind,
                selection.SourceIds,
                cancellationToken);
        var preparation = new TaxonomyMergePreparation(
            Guid.NewGuid(),
            selection,
            target,
            sources,
            sameKindNames,
            owners);
        return new TaxonomyMergePrepareResult(true, preparation, null);
    }

    private static async Task<IReadOnlyList<TaxonomyOwnerSnapshot>>
        LoadAffectedOwnersAsync(
            MediaDesktopDbContext context,
            TaxonomyKind kind,
            IReadOnlyList<Guid> sourceIds,
            CancellationToken cancellationToken)
    {
        Guid[] inboxIds;
        Guid[] assetIds;
        switch (kind)
        {
            case TaxonomyKind.Author:
                inboxIds = await context.InboxDraftAuthors
                    .AsNoTracking()
                    .Where(link => sourceIds.Contains(link.AuthorId))
                    .Select(link => link.InboxItemId)
                    .Distinct()
                    .ToArrayAsync(cancellationToken);
                assetIds = await context.MediaAssetAuthors
                    .AsNoTracking()
                    .Where(link => sourceIds.Contains(link.AuthorId))
                    .Select(link => link.MediaAssetId)
                    .Distinct()
                    .ToArrayAsync(cancellationToken);
                break;
            case TaxonomyKind.Series:
                inboxIds = await context.MetadataDrafts
                    .AsNoTracking()
                    .Where(draft => draft.SeriesId != null
                        && sourceIds.Contains(draft.SeriesId.Value))
                    .Select(draft => draft.InboxItemId)
                    .Distinct()
                    .ToArrayAsync(cancellationToken);
                assetIds = await context.MediaAssets
                    .AsNoTracking()
                    .Where(asset => asset.SeriesId != null
                        && sourceIds.Contains(asset.SeriesId.Value))
                    .Select(asset => asset.Id)
                    .Distinct()
                    .ToArrayAsync(cancellationToken);
                break;
            case TaxonomyKind.Tag:
                inboxIds = await context.InboxDraftTags
                    .AsNoTracking()
                    .Where(link => sourceIds.Contains(link.TagId))
                    .Select(link => link.InboxItemId)
                    .Distinct()
                    .ToArrayAsync(cancellationToken);
                assetIds = await context.MediaAssetTags
                    .AsNoTracking()
                    .Where(link => sourceIds.Contains(link.TagId))
                    .Select(link => link.MediaAssetId)
                    .Distinct()
                    .ToArrayAsync(cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        OwnerFact[] inboxFacts = await context.MetadataDrafts
            .AsNoTracking()
            .Where(draft => inboxIds.Contains(draft.InboxItemId))
            .Select(draft => new OwnerFact
            {
                Id = draft.InboxItemId,
                ActualFilename = draft.InboxItem.ActualFilename,
                DisplayName = draft.DisplayName,
                Version = draft.Version,
                SeriesId = draft.SeriesId,
            })
            .ToArrayAsync(cancellationToken);
        OwnerFact[] assetFacts = await context.MediaAssets
            .AsNoTracking()
            .Where(asset => assetIds.Contains(asset.Id))
            .Select(asset => new OwnerFact
            {
                Id = asset.Id,
                ActualFilename = asset.ActualFilename,
                DisplayName = asset.DisplayName,
                Version = asset.MetadataVersion,
                SeriesId = asset.SeriesId,
            })
            .ToArrayAsync(cancellationToken);

        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> inboxAuthors =
            await LoadInboxAuthorIdsAsync(context, inboxIds, cancellationToken);
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> inboxTags =
            await LoadInboxTagIdsAsync(context, inboxIds, cancellationToken);
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> assetAuthors =
            await LoadAssetAuthorIdsAsync(context, assetIds, cancellationToken);
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> assetTags =
            await LoadAssetTagIdsAsync(context, assetIds, cancellationToken);

        IEnumerable<TaxonomyOwnerSnapshot> inboxOwners = inboxFacts.Select(fact =>
            ToOwnerSnapshot(
                MetadataOwnerKind.InboxItem,
                fact,
                GetIds(inboxAuthors, fact.Id),
                GetIds(inboxTags, fact.Id)));
        IEnumerable<TaxonomyOwnerSnapshot> libraryOwners = assetFacts.Select(fact =>
            ToOwnerSnapshot(
                MetadataOwnerKind.MediaAsset,
                fact,
                GetIds(assetAuthors, fact.Id),
                GetIds(assetTags, fact.Id)));
        return inboxOwners
            .Concat(libraryOwners)
            .OrderBy(owner => owner.Owner.Kind)
            .ThenBy(owner => owner.VisibleName, StringComparer.Ordinal)
            .ThenBy(owner => owner.Owner.Id)
            .ToArray();
    }

    private static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>
        LoadInboxAuthorIdsAsync(
            MediaDesktopDbContext context,
            IReadOnlyList<Guid> ownerIds,
            CancellationToken cancellationToken) =>
        ToOrderedIdMap(await context.InboxDraftAuthors
            .AsNoTracking()
            .Where(link => ownerIds.Contains(link.InboxItemId))
            .OrderBy(link => link.InboxItemId)
            .ThenBy(link => link.Ordinal)
            .ThenBy(link => link.AuthorId)
            .Select(link => new OrderedLink
            {
                OwnerId = link.InboxItemId,
                TermId = link.AuthorId,
                Ordinal = link.Ordinal,
            })
            .ToArrayAsync(cancellationToken));

    private static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>
        LoadInboxTagIdsAsync(
            MediaDesktopDbContext context,
            IReadOnlyList<Guid> ownerIds,
            CancellationToken cancellationToken) =>
        ToOrderedIdMap(await context.InboxDraftTags
            .AsNoTracking()
            .Where(link => ownerIds.Contains(link.InboxItemId))
            .OrderBy(link => link.InboxItemId)
            .ThenBy(link => link.Ordinal)
            .ThenBy(link => link.TagId)
            .Select(link => new OrderedLink
            {
                OwnerId = link.InboxItemId,
                TermId = link.TagId,
                Ordinal = link.Ordinal,
            })
            .ToArrayAsync(cancellationToken));

    private static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>
        LoadAssetAuthorIdsAsync(
            MediaDesktopDbContext context,
            IReadOnlyList<Guid> ownerIds,
            CancellationToken cancellationToken) =>
        ToOrderedIdMap(await context.MediaAssetAuthors
            .AsNoTracking()
            .Where(link => ownerIds.Contains(link.MediaAssetId))
            .OrderBy(link => link.MediaAssetId)
            .ThenBy(link => link.Ordinal)
            .ThenBy(link => link.AuthorId)
            .Select(link => new OrderedLink
            {
                OwnerId = link.MediaAssetId,
                TermId = link.AuthorId,
                Ordinal = link.Ordinal,
            })
            .ToArrayAsync(cancellationToken));

    private static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>
        LoadAssetTagIdsAsync(
            MediaDesktopDbContext context,
            IReadOnlyList<Guid> ownerIds,
            CancellationToken cancellationToken) =>
        ToOrderedIdMap(await context.MediaAssetTags
            .AsNoTracking()
            .Where(link => ownerIds.Contains(link.MediaAssetId))
            .OrderBy(link => link.MediaAssetId)
            .ThenBy(link => link.Ordinal)
            .ThenBy(link => link.TagId)
            .Select(link => new OrderedLink
            {
                OwnerId = link.MediaAssetId,
                TermId = link.TagId,
                Ordinal = link.Ordinal,
            })
            .ToArrayAsync(cancellationToken));

    private static IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> ToOrderedIdMap(
        IEnumerable<OrderedLink> links) =>
        links.GroupBy(link => link.OwnerId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Guid>)group
                    .OrderBy(link => link.Ordinal)
                    .ThenBy(link => link.TermId)
                    .Select(link => link.TermId)
                    .ToArray());

    private static IReadOnlyList<Guid> GetIds(
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> byOwner,
        Guid ownerId) =>
        byOwner.TryGetValue(ownerId, out IReadOnlyList<Guid>? ids) ? ids : [];

    private static TaxonomyOwnerSnapshot ToOwnerSnapshot(
        MetadataOwnerKind kind,
        OwnerFact fact,
        IReadOnlyList<Guid> authorIds,
        IReadOnlyList<Guid> tagIds) =>
        new(
            new MetadataOwner(kind, fact.Id),
            fact.DisplayName ?? fact.ActualFilename,
            fact.Version,
            new TaxonomyAssignmentSnapshot(
                authorIds.ToArray(),
                fact.SeriesId,
                tagIds.ToArray()));

    private static async Task<IReadOnlyDictionary<Guid, ReferenceCounts>>
        LoadReferenceCountsAsync(
            MediaDesktopDbContext context,
            TaxonomyKind kind,
            IReadOnlyList<Guid> termIds,
            CancellationToken cancellationToken)
    {
        IQueryable<ReferenceCountProjection> query = kind switch
        {
            TaxonomyKind.Author => context.TaxonomyTerms
                .Where(term => termIds.Contains(term.Id))
                .Select(term => new ReferenceCountProjection
                {
                    TermId = term.Id,
                    Inbox = context.InboxDraftAuthors.Count(
                        link => link.AuthorId == term.Id),
                    Library = context.MediaAssetAuthors.Count(
                        link => link.AuthorId == term.Id),
                }),
            TaxonomyKind.Series => context.TaxonomyTerms
                .Where(term => termIds.Contains(term.Id))
                .Select(term => new ReferenceCountProjection
                {
                    TermId = term.Id,
                    Inbox = context.MetadataDrafts.Count(
                        draft => draft.SeriesId == term.Id),
                    Library = context.MediaAssets.Count(
                        asset => asset.SeriesId == term.Id),
                }),
            TaxonomyKind.Tag => context.TaxonomyTerms
                .Where(term => termIds.Contains(term.Id))
                .Select(term => new ReferenceCountProjection
                {
                    TermId = term.Id,
                    Inbox = context.InboxDraftTags.Count(
                        link => link.TagId == term.Id),
                    Library = context.MediaAssetTags.Count(
                        link => link.TagId == term.Id),
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        ReferenceCountProjection[] loaded = await query
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        return loaded.ToDictionary(
            row => row.TermId,
            row => new ReferenceCounts(row.Inbox, row.Library));
    }

    private static TaxonomyTermSnapshot ToSnapshot(TaxonomyTermRow row) =>
        new(
            row.Id,
            Enum.Parse<TaxonomyKind>(row.Kind, true),
            row.DisplayName,
            row.NormalizedName,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static string KindKey(TaxonomyKind kind) =>
        kind.ToString().ToLowerInvariant();

    private static TaxonomyMergePrepareResult MergePrepareFailure(
        string errorCode) =>
        new(false, null, errorCode);

    private static TaxonomyDeletePrepareResult DeletePrepareFailure(
        string errorCode) =>
        new(false, null, errorCode);

    private sealed class OwnerFact
    {
        public Guid Id { get; init; }

        public required string ActualFilename { get; init; }

        public string? DisplayName { get; init; }

        public long Version { get; init; }

        public Guid? SeriesId { get; init; }
    }

    private sealed class OrderedLink
    {
        public Guid OwnerId { get; init; }

        public Guid TermId { get; init; }

        public int Ordinal { get; init; }
    }

    private sealed class ReferenceCountProjection
    {
        public Guid TermId { get; init; }

        public int Inbox { get; init; }

        public int Library { get; init; }
    }

    private sealed record ReferenceCounts(int Inbox, int Library)
    {
        public int Total => checked(Inbox + Library);
    }
}
