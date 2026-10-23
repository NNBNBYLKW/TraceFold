using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfLibraryBatchEditStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory,
    INamingRuleEngine namingRuleEngine,
    TimeProvider? timeProvider = null) : ILibraryBatchEditStore
{
    private const int BatchSize = 200;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<LibraryBatchPrepareResult> PrepareAsync(
        IReadOnlyList<LibrarySelectionEntry> selection,
        CancellationToken cancellationToken)
    {
        string? shapeError = ValidateSelectionShape(selection);
        if (shapeError is not null)
        {
            return PrepareFailure(shapeError);
        }

        try
        {
            await using MediaDesktopDbContext context =
                await contextFactory.CreateDbContextAsync(cancellationToken);
            IReadOnlyDictionary<Guid, AssetSnapshot> snapshots =
                await LoadSnapshotsAsync(context, selection, cancellationToken);
            string? snapshotError = ValidateSnapshots(selection, snapshots);
            if (snapshotError is not null)
            {
                return PrepareFailure(snapshotError);
            }

            LibraryBatchAssetDto[] assets = selection
                .OrderBy(item => item.Ordinal)
                .Select(item => ToEditorAsset(snapshots[item.AssetId], item))
                .ToArray();
            IReadOnlySet<string> unselectedNames =
                await LoadUnselectedDisplayNamesAsync(
                    context,
                    selection,
                    cancellationToken);
            var editor = new LibraryBatchEditorDto(
                Array.AsReadOnly(assets),
                BuildCoverage(assets.Select(asset => asset.Authors)),
                BuildCoverage(assets.Select(asset => asset.Tags)),
                unselectedNames);
            return new LibraryBatchPrepareResult(true, editor, null);
        }
        catch (Exception error) when (error is DbUpdateException
            or SqliteException)
        {
            return PrepareFailure("library.batch.prepare_failed");
        }
    }

    public async Task<LibraryBatchStoreResult> ApplyAsync(
        IReadOnlyList<LibrarySelectionEntry> selection,
        LibraryBatchEditDraft draft,
        CancellationToken cancellationToken)
    {
        string? shapeError = ValidateSelectionShape(selection);
        if (shapeError is not null)
        {
            return Failure(selection?.Count ?? 0, shapeError);
        }

        try
        {
            ArgumentNullException.ThrowIfNull(draft);
            _ = LibraryBatchEditDraft.Create(
                draft.Metadata,
                draft.Naming,
                draft.ExistingTermSnapshots);
        }
        catch (ArgumentException)
        {
            return Failure(selection.Count, "library.batch.draft_invalid");
        }

        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            IReadOnlyDictionary<Guid, AssetSnapshot> snapshots =
                await LoadSnapshotsAsync(context, selection, cancellationToken);
            string? snapshotError = ValidateSnapshots(selection, snapshots);
            if (snapshotError is not null)
            {
                throw new LibraryBatchStoreException(snapshotError);
            }

            var resolver = new TaxonomyResolver(
                context,
                snapshots.Values,
                _timeProvider.GetUtcNow());
            await resolver.ValidateSnapshotsAsync(
                draft.ExistingTermSnapshots,
                cancellationToken);
            ResolvedBatchMetadataEdits edits = draft.Metadata is null
                ? UnchangedEdits()
                : await ResolveEditsAsync(
                    draft.Metadata,
                    resolver,
                    cancellationToken);
            // ResolveEditsAsync may add a new series term. ExecuteUpdateAsync below bypasses
            // change tracking, so its series_id must not reference an unflushed term.
            await context.SaveChangesAsync(cancellationToken);
            ProposedAsset[] proposed = selection
                .OrderBy(item => item.Ordinal)
                .Select(item => ProposeMetadata(
                    snapshots[item.AssetId],
                    item,
                    edits,
                    resolver))
                .ToArray();
            NamingPreviewResult? naming = draft.Naming is null
                ? null
                : namingRuleEngine.Preview(
                    draft.Naming,
                    proposed.Select(ToNamingInput).ToArray(),
                    await LoadUnselectedDisplayNamesAsync(
                        context,
                        selection,
                        cancellationToken));
            if (naming is not null && !naming.CanApply)
            {
                throw new LibraryBatchStoreException(
                    "library.batch.preview_invalid");
            }

            IReadOnlyDictionary<Guid, NamingPreviewRow> namingRows =
                naming?.Rows.ToDictionary(row => row.AssetId)
                ?? new Dictionary<Guid, NamingPreviewRow>();
            ProposedWrite[] writes = proposed
                .Select(item => BuildWrite(item, namingRows))
                .ToArray();
            ProposedWrite[] changed = writes
                .Where(item => item.MetadataChanged || item.DisplayNameChanged)
                .ToArray();
            if (changed.Length == 0)
            {
                throw new LibraryBatchStoreException(
                    "library.batch.no_changes");
            }

            var receipt = new LibraryBatchUndoReceipt(
                Guid.NewGuid(),
                changed.Select(item => new LibraryBatchUndoItem(
                    item.Snapshot.AssetId,
                    item.Snapshot.MetadataVersion + 1,
                    item.Snapshot.ExplicitDisplayName,
                    item.Snapshot.Series?.Id,
                    item.Snapshot.Authors.Select(term => term.Id).ToArray(),
                    item.Snapshot.Tags.Select(term => term.Id).ToArray()))
                    .ToArray());
            foreach (ProposedWrite[] chunk in changed.Chunk(BatchSize))
            {
                await ApplyChunkAsync(context, chunk, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                context.ChangeTracker.Clear();
            }

            await transaction.CommitAsync(cancellationToken);
            return new LibraryBatchStoreResult(
                true,
                changed.Length,
                selection.Count - changed.Length,
                naming?.WarningCount ?? 0,
                receipt,
                null);
        }
        catch (LibraryBatchStoreException error)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return Failure(selection.Count, error.ErrorCode);
        }
        catch (Exception error) when (error is DbUpdateException
            or SqliteException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return Failure(
                selection.Count,
                "library.batch.persistence_failed");
        }
    }

    public async Task<LibraryBatchUndoResult> UndoAsync(
        LibraryBatchUndoReceipt receipt,
        CancellationToken cancellationToken)
    {
        string? receiptError = ValidateUndoReceipt(receipt);
        if (receiptError is not null)
        {
            return UndoFailure(receiptError);
        }

        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            IReadOnlyDictionary<Guid, long> versions =
                await LoadVersionsAsync(
                    context,
                    receipt.Items.Select(item => item.AssetId),
                    cancellationToken);
            foreach (LibraryBatchUndoItem item in receipt.Items)
            {
                if (!versions.TryGetValue(item.AssetId, out long version))
                {
                    throw new LibraryBatchStoreException(
                        "library.batch.undo.selection_missing");
                }

                if (version != item.ExpectedPostVersion)
                {
                    throw new LibraryBatchStoreException(
                        "library.batch.undo.version_conflict");
                }
            }

            await ValidateUndoTaxonomyAsync(
                context,
                receipt.Items,
                cancellationToken);
            foreach (LibraryBatchUndoItem[] chunk in
                     receipt.Items.Chunk(BatchSize))
            {
                await RestoreChunkAsync(context, chunk, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                context.ChangeTracker.Clear();
            }

            await transaction.CommitAsync(cancellationToken);
            return new LibraryBatchUndoResult(
                true,
                receipt.Items.Count,
                null);
        }
        catch (LibraryBatchStoreException error)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return UndoFailure(error.ErrorCode);
        }
        catch (Exception error) when (error is DbUpdateException
            or SqliteException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return UndoFailure("library.batch.undo.persistence_failed");
        }
    }

    private async Task RestoreChunkAsync(
        MediaDesktopDbContext context,
        IReadOnlyList<LibraryBatchUndoItem> items,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach (LibraryBatchUndoItem item in items)
        {
            int updated = await context.MediaAssets
                .Where(row => row.Id == item.AssetId
                    && row.MetadataVersion == item.ExpectedPostVersion)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            row => row.DisplayName,
                            item.PreviousDisplayName)
                        .SetProperty(
                            row => row.SeriesId,
                            item.PreviousSeriesId)
                        .SetProperty(
                            row => row.MetadataVersion,
                            item.ExpectedPostVersion + 1)
                        .SetProperty(row => row.UpdatedAtUtc, now),
                    cancellationToken);
            if (updated != 1)
            {
                throw new LibraryBatchStoreException(
                    "library.batch.undo.version_conflict");
            }

            await context.MediaAssetAuthors
                .Where(row => row.MediaAssetId == item.AssetId)
                .ExecuteDeleteAsync(cancellationToken);
            await context.MediaAssetTags
                .Where(row => row.MediaAssetId == item.AssetId)
                .ExecuteDeleteAsync(cancellationToken);
            context.MediaAssetAuthors.AddRange(
                item.PreviousAuthorIds.Select(
                    (id, ordinal) => new MediaAssetAuthorRow
                    {
                        MediaAssetId = item.AssetId,
                        AuthorId = id,
                        Ordinal = ordinal
                    }));
            context.MediaAssetTags.AddRange(
                item.PreviousTagIds.Select(
                    (id, ordinal) => new MediaAssetTagRow
                    {
                        MediaAssetId = item.AssetId,
                        TagId = id,
                        Ordinal = ordinal
                    }));
        }
    }

    private static async Task<IReadOnlyDictionary<Guid, long>> LoadVersionsAsync(
        MediaDesktopDbContext context,
        IEnumerable<Guid> assetIds,
        CancellationToken cancellationToken)
    {
        Guid[] ids = assetIds.Distinct().ToArray();
        var versions = new Dictionary<Guid, long>();
        foreach (Guid[] chunk in ids.Chunk(BatchSize))
        {
            VersionRow[] loaded = await context.MediaAssets
                .AsNoTracking()
                .Where(row => chunk.Contains(row.Id))
                .Select(row => new VersionRow(row.Id, row.MetadataVersion))
                .ToArrayAsync(cancellationToken);
            foreach (VersionRow item in loaded)
            {
                versions[item.AssetId] = item.MetadataVersion;
            }
        }

        return versions;
    }

    private static async Task ValidateUndoTaxonomyAsync(
        MediaDesktopDbContext context,
        IReadOnlyList<LibraryBatchUndoItem> items,
        CancellationToken cancellationToken)
    {
        Guid[] authorIds = items
            .SelectMany(item => item.PreviousAuthorIds)
            .Distinct()
            .ToArray();
        Guid[] tagIds = items
            .SelectMany(item => item.PreviousTagIds)
            .Distinct()
            .ToArray();
        Guid[] seriesIds = items
            .Where(item => item.PreviousSeriesId.HasValue)
            .Select(item => item.PreviousSeriesId!.Value)
            .Distinct()
            .ToArray();
        Guid[] requested = authorIds.Concat(tagIds).Concat(seriesIds)
            .Distinct()
            .ToArray();
        var kinds = new Dictionary<Guid, string>();
        foreach (Guid[] chunk in requested.Chunk(BatchSize))
        {
            TaxonomyKindRow[] loaded = await context.TaxonomyTerms
                .AsNoTracking()
                .Where(row => chunk.Contains(row.Id))
                .Select(row => new TaxonomyKindRow(row.Id, row.Kind))
                .ToArrayAsync(cancellationToken);
            foreach (TaxonomyKindRow item in loaded)
            {
                kinds[item.TermId] = item.Kind;
            }
        }

        bool invalid = kinds.Count != requested.Length
            || authorIds.Any(id => kinds[id] != "author")
            || tagIds.Any(id => kinds[id] != "tag")
            || seriesIds.Any(id => kinds[id] != "series");
        if (invalid)
        {
            throw new LibraryBatchStoreException(
                "library.batch.undo.taxonomy_missing");
        }
    }

    private static string? ValidateUndoReceipt(LibraryBatchUndoReceipt? receipt)
    {
        if (receipt is null
            || receipt.OperationId == Guid.Empty
            || receipt.Items is null
            || receipt.Items.Count == 0
            || receipt.Items.Any(item => item is null
                || item.AssetId == Guid.Empty
                || item.ExpectedPostVersion < 0
                || item.PreviousAuthorIds is null
                || item.PreviousTagIds is null
                || item.PreviousAuthorIds.Any(id => id == Guid.Empty)
                || item.PreviousTagIds.Any(id => id == Guid.Empty)
                || item.PreviousAuthorIds.Distinct().Count()
                    != item.PreviousAuthorIds.Count
                || item.PreviousTagIds.Distinct().Count()
                    != item.PreviousTagIds.Count)
            || receipt.Items.Select(item => item.AssetId).Distinct().Count()
                != receipt.Items.Count)
        {
            return "library.batch.undo.invalid_receipt";
        }

        return null;
    }

    private static LibraryBatchUndoResult UndoFailure(string errorCode) =>
        new(false, 0, errorCode);

    private async Task ApplyChunkAsync(
        MediaDesktopDbContext context,
        IReadOnlyList<ProposedWrite> writes,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach (ProposedWrite write in writes)
        {
            int updated = await context.MediaAssets
                .Where(row => row.Id == write.Snapshot.AssetId
                    && row.MetadataVersion == write.Snapshot.MetadataVersion)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            row => row.DisplayName,
                            write.TargetDisplayName)
                        .SetProperty(
                            row => row.SeriesId,
                            write.ProposedMetadata.SeriesId)
                        .SetProperty(
                            row => row.MetadataVersion,
                            write.Snapshot.MetadataVersion + 1)
                        .SetProperty(row => row.UpdatedAtUtc, now),
                    cancellationToken);
            if (updated != 1)
            {
                throw new LibraryBatchStoreException(
                    "library.batch.version_conflict");
            }

            if (!write.MetadataChanged)
            {
                continue;
            }

            await context.MediaAssetAuthors
                .Where(row => row.MediaAssetId == write.Snapshot.AssetId)
                .ExecuteDeleteAsync(cancellationToken);
            await context.MediaAssetTags
                .Where(row => row.MediaAssetId == write.Snapshot.AssetId)
                .ExecuteDeleteAsync(cancellationToken);
            context.MediaAssetAuthors.AddRange(
                write.ProposedMetadata.AuthorIds.Select(
                    (id, ordinal) => new MediaAssetAuthorRow
                    {
                        MediaAssetId = write.Snapshot.AssetId,
                        AuthorId = id,
                        Ordinal = ordinal
                    }));
            context.MediaAssetTags.AddRange(
                write.ProposedMetadata.TagIds.Select(
                    (id, ordinal) => new MediaAssetTagRow
                    {
                        MediaAssetId = write.Snapshot.AssetId,
                        TagId = id,
                        Ordinal = ordinal
                    }));
        }
    }

    private static ProposedAsset ProposeMetadata(
        AssetSnapshot snapshot,
        LibrarySelectionEntry selection,
        ResolvedBatchMetadataEdits edits,
        TaxonomyResolver resolver)
    {
        MetadataSelection current = MetadataSelection.Create(
            snapshot.ExplicitDisplayName,
            snapshot.Authors.Select(term => term.Id),
            snapshot.Series?.Id,
            snapshot.Tags.Select(term => term.Id));
        MetadataSelection proposed = BatchMetadataMutation.Apply(current, edits);
        bool metadataChanged = !proposed.AuthorIds.SequenceEqual(current.AuthorIds)
            || proposed.SeriesId != current.SeriesId
            || !proposed.TagIds.SequenceEqual(current.TagIds);
        return new ProposedAsset(
            snapshot,
            selection,
            proposed,
            proposed.AuthorIds.Select(resolver.Get).ToArray(),
            proposed.SeriesId.HasValue
                ? resolver.Get(proposed.SeriesId.Value)
                : null,
            proposed.TagIds.Select(resolver.Get).ToArray(),
            metadataChanged);
    }

    private static NamingAssetInput ToNamingInput(ProposedAsset asset) =>
        new(
            asset.Snapshot.AssetId,
            asset.Snapshot.ActualFilename,
            asset.Snapshot.ExplicitDisplayName,
            asset.Authors.Select(term => term.DisplayName).ToArray(),
            asset.Series?.DisplayName,
            asset.Tags.Select(term => term.DisplayName).ToArray(),
            asset.Selection.Ordinal);

    private static ProposedWrite BuildWrite(
        ProposedAsset asset,
        IReadOnlyDictionary<Guid, NamingPreviewRow> namingRows)
    {
        namingRows.TryGetValue(
            asset.Snapshot.AssetId,
            out NamingPreviewRow? naming);
        bool displayNameChanged = naming?.IsChanged ?? false;
        string? targetDisplayName = displayNameChanged
            ? naming!.GeneratedName
            : asset.Snapshot.ExplicitDisplayName;
        return new ProposedWrite(
            asset.Snapshot,
            asset.ProposedMetadata,
            asset.MetadataChanged,
            displayNameChanged,
            targetDisplayName);
    }

    private static async Task<ResolvedBatchMetadataEdits> ResolveEditsAsync(
        BatchMetadataDraft draft,
        TaxonomyResolver resolver,
        CancellationToken cancellationToken) =>
        new(
            new ResolvedBatchSetEdit(
                draft.Authors.Operation,
                await resolver.ResolveManyAsync(
                    draft.Authors.Values,
                    TaxonomyKind.Author,
                    cancellationToken)),
            new ResolvedBatchSeriesEdit(
                draft.Series.Operation,
                draft.Series.Value is null
                    ? null
                    : await resolver.ResolveAsync(
                        draft.Series.Value,
                        TaxonomyKind.Series,
                        cancellationToken)),
            new ResolvedBatchSetEdit(
                draft.Tags.Operation,
                await resolver.ResolveManyAsync(
                    draft.Tags.Values,
                    TaxonomyKind.Tag,
                    cancellationToken)));

    private static ResolvedBatchMetadataEdits UnchangedEdits() =>
        new(
            ResolvedBatchSetEdit.Unchanged,
            ResolvedBatchSeriesEdit.Unchanged,
            ResolvedBatchSetEdit.Unchanged);

    private static async Task<IReadOnlyDictionary<Guid, AssetSnapshot>>
        LoadSnapshotsAsync(
            MediaDesktopDbContext context,
            IReadOnlyList<LibrarySelectionEntry> selection,
            CancellationToken cancellationToken)
    {
        Guid[] ids = selection.Select(item => item.AssetId).Distinct().ToArray();
        var basics = new Dictionary<Guid, AssetBase>();
        var authors = new Dictionary<Guid, List<TaxonomyTermDto>>();
        var tags = new Dictionary<Guid, List<TaxonomyTermDto>>();
        foreach (Guid[] chunk in ids.Chunk(BatchSize))
        {
            AssetBase[] loaded = await context.MediaAssets
                .AsNoTracking()
                .Where(row => chunk.Contains(row.Id))
                .Select(row => new AssetBase(
                    row.Id,
                    row.ActualFilename,
                    row.DisplayName,
                    row.MetadataVersion,
                    row.Series == null ? null : row.Series.Id,
                    row.Series == null ? null : row.Series.Kind,
                    row.Series == null ? null : row.Series.DisplayName,
                    row.Series == null ? null : row.Series.NormalizedName))
                .ToArrayAsync(cancellationToken);
            foreach (AssetBase item in loaded)
            {
                basics[item.AssetId] = item;
            }

            TermLink[] loadedAuthors = await context.MediaAssetAuthors
                .AsNoTracking()
                .Where(row => chunk.Contains(row.MediaAssetId))
                .OrderBy(row => row.MediaAssetId)
                .ThenBy(row => row.Ordinal)
                .Select(row => new TermLink(
                    row.MediaAssetId,
                    row.Author.Id,
                    row.Author.Kind,
                    row.Author.DisplayName,
                    row.Author.NormalizedName))
                .ToArrayAsync(cancellationToken);
            AddLinks(authors, loadedAuthors);

            TermLink[] loadedTags = await context.MediaAssetTags
                .AsNoTracking()
                .Where(row => chunk.Contains(row.MediaAssetId))
                .OrderBy(row => row.MediaAssetId)
                .ThenBy(row => row.Ordinal)
                .Select(row => new TermLink(
                    row.MediaAssetId,
                    row.Tag.Id,
                    row.Tag.Kind,
                    row.Tag.DisplayName,
                    row.Tag.NormalizedName))
                .ToArrayAsync(cancellationToken);
            AddLinks(tags, loadedTags);
        }

        return basics.Values.ToDictionary(
            item => item.AssetId,
            item => new AssetSnapshot(
                item.AssetId,
                item.ActualFilename,
                item.ExplicitDisplayName,
                item.SeriesId.HasValue
                    ? ToTerm(
                        item.SeriesId.Value,
                        item.SeriesKind!,
                        item.SeriesDisplayName!,
                        item.SeriesNormalizedName!)
                    : null,
                authors.TryGetValue(
                    item.AssetId,
                    out List<TaxonomyTermDto>? authorTerms)
                        ? authorTerms.ToArray()
                        : [],
                tags.TryGetValue(
                    item.AssetId,
                    out List<TaxonomyTermDto>? tagTerms)
                        ? tagTerms.ToArray()
                        : [],
                item.MetadataVersion));
    }

    private static void AddLinks(
        IDictionary<Guid, List<TaxonomyTermDto>> destination,
        IEnumerable<TermLink> links)
    {
        foreach (TermLink link in links)
        {
            if (!destination.TryGetValue(
                    link.AssetId,
                    out List<TaxonomyTermDto>? terms))
            {
                terms = [];
                destination.Add(link.AssetId, terms);
            }

            terms.Add(ToTerm(
                link.TermId,
                link.Kind,
                link.DisplayName,
                link.NormalizedName));
        }
    }

    private static TaxonomyTermDto ToTerm(
        Guid id,
        string kind,
        string displayName,
        string normalizedName) =>
        new(
            id,
            Enum.Parse<TaxonomyKind>(kind, true),
            displayName,
            normalizedName,
            0);

    private static LibraryBatchAssetDto ToEditorAsset(
        AssetSnapshot snapshot,
        LibrarySelectionEntry selection) =>
        new(
            selection,
            snapshot.ActualFilename,
            snapshot.ExplicitDisplayName,
            snapshot.Authors,
            snapshot.Series,
            snapshot.Tags);

    private static IReadOnlyList<BatchTermCoverageDto> BuildCoverage(
        IEnumerable<IReadOnlyList<TaxonomyTermDto>> selections)
    {
        IReadOnlyList<TaxonomyTermDto>[] materialized = selections.ToArray();
        var ordered = new List<TaxonomyTermDto>();
        var seen = new HashSet<Guid>();
        foreach (TaxonomyTermDto term in materialized.SelectMany(value => value))
        {
            if (seen.Add(term.Id))
            {
                ordered.Add(term);
            }
        }

        return ordered
            .Select(term => new BatchTermCoverageDto(
                term,
                materialized.Count(values => values.Any(
                    value => value.Id == term.Id))))
            .OrderBy(item => item.Term.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Term.Id)
            .ToArray();
    }

    private static async Task<IReadOnlySet<string>>
        LoadUnselectedDisplayNamesAsync(
            MediaDesktopDbContext context,
            IReadOnlyList<LibrarySelectionEntry> selection,
            CancellationToken cancellationToken)
    {
        var selected = selection.Select(item => item.AssetId).ToHashSet();
        DisplayRow[] rows = await context.MediaAssets
            .AsNoTracking()
            .Select(row => new DisplayRow(
                row.Id,
                row.ActualFilename,
                row.DisplayName))
            .ToArrayAsync(cancellationToken);
        return rows
            .Where(row => !selected.Contains(row.AssetId))
            .Select(row => row.ExplicitDisplayName
                ?? Path.GetFileNameWithoutExtension(row.ActualFilename))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string? ValidateSelectionShape(
        IReadOnlyList<LibrarySelectionEntry>? selection)
    {
        if (selection is null
            || selection.Count == 0
            || selection.Any(item => item is null
                || item.AssetId == Guid.Empty
                || item.ExpectedMetadataVersion < 0
                || item.Ordinal < 0)
            || selection.Select(item => item.AssetId).Distinct().Count()
                != selection.Count
            || selection.Select(item => item.Ordinal).Distinct().Count()
                != selection.Count)
        {
            return "library.batch.selection_invalid";
        }

        return null;
    }

    private static string? ValidateSnapshots(
        IReadOnlyList<LibrarySelectionEntry> selection,
        IReadOnlyDictionary<Guid, AssetSnapshot> snapshots)
    {
        foreach (LibrarySelectionEntry item in selection)
        {
            if (!snapshots.TryGetValue(item.AssetId, out AssetSnapshot? snapshot))
            {
                return "library.batch.selection_missing";
            }

            if (snapshot.MetadataVersion != item.ExpectedMetadataVersion)
            {
                return "library.batch.version_conflict";
            }
        }

        return null;
    }

    private static LibraryBatchPrepareResult PrepareFailure(string errorCode) =>
        new(false, null, errorCode);

    private static LibraryBatchStoreResult Failure(int count, string errorCode) =>
        new(false, 0, count, 0, null, errorCode);

    private sealed record AssetBase(
        Guid AssetId,
        string ActualFilename,
        string? ExplicitDisplayName,
        long MetadataVersion,
        Guid? SeriesId,
        string? SeriesKind,
        string? SeriesDisplayName,
        string? SeriesNormalizedName);

    private sealed record TermLink(
        Guid AssetId,
        Guid TermId,
        string Kind,
        string DisplayName,
        string NormalizedName);

    private sealed record DisplayRow(
        Guid AssetId,
        string ActualFilename,
        string? ExplicitDisplayName);

    private sealed record VersionRow(Guid AssetId, long MetadataVersion);

    private sealed record TaxonomyKindRow(Guid TermId, string Kind);

    private sealed record AssetSnapshot(
        Guid AssetId,
        string ActualFilename,
        string? ExplicitDisplayName,
        TaxonomyTermDto? Series,
        IReadOnlyList<TaxonomyTermDto> Authors,
        IReadOnlyList<TaxonomyTermDto> Tags,
        long MetadataVersion);

    private sealed record ProposedAsset(
        AssetSnapshot Snapshot,
        LibrarySelectionEntry Selection,
        MetadataSelection ProposedMetadata,
        IReadOnlyList<TaxonomyTermDto> Authors,
        TaxonomyTermDto? Series,
        IReadOnlyList<TaxonomyTermDto> Tags,
        bool MetadataChanged);

    private sealed record ProposedWrite(
        AssetSnapshot Snapshot,
        MetadataSelection ProposedMetadata,
        bool MetadataChanged,
        bool DisplayNameChanged,
        string? TargetDisplayName);

    private sealed class TaxonomyResolver
    {
        private readonly MediaDesktopDbContext _context;
        private readonly DateTimeOffset _now;
        private readonly Dictionary<Guid, TaxonomyTermDto> _byId = [];
        private readonly Dictionary<TermKey, TaxonomyTermDto> _byName = [];

        public TaxonomyResolver(
            MediaDesktopDbContext context,
            IEnumerable<AssetSnapshot> snapshots,
            DateTimeOffset now)
        {
            _context = context;
            _now = now;
            foreach (AssetSnapshot snapshot in snapshots)
            {
                foreach (TaxonomyTermDto term in snapshot.Authors)
                {
                    Add(term);
                }

                if (snapshot.Series is not null)
                {
                    Add(snapshot.Series);
                }

                foreach (TaxonomyTermDto term in snapshot.Tags)
                {
                    Add(term);
                }
            }
        }

        public TaxonomyTermDto Get(Guid id) =>
            _byId.TryGetValue(id, out TaxonomyTermDto? term)
                ? term
                : throw new LibraryBatchStoreException(
                    "library.batch.taxonomy_missing");

        public async Task ValidateSnapshotsAsync(
            IReadOnlyList<TaxonomyTermDto> snapshots,
            CancellationToken cancellationToken)
        {
            foreach (TaxonomyTermDto expected in snapshots)
            {
                TaxonomyTermDto actual;
                if (_byId.TryGetValue(expected.Id, out TaxonomyTermDto? loaded))
                {
                    actual = loaded;
                }
                else
                {
                    TaxonomyTermRow? row = await _context.TaxonomyTerms
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            item => item.Id == expected.Id,
                            cancellationToken);
                    if (row is null)
                    {
                        throw new LibraryBatchStoreException(
                            "library.batch.taxonomy_missing");
                    }

                    actual = ToTerm(
                        row.Id,
                        row.Kind,
                        row.DisplayName,
                        row.NormalizedName);
                    Add(actual);
                }

                if (actual.Kind != expected.Kind
                    || !StringComparer.Ordinal.Equals(
                        actual.DisplayName,
                        expected.DisplayName)
                    || !StringComparer.Ordinal.Equals(
                        actual.NormalizedName,
                        expected.NormalizedName))
                {
                    throw new LibraryBatchStoreException(
                        "library.batch.taxonomy_invalid");
                }
            }
        }

        public async Task<IReadOnlyList<Guid>> ResolveManyAsync(
            IReadOnlyList<BatchTaxonomyReference> references,
            TaxonomyKind expectedKind,
            CancellationToken cancellationToken)
        {
            var ids = new List<Guid>(references.Count);
            var seen = new HashSet<Guid>();
            foreach (BatchTaxonomyReference reference in references)
            {
                Guid id = await ResolveAsync(
                    reference,
                    expectedKind,
                    cancellationToken);
                if (seen.Add(id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        public async Task<Guid> ResolveAsync(
            BatchTaxonomyReference reference,
            TaxonomyKind expectedKind,
            CancellationToken cancellationToken)
        {
            if (reference is null || reference.Kind != expectedKind)
            {
                throw new LibraryBatchStoreException(
                    "library.batch.taxonomy_invalid");
            }

            if (reference.ExistingId.HasValue)
            {
                if (reference.ExistingId.Value == Guid.Empty
                    || reference.NewName is not null)
                {
                    throw new LibraryBatchStoreException(
                        "library.batch.taxonomy_invalid");
                }

                Guid id = reference.ExistingId.Value;
                if (!_byId.TryGetValue(id, out TaxonomyTermDto? term))
                {
                    TaxonomyTermRow? row = await _context.TaxonomyTerms
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            item => item.Id == id,
                            cancellationToken);
                    if (row is null)
                    {
                        throw new LibraryBatchStoreException(
                            "library.batch.taxonomy_missing");
                    }

                    term = ToTerm(
                        row.Id,
                        row.Kind,
                        row.DisplayName,
                        row.NormalizedName);
                    Add(term);
                }

                if (term.Kind != expectedKind)
                {
                    throw new LibraryBatchStoreException(
                        "library.batch.taxonomy_missing");
                }

                return term.Id;
            }

            TaxonomyName name;
            try
            {
                name = TaxonomyName.Create(reference.NewName!);
            }
            catch (ArgumentException)
            {
                throw new LibraryBatchStoreException(
                    "library.batch.taxonomy_invalid");
            }

            var key = new TermKey(expectedKind, name.Normalized);
            if (_byName.TryGetValue(key, out TaxonomyTermDto? matched))
            {
                return matched.Id;
            }

            string kind = expectedKind.ToString().ToLowerInvariant();
            TaxonomyTermRow? existing = await _context.TaxonomyTerms
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    row => row.Kind == kind
                        && row.NormalizedName == name.Normalized,
                    cancellationToken);
            if (existing is not null)
            {
                TaxonomyTermDto term = ToTerm(
                    existing.Id,
                    existing.Kind,
                    existing.DisplayName,
                    existing.NormalizedName);
                Add(term);
                return term.Id;
            }

            var created = new TaxonomyTermRow
            {
                Id = Guid.NewGuid(),
                Kind = kind,
                DisplayName = name.Display,
                NormalizedName = name.Normalized,
                CreatedAtUtc = _now,
                UpdatedAtUtc = _now
            };
            _context.TaxonomyTerms.Add(created);
            var createdTerm = new TaxonomyTermDto(
                created.Id,
                expectedKind,
                name.Display,
                name.Normalized,
                0);
            Add(createdTerm);
            return created.Id;
        }

        private void Add(TaxonomyTermDto term)
        {
            var key = new TermKey(term.Kind, term.NormalizedName);
            if (_byId.TryGetValue(term.Id, out TaxonomyTermDto? byId)
                && byId != term)
            {
                throw new LibraryBatchStoreException(
                    "library.batch.taxonomy_invalid");
            }

            if (_byName.TryGetValue(key, out TaxonomyTermDto? byName)
                && byName.Id != term.Id)
            {
                throw new LibraryBatchStoreException(
                    "library.batch.taxonomy_invalid");
            }

            _byId.TryAdd(term.Id, term);
            _byName.TryAdd(key, term);
        }

        private readonly record struct TermKey(
            TaxonomyKind Kind,
            string NormalizedName);
    }

    private sealed class LibraryBatchStoreException(string errorCode)
        : Exception
    {
        public string ErrorCode { get; } = errorCode;
    }
}
