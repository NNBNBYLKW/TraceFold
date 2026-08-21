using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfInboxScanStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory)
    : IInboxScanStore
{
    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> LoadFirstObservedAsync(
        IReadOnlySet<string> normalizedPaths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(normalizedPaths);
        if (normalizedPaths.Count == 0)
        {
            return new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        }

        string[] paths = normalizedPaths.ToArray();
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        SourceFileRow[] rows = await context.SourceFiles
            .AsNoTracking()
            .Where(row => row.ActivePathKey != null && paths.Contains(row.ActivePathKey))
            .ToArrayAsync(cancellationToken);
        return rows.ToDictionary(
            row => row.ActivePathKey!,
            row => row.FirstObservedAtUtc,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ScanPersistenceResult> ReconcileAsync(
        ScanReconciliation reconciliation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reconciliation);
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);

        List<SourceFileRow> activeSources = await context.SourceFiles
            .Where(row => row.ActivePathKey != null)
            .Include(row => row.InboxItem)
            .ToListAsync(cancellationToken);
        var sourcesByPath = activeSources.ToDictionary(
            row => row.ActivePathKey!,
            StringComparer.OrdinalIgnoreCase);
        var createdItemIds = new HashSet<Guid>();
        var updatedItemIds = new HashSet<Guid>();

        foreach (CandidateGroup group in reconciliation.Groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] groupPaths = group.Sources
                .Select(source => NormalizePath(source.FullPath))
                .ToArray();
            InboxItemRow[] matchedItems = groupPaths
                .Where(sourcesByPath.ContainsKey)
                .Select(path => sourcesByPath[path].InboxItem)
                .DistinctBy(item => item.Id)
                .ToArray();
            if (matchedItems.Length > 1)
            {
                throw new InvalidOperationException(
                    "A candidate group matched more than one active inbox item.");
            }

            bool created = matchedItems.Length == 0;
            InboxItemRow item;
            if (created)
            {
                item = new InboxItemRow
                {
                    Id = Guid.NewGuid(),
                    State = InboxItemState.WaitingForStability,
                    MediaKind = group.MediaKind,
                    ActualFilename = group.Sources[0].Filename,
                    GroupingRule = group.RuleName,
                    CreatedAtUtc = reconciliation.ObservedAtUtc,
                    UpdatedAtUtc = reconciliation.ObservedAtUtc
                };
                context.InboxItems.Add(item);
                context.MetadataDrafts.Add(new MetadataDraftRow
                {
                    InboxItemId = item.Id,
                    InboxItem = item,
                    Version = 0,
                    UpdatedAtUtc = reconciliation.ObservedAtUtc
                });
                createdItemIds.Add(item.Id);
            }
            else
            {
                item = matchedItems[0];
                updatedItemIds.Add(item.Id);
            }

            item.MediaKind = group.MediaKind;
            item.ActualFilename = group.Sources[0].Filename;
            item.GroupingRule = group.RuleName;
            item.UpdatedAtUtc = reconciliation.ObservedAtUtc;

            bool sourceChanged = false;
            var currentGroupSources = new List<SourceFileRow>(group.Sources.Count);
            for (int index = 0; index < group.Sources.Count; index++)
            {
                ObservedFile observed = group.Sources[index];
                string pathKey = groupPaths[index];
                if (!sourcesByPath.TryGetValue(pathKey, out SourceFileRow? row))
                {
                    row = new SourceFileRow
                    {
                        Id = Guid.NewGuid(),
                        InboxItemId = item.Id,
                        InboxItem = item,
                        NormalizedPath = pathKey,
                        ActivePathKey = pathKey,
                        Filename = observed.Filename,
                        Extension = observed.Extension,
                        Size = observed.Fingerprint.Size,
                        LastWriteTimeUtc = observed.Fingerprint.LastWriteTimeUtc,
                        FirstObservedAtUtc = observed.FirstObservedAtUtc,
                        StableObservationCount = 1,
                        IsPresent = true,
                        IsPrimary = index == 0
                    };
                    context.SourceFiles.Add(row);
                    activeSources.Add(row);
                    sourcesByPath.Add(pathKey, row);
                    sourceChanged = !created;
                }
                else
                {
                    if (row.InboxItemId != item.Id)
                    {
                        throw new InvalidOperationException(
                            "An active source path belongs to another inbox item.");
                    }

                    bool unchanged = row.IsPresent
                        && row.Size == observed.Fingerprint.Size
                        && row.LastWriteTimeUtc == observed.Fingerprint.LastWriteTimeUtc;
                    if (unchanged)
                    {
                        row.StableObservationCount++;
                    }
                    else
                    {
                        row.StableObservationCount = 1;
                        row.Sha256 = null;
                        sourceChanged = true;
                    }

                    row.NormalizedPath = pathKey;
                    row.Filename = observed.Filename;
                    row.Extension = observed.Extension;
                    row.Size = observed.Fingerprint.Size;
                    row.LastWriteTimeUtc = observed.Fingerprint.LastWriteTimeUtc;
                    row.IsPresent = true;
                    row.IsPrimary = index == 0;
                }

                currentGroupSources.Add(row);
            }

            bool scanManagedState = item.State is
                InboxItemState.WaitingForStability or
                InboxItemState.Analyzing or
                InboxItemState.ReadyToTag or
                InboxItemState.RequiresProcessing or
                InboxItemState.SourceMissing;
            if (sourceChanged || scanManagedState)
            {
                ApplyCandidateState(item, group, currentGroupSources, sourceChanged);
            }
        }

        foreach (SourceFileRow source in activeSources)
        {
            if (source.ActivePathKey is null
                || reconciliation.ObservedPaths.Contains(source.ActivePathKey))
            {
                continue;
            }

            source.IsPresent = false;
            if (source.InboxItem.State is not
                (InboxItemState.Ingesting or
                 InboxItemState.CleanupPending or
                 InboxItemState.Completed))
            {
                source.InboxItem.State = InboxItemState.SourceMissing;
                source.InboxItem.RequiredCapability = null;
                source.InboxItem.ErrorCode = null;
                source.InboxItem.IsRetryable = false;
                source.InboxItem.UpdatedAtUtc = reconciliation.ObservedAtUtc;
                if (!createdItemIds.Contains(source.InboxItemId))
                {
                    updatedItemIds.Add(source.InboxItemId);
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        bool waiting = await context.InboxItems
            .AsNoTracking()
            .AnyAsync(
                row => row.State == InboxItemState.WaitingForStability,
                cancellationToken);
        return new ScanPersistenceResult(
            createdItemIds.Count,
            updatedItemIds.Count,
            waiting);
    }

    public async Task<bool> HasWaitingCandidatesAsync(CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.InboxItems
            .AsNoTracking()
            .AnyAsync(
                row => row.State == InboxItemState.WaitingForStability,
                cancellationToken);
    }

    public async Task ClearMissingAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        InboxItemRow? item = await context.InboxItems
            .Include(row => row.IngestOperations)
            .SingleOrDefaultAsync(row => row.Id == inboxItemId, cancellationToken);
        if (item is null || item.State != InboxItemState.SourceMissing)
        {
            return;
        }

        if (item.IngestOperations.Count > 0)
        {
            throw new InvalidOperationException(
                "An inbox item with ingest history cannot be cleared as never-ingested.");
        }

        context.InboxItems.Remove(item);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static void ApplyCandidateState(
        InboxItemRow item,
        CandidateGroup group,
        IReadOnlyList<SourceFileRow> sources,
        bool sourceChanged)
    {
        bool allStable = sources.All(source => source.StableObservationCount >= 2);
        item.RequiredCapability = null;
        item.ErrorCode = null;
        item.IsRetryable = false;
        if (sourceChanged || !allStable
            || group.Disposition == CandidateDisposition.WaitingForCompanion)
        {
            item.State = InboxItemState.WaitingForStability;
            return;
        }

        if (group.Disposition == CandidateDisposition.RequiresProcessing)
        {
            item.State = InboxItemState.RequiresProcessing;
            item.RequiredCapability = group.RequiredCapability;
            return;
        }

        item.State = InboxItemState.ReadyToTag;
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).ToUpperInvariant();
}
