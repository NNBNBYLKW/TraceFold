using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfBatchMetadataStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory) : IBatchMetadataStore
{
    private const int ReadBatchSize = 200;

    public async Task<BatchMetadataEditorDto?> PrepareAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        IReadOnlyDictionary<Guid, InboxDraftSnapshot> snapshots =
            await LoadAllSnapshotsAsync(context, selection, cancellationToken);
        if (ValidateEverySelection(selection, snapshots) is not null)
        {
            return null;
        }

        IReadOnlyList<InboxDraftSnapshot> selected = selection
            .OrderBy(entry => entry.Ordinal)
            .Select(entry => snapshots[entry.InboxItemId])
            .ToArray();
        IReadOnlyList<BatchTermCoverageDto> authors = await BuildCoverageAsync(
            context,
            selected.SelectMany(snapshot => snapshot.AuthorIds),
            selected.Select(snapshot => snapshot.AuthorIds),
            cancellationToken);
        IReadOnlyList<BatchTermCoverageDto> tags = await BuildCoverageAsync(
            context,
            selected.SelectMany(snapshot => snapshot.TagIds),
            selected.Select(snapshot => snapshot.TagIds),
            cancellationToken);
        return new BatchMetadataEditorDto(selection.Count, authors, tags);
    }

    public async Task<BatchMetadataResult> ApplyAsync(
        BatchMetadataWrite write,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            IReadOnlyDictionary<Guid, InboxDraftSnapshot> snapshots =
                await LoadAllSnapshotsAsync(
                    context, write.Selection, cancellationToken);
            BatchMetadataResult? rejection = ValidateEverySelection(
                write.Selection, snapshots);
            if (rejection is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return rejection;
            }

            ResolvedBatchMetadataEdits edits = await ResolveEditsAsync(
                context, write, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            int changed = 0;
            foreach (InboxSelectionEntry[] chunk in write.Selection
                         .OrderBy(item => item.Ordinal)
                         .Chunk(ReadBatchSize))
            {
                changed += await ApplyChunkAsync(
                    context, chunk, snapshots, edits, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                context.ChangeTracker.Clear();
            }

            await transaction.CommitAsync(cancellationToken);
            return new BatchMetadataResult(
                true,
                changed,
                write.Selection.Count - changed,
                write.Selection.Count,
                null);
        }
        catch (BatchMetadataStoreException error)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return Failure(write.Selection.Count, error.ErrorCode);
        }
        catch (Exception error) when (error is DbUpdateException or SqliteException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return Failure(
                write.Selection.Count,
                "batch.metadata.persistence_failed");
        }
    }

    private static BatchMetadataResult Failure(int count, string errorCode) =>
        new(false, 0, 0, count, errorCode);

    private static async Task<IReadOnlyDictionary<Guid, InboxDraftSnapshot>>
        LoadAllSnapshotsAsync(
            MediaDesktopDbContext context,
            IReadOnlyList<InboxSelectionEntry> selection,
            CancellationToken cancellationToken)
    {
        Guid[] ids = selection
            .Select(entry => entry.InboxItemId)
            .Distinct()
            .ToArray();
        var basics = new Dictionary<Guid, InboxDraftBase>();
        var authors = new Dictionary<Guid, List<Guid>>();
        var tags = new Dictionary<Guid, List<Guid>>();
        foreach (Guid[] chunk in ids.Chunk(ReadBatchSize))
        {
            InboxDraftBase[] loaded = await context.InboxItems
                .AsNoTracking()
                .Where(row => chunk.Contains(row.Id) && row.MetadataDraft != null)
                .Select(row => new InboxDraftBase(
                    row.Id,
                    row.State,
                    row.IsRetryable,
                    row.RequiredCapability,
                    row.MetadataDraft!.DisplayName,
                    row.MetadataDraft.SeriesId,
                    row.MetadataDraft.Version))
                .ToArrayAsync(cancellationToken);
            foreach (InboxDraftBase item in loaded)
            {
                basics[item.InboxItemId] = item;
            }

            DraftLink[] loadedAuthors = await context.InboxDraftAuthors
                .AsNoTracking()
                .Where(row => chunk.Contains(row.InboxItemId))
                .OrderBy(row => row.InboxItemId)
                .ThenBy(row => row.Ordinal)
                .Select(row => new DraftLink(row.InboxItemId, row.AuthorId))
                .ToArrayAsync(cancellationToken);
            AddLinks(authors, loadedAuthors);

            DraftLink[] loadedTags = await context.InboxDraftTags
                .AsNoTracking()
                .Where(row => chunk.Contains(row.InboxItemId))
                .OrderBy(row => row.InboxItemId)
                .ThenBy(row => row.Ordinal)
                .Select(row => new DraftLink(row.InboxItemId, row.TagId))
                .ToArrayAsync(cancellationToken);
            AddLinks(tags, loadedTags);
        }

        return basics.Values.ToDictionary(
            item => item.InboxItemId,
            item => new InboxDraftSnapshot(
                item.InboxItemId,
                item.State,
                item.IsRetryable,
                item.RequiredCapability,
                item.DisplayName,
                item.SeriesId,
                authors.TryGetValue(item.InboxItemId, out List<Guid>? authorIds)
                    ? authorIds.ToArray()
                    : [],
                tags.TryGetValue(item.InboxItemId, out List<Guid>? tagIds)
                    ? tagIds.ToArray()
                    : [],
                item.MetadataVersion));
    }

    private static void AddLinks(
        IDictionary<Guid, List<Guid>> destination,
        IEnumerable<DraftLink> links)
    {
        foreach (DraftLink link in links)
        {
            if (!destination.TryGetValue(link.InboxItemId, out List<Guid>? values))
            {
                values = [];
                destination.Add(link.InboxItemId, values);
            }

            values.Add(link.TermId);
        }
    }

    private static BatchMetadataResult? ValidateEverySelection(
        IReadOnlyList<InboxSelectionEntry> selection,
        IReadOnlyDictionary<Guid, InboxDraftSnapshot> snapshots)
    {
        if (selection.Count == 0
            || selection.Any(entry => entry is null
                || entry.InboxItemId == Guid.Empty
                || entry.ExpectedMetadataVersion < 0
                || entry.Ordinal < 0)
            || selection.Select(entry => entry.InboxItemId).Distinct().Count()
                != selection.Count
            || selection.Select(entry => entry.Ordinal).Distinct().Count()
                != selection.Count)
        {
            return Failure(selection.Count, "batch.metadata.selection_invalid");
        }

        foreach (InboxSelectionEntry entry in selection)
        {
            if (!snapshots.TryGetValue(
                    entry.InboxItemId, out InboxDraftSnapshot? snapshot))
            {
                return Failure(selection.Count, "batch.metadata.selection_missing");
            }

            if (snapshot.MetadataVersion != entry.ExpectedMetadataVersion)
            {
                return Failure(selection.Count, "batch.metadata.version_conflict");
            }

            InboxAction actions = InboxActionPolicy.For(
                snapshot.State,
                snapshot.IsRetryable,
                snapshot.RequiredCapability);
            if (!actions.HasFlag(InboxAction.EditMetadata))
            {
                return Failure(selection.Count, "batch.metadata.action_not_allowed");
            }
        }

        return null;
    }

    private static async Task<ResolvedBatchMetadataEdits> ResolveEditsAsync(
        MediaDesktopDbContext context,
        BatchMetadataWrite write,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<Guid> authors = await ResolveTokensAsync(
            context,
            write.Authors,
            TaxonomyKind.Author,
            now,
            cancellationToken);
        Guid? series = write.Series is null
            ? null
            : await ResolveTokenOfKindAsync(
                context,
                write.Series,
                TaxonomyKind.Series,
                now,
                cancellationToken);
        IReadOnlyList<Guid> tags = await ResolveTokensAsync(
            context,
            write.Tags,
            TaxonomyKind.Tag,
            now,
            cancellationToken);
        return new ResolvedBatchMetadataEdits(
            new(write.AuthorOperation, authors),
            new(write.SeriesOperation, series),
            new(write.TagOperation, tags));
    }

    private static async Task<IReadOnlyList<Guid>> ResolveTokensAsync(
        MediaDesktopDbContext context,
        IReadOnlyList<BatchTaxonomyToken> tokens,
        TaxonomyKind expectedKind,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var identities = new HashSet<TokenIdentity>();
        var resolved = new List<Guid>(tokens.Count);
        foreach (BatchTaxonomyToken token in tokens)
        {
            ValidateTokenKind(token, expectedKind);
            TokenIdentity identity = Identity(token);
            if (!identities.Add(identity))
            {
                continue;
            }

            resolved.Add(await ResolveTokenAsync(
                context, token, now, cancellationToken));
        }

        return resolved;
    }

    private static async Task<Guid> ResolveTokenOfKindAsync(
        MediaDesktopDbContext context,
        BatchTaxonomyToken token,
        TaxonomyKind expectedKind,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ValidateTokenKind(token, expectedKind);
        return await ResolveTokenAsync(context, token, now, cancellationToken);
    }

    private static void ValidateTokenKind(
        BatchTaxonomyToken token,
        TaxonomyKind expectedKind)
    {
        if (token is null || token.Kind != expectedKind)
        {
            throw new BatchMetadataStoreException(
                "batch.metadata.taxonomy_invalid");
        }
    }

    private static TokenIdentity Identity(BatchTaxonomyToken token)
    {
        if (token.ExistingId is Guid id)
        {
            if (id == Guid.Empty || token.NewName is not null)
            {
                throw new BatchMetadataStoreException(
                    "batch.metadata.taxonomy_invalid");
            }

            return new TokenIdentity(token.Kind, id, null);
        }

        TaxonomyName name = token.NewName
            ?? throw new BatchMetadataStoreException(
                "batch.metadata.taxonomy_invalid");
        return new TokenIdentity(token.Kind, null, name.Normalized);
    }

    private static async Task<Guid> ResolveTokenAsync(
        MediaDesktopDbContext context,
        BatchTaxonomyToken token,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string kind = token.Kind.ToString().ToLowerInvariant();
        if (token.ExistingId is Guid id)
        {
            TaxonomyTermRow? existing = await context.TaxonomyTerms
                .SingleOrDefaultAsync(
                    row => row.Id == id && row.Kind == kind,
                    cancellationToken);
            return existing?.Id
                ?? throw new BatchMetadataStoreException(
                    "batch.metadata.taxonomy_missing");
        }

        TaxonomyName name = token.NewName
            ?? throw new BatchMetadataStoreException(
                "batch.metadata.taxonomy_invalid");
        TaxonomyTermRow? matched = await context.TaxonomyTerms
            .SingleOrDefaultAsync(
                row => row.Kind == kind
                    && row.NormalizedName == name.Normalized,
                cancellationToken);
        if (matched is not null)
        {
            return matched.Id;
        }

        var created = new TaxonomyTermRow
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            DisplayName = name.Display,
            NormalizedName = name.Normalized,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.TaxonomyTerms.Add(created);
        return created.Id;
    }

    private static async Task<int> ApplyChunkAsync(
        MediaDesktopDbContext context,
        IReadOnlyList<InboxSelectionEntry> chunk,
        IReadOnlyDictionary<Guid, InboxDraftSnapshot> snapshots,
        ResolvedBatchMetadataEdits edits,
        CancellationToken cancellationToken)
    {
        int changed = 0;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (InboxSelectionEntry entry in chunk)
        {
            InboxDraftSnapshot snapshot = snapshots[entry.InboxItemId];
            MetadataSelection current = MetadataSelection.Create(
                snapshot.DisplayName,
                snapshot.AuthorIds,
                snapshot.SeriesId,
                snapshot.TagIds);
            MetadataSelection updated = BatchMetadataMutation.Apply(current, edits);
            if (ReferenceEquals(current, updated))
            {
                continue;
            }

            int affected = await context.MetadataDrafts
                .Where(row => row.InboxItemId == entry.InboxItemId
                    && row.Version == entry.ExpectedMetadataVersion)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(row => row.DisplayName, updated.DisplayName)
                        .SetProperty(row => row.SeriesId, updated.SeriesId)
                        .SetProperty(
                            row => row.Version,
                            entry.ExpectedMetadataVersion + 1)
                        .SetProperty(row => row.UpdatedAtUtc, now),
                    cancellationToken);
            if (affected != 1)
            {
                throw new BatchMetadataStoreException(
                    "batch.metadata.version_conflict");
            }

            await context.InboxDraftAuthors
                .Where(row => row.InboxItemId == entry.InboxItemId)
                .ExecuteDeleteAsync(cancellationToken);
            await context.InboxDraftTags
                .Where(row => row.InboxItemId == entry.InboxItemId)
                .ExecuteDeleteAsync(cancellationToken);
            context.InboxDraftAuthors.AddRange(updated.AuthorIds.Select(
                (id, ordinal) => new InboxDraftAuthorRow
                {
                    InboxItemId = entry.InboxItemId,
                    AuthorId = id,
                    Ordinal = ordinal
                }));
            context.InboxDraftTags.AddRange(updated.TagIds.Select(
                (id, ordinal) => new InboxDraftTagRow
                {
                    InboxItemId = entry.InboxItemId,
                    TagId = id,
                    Ordinal = ordinal
                }));
            changed++;
        }

        return changed;
    }

    private static async Task<IReadOnlyList<BatchTermCoverageDto>> BuildCoverageAsync(
        MediaDesktopDbContext context,
        IEnumerable<Guid> allIds,
        IEnumerable<IReadOnlyList<Guid>> selections,
        CancellationToken cancellationToken)
    {
        Guid[] ids = allIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var terms = new Dictionary<Guid, TaxonomyTermDto>();
        foreach (Guid[] chunk in ids.Chunk(ReadBatchSize))
        {
            TaxonomyTermDto[] loaded = await context.TaxonomyTerms
                .AsNoTracking()
                .Where(row => chunk.Contains(row.Id))
                .Select(row => new TaxonomyTermDto(
                    row.Id,
                    Enum.Parse<TaxonomyKind>(row.Kind, true),
                    row.DisplayName,
                    row.NormalizedName,
                    0))
                .ToArrayAsync(cancellationToken);
            foreach (TaxonomyTermDto term in loaded)
            {
                terms[term.Id] = term;
            }
        }

        Dictionary<Guid, int> coverage = ids.ToDictionary(id => id, _ => 0);
        foreach (IReadOnlyList<Guid> selectedIds in selections)
        {
            foreach (Guid id in selectedIds.Distinct())
            {
                coverage[id]++;
            }
        }

        return ids
            .Where(terms.ContainsKey)
            .Select(id => new BatchTermCoverageDto(terms[id], coverage[id]))
            .OrderBy(item => item.Term.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record InboxDraftBase(
        Guid InboxItemId,
        InboxItemState State,
        bool IsRetryable,
        string? RequiredCapability,
        string? DisplayName,
        Guid? SeriesId,
        long MetadataVersion);

    private sealed record DraftLink(Guid InboxItemId, Guid TermId);

    private sealed record InboxDraftSnapshot(
        Guid InboxItemId,
        InboxItemState State,
        bool IsRetryable,
        string? RequiredCapability,
        string? DisplayName,
        Guid? SeriesId,
        IReadOnlyList<Guid> AuthorIds,
        IReadOnlyList<Guid> TagIds,
        long MetadataVersion);

    private readonly record struct TokenIdentity(
        TaxonomyKind Kind,
        Guid? ExistingId,
        string? NormalizedName);

    private sealed class BatchMetadataStoreException(string errorCode) : Exception
    {
        public string ErrorCode { get; } = errorCode;
    }
}
