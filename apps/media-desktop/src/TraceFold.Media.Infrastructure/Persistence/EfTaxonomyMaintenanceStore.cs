using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

    public async Task<TaxonomyMaintenanceStoreResult> ApplyMergeAsync(
        TaxonomyMergePreparation preparation,
        TaxonomyName? finalName,
        CancellationToken cancellationToken)
    {
        if (preparation is null)
        {
            return StoreFailure(
                TaxonomyMaintenanceErrorCodes.SelectionInvalid);
        }

        await using MediaDesktopDbContext context =
            await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            TaxonomyMergePrepareResult reloaded =
                await PrepareMergeInContextAsync(
                    context,
                    preparation.Selection,
                    cancellationToken);
            if (!reloaded.Succeeded || reloaded.Preparation is null)
            {
                throw new TaxonomyMaintenancePlanningException(
                    TaxonomyMaintenanceErrorCodes.PreviewStale);
            }

            TaxonomyMergePreparation current = reloaded.Preparation with
            {
                OperationId = preparation.OperationId,
            };
            if (!MergePreparationsEqual(preparation, current))
            {
                throw new TaxonomyMaintenancePlanningException(
                    TaxonomyMaintenanceErrorCodes.PreviewStale);
            }

            TaxonomyMergePlan plan = TaxonomyMergePlanner.Build(
                current,
                finalName);
            var receipt = new TaxonomyMergeUndoReceipt(
                preparation.OperationId,
                current.Target,
                plan.FinalName,
                current.Sources.ToArray(),
                plan.Owners.Select(owner => new TaxonomyMergeUndoOwner(
                    owner.Owner,
                    checked(owner.BeforeVersion + 1),
                    owner.Before,
                    owner.After)).ToArray());

            DateTimeOffset now = _timeProvider.GetUtcNow();
            foreach (TaxonomyMergePlanOwner owner in plan.Owners)
            {
                await ApplyOwnerAsync(
                    context,
                    plan.Kind,
                    owner,
                    now,
                    cancellationToken);
            }

            Guid[] sourceIds = plan.Sources.Select(source => source.Id).ToArray();
            TaxonomyTermRow[] sourceRows = await context.TaxonomyTerms
                .Where(term => sourceIds.Contains(term.Id))
                .ToArrayAsync(cancellationToken);
            TaxonomyTermRow? targetRow = await context.TaxonomyTerms
                .SingleOrDefaultAsync(
                    term => term.Id == plan.Target.Id,
                    cancellationToken);
            if (sourceRows.Length != sourceIds.Length || targetRow is null)
            {
                throw new TaxonomyMaintenancePlanningException(
                    TaxonomyMaintenanceErrorCodes.PreviewStale);
            }

            context.TaxonomyTerms.RemoveRange(sourceRows);
            await context.SaveChangesAsync(cancellationToken);

            if (!StringComparer.Ordinal.Equals(
                    targetRow.DisplayName,
                    plan.FinalName.Display)
                || !StringComparer.Ordinal.Equals(
                    targetRow.NormalizedName,
                    plan.FinalName.Normalized))
            {
                targetRow.DisplayName = plan.FinalName.Display;
                targetRow.NormalizedName = plan.FinalName.Normalized;
                targetRow.UpdatedAtUtc = now;
                await context.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new TaxonomyMaintenanceStoreResult(
                true,
                plan.Owners.Select(owner => owner.Owner).ToArray(),
                plan.Sources.Count,
                receipt,
                null);
        }
        catch (TaxonomyMaintenancePlanningException error)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return StoreFailure(error.ErrorCode);
        }
        catch (Exception error) when (error is DbUpdateException
            or SqliteException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return StoreFailure(
                TaxonomyMaintenanceErrorCodes.PersistenceFailed);
        }
    }

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

    public async Task<TaxonomyMaintenanceStoreResult> DeleteUnusedAsync(
        TaxonomyDeletePreparation preparation,
        CancellationToken cancellationToken)
    {
        if (preparation is null
            || preparation.OperationId == Guid.Empty
            || preparation.Selection is null
            || preparation.Terms is null
            || preparation.Blockers is null)
        {
            return StoreFailure(
                TaxonomyMaintenanceErrorCodes.SelectionInvalid);
        }

        if (!preparation.CanDelete)
        {
            return StoreFailure(TaxonomyMaintenanceErrorCodes.DeleteInUse);
        }

        await using MediaDesktopDbContext context =
            await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            Guid[] selectedIds = preparation.Selection.TermIds.ToArray();
            TaxonomyTermRow[] loaded = await context.TaxonomyTerms
                .Where(term => selectedIds.Contains(term.Id))
                .ToArrayAsync(cancellationToken);
            if (loaded.Length != selectedIds.Length
                || loaded.Any(term => term.Kind != KindKey(
                    preparation.Selection.Kind)))
            {
                throw new TaxonomyMaintenancePlanningException(
                    TaxonomyMaintenanceErrorCodes.PreviewStale);
            }

            IReadOnlyDictionary<Guid, TaxonomyTermRow> byId =
                loaded.ToDictionary(term => term.Id);
            TaxonomyTermSnapshot[] currentTerms = selectedIds
                .Select(id => ToSnapshot(byId[id]))
                .ToArray();
            if (!SnapshotSetsEqual(preparation.Terms, currentTerms))
            {
                throw new TaxonomyMaintenancePlanningException(
                    TaxonomyMaintenanceErrorCodes.PreviewStale);
            }

            IReadOnlyDictionary<Guid, ReferenceCounts> counts =
                await LoadReferenceCountsAsync(
                    context,
                    preparation.Selection.Kind,
                    selectedIds,
                    cancellationToken);
            if (counts.Values.Any(count => count.Total > 0))
            {
                throw new TaxonomyMaintenancePlanningException(
                    TaxonomyMaintenanceErrorCodes.DeleteInUse);
            }

            var receipt = new TaxonomyDeleteUndoReceipt(
                preparation.OperationId,
                currentTerms);
            context.TaxonomyTerms.RemoveRange(loaded);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new TaxonomyMaintenanceStoreResult(
                true,
                [],
                currentTerms.Length,
                receipt,
                null);
        }
        catch (TaxonomyMaintenancePlanningException error)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return StoreFailure(error.ErrorCode);
        }
        catch (Exception error) when (error is DbUpdateException
            or SqliteException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return StoreFailure(
                TaxonomyMaintenanceErrorCodes.PersistenceFailed);
        }
    }

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

    private static async Task ApplyOwnerAsync(
        MediaDesktopDbContext context,
        TaxonomyKind kind,
        TaxonomyMergePlanOwner owner,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        switch (owner.Owner.Kind)
        {
            case MetadataOwnerKind.InboxItem:
                MetadataDraftRow? draft = await context.MetadataDrafts
                    .SingleOrDefaultAsync(
                        row => row.InboxItemId == owner.Owner.Id,
                        cancellationToken);
                if (draft is null)
                {
                    throw new TaxonomyMaintenancePlanningException(
                        TaxonomyMaintenanceErrorCodes.PreviewStale);
                }

                await ReplaceInboxAssignmentAsync(
                    context,
                    kind,
                    owner.Owner.Id,
                    owner.After,
                    cancellationToken);
                draft.SeriesId = owner.After.SeriesId;
                draft.Version = checked(draft.Version + 1);
                draft.UpdatedAtUtc = now;
                break;
            case MetadataOwnerKind.MediaAsset:
                MediaAssetRow? asset = await context.MediaAssets
                    .SingleOrDefaultAsync(
                        row => row.Id == owner.Owner.Id,
                        cancellationToken);
                if (asset is null)
                {
                    throw new TaxonomyMaintenancePlanningException(
                        TaxonomyMaintenanceErrorCodes.PreviewStale);
                }

                await ReplaceAssetAssignmentAsync(
                    context,
                    kind,
                    owner.Owner.Id,
                    owner.After,
                    cancellationToken);
                asset.SeriesId = owner.After.SeriesId;
                asset.MetadataVersion = checked(asset.MetadataVersion + 1);
                asset.UpdatedAtUtc = now;
                break;
            default:
                throw new TaxonomyMaintenancePlanningException(
                    TaxonomyMaintenanceErrorCodes.SelectionInvalid);
        }
    }

    private static async Task ReplaceInboxAssignmentAsync(
        MediaDesktopDbContext context,
        TaxonomyKind kind,
        Guid ownerId,
        TaxonomyAssignmentSnapshot after,
        CancellationToken cancellationToken)
    {
        if (kind == TaxonomyKind.Author)
        {
            await context.InboxDraftAuthors
                .Where(link => link.InboxItemId == ownerId)
                .ExecuteDeleteAsync(cancellationToken);
            context.InboxDraftAuthors.AddRange(after.AuthorIds.Select(
                (termId, ordinal) => new InboxDraftAuthorRow
                {
                    InboxItemId = ownerId,
                    AuthorId = termId,
                    Ordinal = ordinal,
                }));
        }
        else if (kind == TaxonomyKind.Tag)
        {
            await context.InboxDraftTags
                .Where(link => link.InboxItemId == ownerId)
                .ExecuteDeleteAsync(cancellationToken);
            context.InboxDraftTags.AddRange(after.TagIds.Select(
                (termId, ordinal) => new InboxDraftTagRow
                {
                    InboxItemId = ownerId,
                    TagId = termId,
                    Ordinal = ordinal,
                }));
        }
    }

    private static async Task ReplaceAssetAssignmentAsync(
        MediaDesktopDbContext context,
        TaxonomyKind kind,
        Guid ownerId,
        TaxonomyAssignmentSnapshot after,
        CancellationToken cancellationToken)
    {
        if (kind == TaxonomyKind.Author)
        {
            await context.MediaAssetAuthors
                .Where(link => link.MediaAssetId == ownerId)
                .ExecuteDeleteAsync(cancellationToken);
            context.MediaAssetAuthors.AddRange(after.AuthorIds.Select(
                (termId, ordinal) => new MediaAssetAuthorRow
                {
                    MediaAssetId = ownerId,
                    AuthorId = termId,
                    Ordinal = ordinal,
                }));
        }
        else if (kind == TaxonomyKind.Tag)
        {
            await context.MediaAssetTags
                .Where(link => link.MediaAssetId == ownerId)
                .ExecuteDeleteAsync(cancellationToken);
            context.MediaAssetTags.AddRange(after.TagIds.Select(
                (termId, ordinal) => new MediaAssetTagRow
                {
                    MediaAssetId = ownerId,
                    TagId = termId,
                    Ordinal = ordinal,
                }));
        }
    }

    private static bool MergePreparationsEqual(
        TaxonomyMergePreparation expected,
        TaxonomyMergePreparation current) =>
        expected.OperationId == current.OperationId
        && expected.Selection.Kind == current.Selection.Kind
        && expected.Selection.TargetId == current.Selection.TargetId
        && expected.Selection.SourceIds.SequenceEqual(
            current.Selection.SourceIds)
        && TermSnapshotsEqual(expected.Target, current.Target)
        && SnapshotSetsEqual(expected.Sources, current.Sources)
        && OwnerSetsEqual(expected.Owners, current.Owners);

    private static bool SnapshotSetsEqual(
        IReadOnlyList<TaxonomyTermSnapshot> expected,
        IReadOnlyList<TaxonomyTermSnapshot> current)
    {
        if (expected.Count != current.Count)
        {
            return false;
        }

        IReadOnlyDictionary<Guid, TaxonomyTermSnapshot> currentById =
            current.ToDictionary(term => term.Id);
        return expected.All(term =>
            currentById.TryGetValue(
                term.Id,
                out TaxonomyTermSnapshot? currentTerm)
            && TermSnapshotsEqual(term, currentTerm));
    }

    private static bool TermSnapshotsEqual(
        TaxonomyTermSnapshot left,
        TaxonomyTermSnapshot right) =>
        left.Id == right.Id
        && left.Kind == right.Kind
        && StringComparer.Ordinal.Equals(left.DisplayName, right.DisplayName)
        && StringComparer.Ordinal.Equals(
            left.NormalizedName,
            right.NormalizedName)
        && left.CreatedAtUtc == right.CreatedAtUtc
        && left.UpdatedAtUtc == right.UpdatedAtUtc;

    private static bool OwnerSetsEqual(
        IReadOnlyList<TaxonomyOwnerSnapshot> expected,
        IReadOnlyList<TaxonomyOwnerSnapshot> current)
    {
        if (expected.Count != current.Count)
        {
            return false;
        }

        IReadOnlyDictionary<MetadataOwner, TaxonomyOwnerSnapshot> currentByOwner =
            current.ToDictionary(owner => owner.Owner);
        return expected.All(owner =>
            currentByOwner.TryGetValue(
                owner.Owner,
                out TaxonomyOwnerSnapshot? currentOwner)
            && owner.Version == currentOwner.Version
            && StringComparer.Ordinal.Equals(
                owner.VisibleName,
                currentOwner.VisibleName)
            && AssignmentsEqual(owner.Assignment, currentOwner.Assignment));
    }

    private static bool AssignmentsEqual(
        TaxonomyAssignmentSnapshot left,
        TaxonomyAssignmentSnapshot right) =>
        left.SeriesId == right.SeriesId
        && left.AuthorIds.SequenceEqual(right.AuthorIds)
        && left.TagIds.SequenceEqual(right.TagIds);

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

    private static TaxonomyMaintenanceStoreResult StoreFailure(
        string errorCode) =>
        new(false, [], 0, null, errorCode);

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
