using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfConflictCaseStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory,
    TimeProvider timeProvider) : IConflictCaseStore
{
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public async Task<IReadOnlyList<RegisteredConflictCandidate>>
        FindRegisteredCandidatesAsync(
            string targetFilename,
            string sha256,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFilename);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        string normalizedTarget = targetFilename.ToUpperInvariant();
        string normalizedSha = sha256.ToUpperInvariant();
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        MediaAssetRow[] assets = await context.MediaAssets
            .AsNoTracking()
            .AsSplitQuery()
            .Include(asset => asset.Authors)
                .ThenInclude(link => link.Author)
            .Include(asset => asset.Tags)
                .ThenInclude(link => link.Tag)
            .Include(asset => asset.Series)
            .Where(asset => asset.NormalizedRelativePath == normalizedTarget
                || asset.Sha256 == normalizedSha)
            .ToArrayAsync(cancellationToken);

        return assets
            .Select(asset => ToCandidate(asset, normalizedTarget, normalizedSha))
            .OrderByDescending(match =>
                match.DatabaseReason.HasFlag(ConflictReason.Name))
            .ThenBy(match => match.ActualFilename, StringComparer.OrdinalIgnoreCase)
            .ThenBy(match => match.AssetId)
            .ToArray();
    }

    public async Task<ConflictCaseRecord> ReplaceOpenAsync(
        Guid inboxItemId,
        ConflictInputSnapshot input,
        IReadOnlyList<ConflictMatchRecord> matches,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(matches);
        Guid caseId = Guid.NewGuid();
        _ = ConflictCase.Open(
            caseId,
            inboxItemId,
            input.Sha256,
            input.TargetFilename,
            matches.Select(match => match.Id).ToArray());

        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            await using MediaDesktopDbContext context =
                await contextFactory.CreateDbContextAsync(cancellationToken);
            await using IDbContextTransaction transaction =
                await context.Database.BeginTransactionAsync(cancellationToken);
            InboxItemRow item = await context.InboxItems.SingleAsync(
                row => row.Id == inboxItemId,
                cancellationToken);
            ConflictCaseRow? previous = await context.ConflictCases
                .Include(row => row.Matches)
                .SingleOrDefaultAsync(
                    row => row.ActiveInboxItemId == inboxItemId,
                    cancellationToken);
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (previous is not null)
            {
                ConflictCase previousAggregate = Rehydrate(previous);
                previousAggregate.Invalidate("conflict.replaced_by_refresh");
                Apply(previous, previousAggregate, now);
                previous.ActiveInboxItemId = null;
                previous.ResolvedAtUtc = now;
            }

            var row = new ConflictCaseRow
            {
                Id = caseId,
                InboxItemId = inboxItemId,
                InboxItem = item,
                ActiveInboxItemId = inboxItemId,
                Status = ConflictCaseStatus.Open,
                InputPath = input.FullPath,
                InputSize = input.Size,
                InputLastWriteTimeUtc = input.LastWriteTimeUtc,
                InputSha256 = input.Sha256.ToUpperInvariant(),
                TargetFilename = input.TargetFilename,
                MetadataVersion = input.MetadataVersion,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            foreach (ConflictMatchRecord match in matches)
            {
                row.Matches.Add(ToRow(row, match));
            }

            item.State = InboxItemState.ConflictBlocked;
            item.RequiredCapability = null;
            item.ErrorCode = "ingest.conflict";
            item.IsRetryable = false;
            item.UpdatedAtUtc = now;
            context.ConflictCases.Add(row);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToRecord(row);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<ConflictCaseRecord?> LoadAsync(
        Guid conflictCaseId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        ConflictCaseRow? row = await context.ConflictCases
            .AsNoTracking()
            .Include(item => item.Matches)
            .SingleOrDefaultAsync(
                item => item.Id == conflictCaseId,
                cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<ConflictCaseRecord?> LoadOpenForInboxAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        ConflictCaseRow? row = await context.ConflictCases
            .AsNoTracking()
            .Include(item => item.Matches)
            .SingleOrDefaultAsync(
                item => item.ActiveInboxItemId == inboxItemId
                    && item.OperationId == null
                    && (item.Status == ConflictCaseStatus.Open
                        || item.Status == ConflictCaseStatus.Chosen),
                cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public Task ChooseAsync(
        Guid conflictCaseId,
        ConflictResolution resolution,
        Guid? matchId,
        CancellationToken cancellationToken) =>
        MutateAsync(
            conflictCaseId,
            (aggregate, _, _) => aggregate.Choose(resolution, matchId),
            cancellationToken);

    public Task SkipAsync(
        Guid conflictCaseId,
        CancellationToken cancellationToken) =>
        MutateAsync(
            conflictCaseId,
            (aggregate, row, now) =>
            {
                aggregate.Skip();
                row.ActiveInboxItemId = null;
                row.ResolvedAtUtc = now;
                ReturnItemToReady(row.InboxItem, now);
            },
            cancellationToken);

    public Task InvalidateAsync(
        Guid conflictCaseId,
        string reason,
        CancellationToken cancellationToken) =>
        MutateAsync(
            conflictCaseId,
            (aggregate, row, now) =>
            {
                aggregate.Invalidate(reason);
                row.ActiveInboxItemId = null;
                row.ResolvedAtUtc = now;
                ReturnItemToReady(row.InboxItem, now);
            },
            cancellationToken);

    private async Task MutateAsync(
        Guid conflictCaseId,
        Action<ConflictCase, ConflictCaseRow, DateTimeOffset> mutation,
        CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            await using MediaDesktopDbContext context =
                await contextFactory.CreateDbContextAsync(cancellationToken);
            await using IDbContextTransaction transaction =
                await context.Database.BeginTransactionAsync(cancellationToken);
            ConflictCaseRow? row = await context.ConflictCases
                .Include(item => item.InboxItem)
                .Include(item => item.Matches)
                .SingleOrDefaultAsync(
                    item => item.Id == conflictCaseId
                        && item.ActiveInboxItemId != null
                        && item.OperationId == null,
                    cancellationToken);
            if (row is null)
            {
                throw new InvalidOperationException(
                    "The conflict case is not active.");
            }

            ConflictCase aggregate = Rehydrate(row);
            DateTimeOffset now = timeProvider.GetUtcNow();
            mutation(aggregate, row, now);
            Apply(row, aggregate, now);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private static RegisteredConflictCandidate ToCandidate(
        MediaAssetRow asset,
        string normalizedTarget,
        string normalizedSha)
    {
        ConflictReason reason = ConflictReason.None;
        if (StringComparer.Ordinal.Equals(
                asset.NormalizedRelativePath,
                normalizedTarget))
        {
            reason |= ConflictReason.Name;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(asset.Sha256, normalizedSha))
        {
            reason |= ConflictReason.Content;
        }

        return new RegisteredConflictCandidate(
            asset.Id,
            asset.MetadataVersion,
            asset.RelativePath,
            asset.ActualFilename,
            asset.DisplayName,
            asset.MediaKind,
            asset.Size,
            asset.Sha256,
            reason,
            new ConflictMetadataSnapshot(
                asset.DisplayName,
                asset.Authors
                    .OrderBy(link => link.Ordinal)
                    .Select(link => link.Author.DisplayName)
                    .ToArray(),
                asset.Series?.DisplayName,
                asset.Tags
                    .OrderBy(link => link.Ordinal)
                    .Select(link => link.Tag.DisplayName)
                    .ToArray()));
    }

    private static ConflictMatchRow ToRow(
        ConflictCaseRow conflict,
        ConflictMatchRecord match) =>
        new()
        {
            Id = match.Id,
            ConflictCaseId = conflict.Id,
            ConflictCase = conflict,
            Reason = match.Reason,
            IsRegistered = match.IsRegistered,
            AssetId = match.AssetId,
            AssetMetadataVersion = match.AssetMetadataVersion,
            RelativePath = match.RelativePath,
            ActualFilename = match.ActualFilename,
            DisplayName = match.DisplayName,
            MediaKind = match.MediaKind,
            Size = match.Size,
            LastWriteTimeUtc = match.LastWriteTimeUtc,
            Sha256 = match.Sha256.ToUpperInvariant(),
            MetadataSnapshotJson = JsonSerializer.Serialize(match.Metadata),
            TechnicalSnapshotJson = JsonSerializer.Serialize(match.Technical)
        };

    private static ConflictCase Rehydrate(ConflictCaseRow row)
    {
        ConflictCase aggregate = ConflictCase.Open(
            row.Id,
            row.InboxItemId,
            row.InputSha256,
            row.TargetFilename,
            row.Matches.Select(match => match.Id).ToArray());
        if (row.Status == ConflictCaseStatus.Chosen)
        {
            aggregate.Choose(
                row.Resolution
                    ?? throw new InvalidOperationException(
                        "A chosen conflict case has no resolution."),
                row.SelectedMatchId);
        }
        else if (row.Status != ConflictCaseStatus.Open)
        {
            throw new InvalidOperationException(
                "Only an open or chosen conflict case can be mutated.");
        }

        return aggregate;
    }

    private static void Apply(
        ConflictCaseRow row,
        ConflictCase aggregate,
        DateTimeOffset now)
    {
        row.Status = aggregate.Status;
        row.Resolution = aggregate.Resolution;
        row.SelectedMatchId = aggregate.SelectedMatchId;
        row.InvalidationReason = aggregate.InvalidationReason;
        row.UpdatedAtUtc = now;
    }

    private static void ReturnItemToReady(
        InboxItemRow item,
        DateTimeOffset now)
    {
        item.State = InboxItemState.ReadyToTag;
        item.RequiredCapability = null;
        item.ErrorCode = null;
        item.IsRetryable = false;
        item.UpdatedAtUtc = now;
    }

    private static ConflictCaseRecord ToRecord(ConflictCaseRow row) =>
        new(
            row.Id,
            row.InboxItemId,
            row.Status,
            new ConflictInputSnapshot(
                row.InputPath,
                row.InputSize,
                row.InputLastWriteTimeUtc,
                row.InputSha256,
                row.TargetFilename,
                row.MetadataVersion),
            row.Resolution,
            row.SelectedMatchId,
            row.InvalidationReason,
            row.Matches
                .OrderByDescending(match =>
                    match.Reason.HasFlag(ConflictReason.Name))
                .ThenBy(match => match.ActualFilename, StringComparer.OrdinalIgnoreCase)
                .ThenBy(match => match.Id)
                .Select(ToRecord)
                .ToArray());

    private static ConflictMatchRecord ToRecord(ConflictMatchRow row) =>
        new(
            row.Id,
            row.Reason,
            row.IsRegistered,
            row.AssetId,
            row.AssetMetadataVersion,
            row.RelativePath,
            row.ActualFilename,
            row.DisplayName,
            row.MediaKind,
            row.Size,
            row.LastWriteTimeUtc,
            row.Sha256,
            JsonSerializer.Deserialize<ConflictMetadataSnapshot>(
                    row.MetadataSnapshotJson)
                ?? throw new InvalidOperationException(
                    "The conflict metadata snapshot is invalid."),
            JsonSerializer.Deserialize<ConflictTechnicalMetadata>(
                    row.TechnicalSnapshotJson)
                ?? throw new InvalidOperationException(
                    "The conflict technical snapshot is invalid."));
}
