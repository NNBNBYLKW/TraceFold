using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfIngestStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory) : IIngestStore
{
    public async Task<IngestCandidate?> LoadCandidateAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        InboxItemRow? item = await context.InboxItems
            .AsNoTracking()
            .Include(row => row.SourceFiles)
            .Include(row => row.MetadataDraft)
            .SingleOrDefaultAsync(row => row.Id == inboxItemId, cancellationToken);
        if (item?.MetadataDraft is null)
        {
            return null;
        }

        Guid[] authors = await context.InboxDraftAuthors
            .AsNoTracking()
            .Where(row => row.InboxItemId == inboxItemId)
            .OrderBy(row => row.Ordinal)
            .Select(row => row.AuthorId)
            .ToArrayAsync(cancellationToken);
        Guid[] tags = await context.InboxDraftTags
            .AsNoTracking()
            .Where(row => row.InboxItemId == inboxItemId)
            .OrderBy(row => row.Ordinal)
            .Select(row => row.TagId)
            .ToArrayAsync(cancellationToken);
        return new IngestCandidate(
            item.Id,
            item.State,
            item.MediaKind,
            item.ActualFilename,
            item.RequiredCapability,
            item.SourceFiles
                .OrderByDescending(row => row.IsPrimary)
                .ThenBy(row => row.Filename, StringComparer.OrdinalIgnoreCase)
                .Select(row => new IngestSourceRecord(
                    row.NormalizedPath,
                    row.Size,
                    row.LastWriteTimeUtc,
                    row.IsPresent,
                    row.IsPrimary))
                .ToArray(),
            MetadataSelection.Create(
                item.MetadataDraft.DisplayName,
                authors,
                item.MetadataDraft.SeriesId,
                tags),
            item.MetadataDraft.Version);
    }

    public async Task<IngestOperationRecord?> FindActiveAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        IngestOperationRow[] rows = await context.IngestOperations
            .AsNoTracking()
            .Where(row => row.InboxItemId == inboxItemId)
            .ToArrayAsync(cancellationToken);
        IngestOperationRow? row = rows
            .OrderByDescending(item => item.ActiveInboxItemId.HasValue)
            .ThenByDescending(item => item.CreatedAtUtc)
            .FirstOrDefault();
        return row is null
            ? null
            : await ToRecordAsync(context, row, cancellationToken);
    }

    public async Task<ConflictCheck> CheckConflictsAsync(
        string targetFilename,
        string sourceSha256,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        string targetKey = targetFilename.ToUpperInvariant();
        bool nameConflict = await context.MediaAssets
            .AsNoTracking()
            .AnyAsync(
                row => row.NormalizedRelativePath == targetKey,
                cancellationToken);
        if (nameConflict)
        {
            return new ConflictCheck(ConflictKind.Name);
        }

        bool contentConflict = await context.MediaAssets
            .AsNoTracking()
            .AnyAsync(
                row => row.Sha256 == sourceSha256,
                cancellationToken);
        return new ConflictCheck(
            contentConflict ? ConflictKind.RegisteredContent : null);
    }

    public async Task BlockConflictAsync(
        Guid inboxItemId,
        ConflictKind kind,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        InboxItemRow? item = await context.InboxItems.FindAsync(
            [inboxItemId],
            cancellationToken);
        if (item is null)
        {
            return;
        }

        item.State = InboxItemState.ConflictBlocked;
        item.RequiredCapability = null;
        item.ErrorCode = kind == ConflictKind.Name
            ? "ingest.name_conflict"
            : "ingest.content_conflict";
        item.IsRetryable = false;
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IngestOperationRecord> CreateAsync(
        IngestPreparation preparation,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        IngestOperationRow? existing = await context.IngestOperations
            .SingleOrDefaultAsync(
                row => row.ActiveInboxItemId == preparation.InboxItemId,
                cancellationToken);
        if (existing is not null)
        {
            return await ToRecordAsync(context, existing, cancellationToken);
        }

        InboxItemRow item = await context.InboxItems.SingleAsync(
            row => row.Id == preparation.InboxItemId,
            cancellationToken);
        if (item.State != InboxItemState.ReadyToTag)
        {
            throw new InvalidOperationException(
                "Only a ready inbox item can begin an ingest operation.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var row = new IngestOperationRow
        {
            Id = Guid.NewGuid(),
            InboxItemId = preparation.InboxItemId,
            InboxItem = item,
            ActiveInboxItemId = preparation.InboxItemId,
            Stage = IngestOperationStage.Created,
            SourcePath = preparation.SourcePath,
            SourceSize = preparation.SourceSize,
            SourceLastWriteTimeUtc = preparation.SourceLastWriteTimeUtc,
            SourceSha256 = preparation.SourceSha256,
            TargetFilename = preparation.TargetFilename,
            MetadataSnapshotJson = JsonSerializer.Serialize(
                MetadataSnapshot.From(preparation)),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        item.State = InboxItemState.Ingesting;
        item.RequiredCapability = null;
        item.ErrorCode = null;
        item.IsRetryable = false;
        item.UpdatedAtUtc = now;
        SourceFileRow? primary = await context.SourceFiles.SingleOrDefaultAsync(
            source => source.InboxItemId == preparation.InboxItemId && source.IsPrimary,
            cancellationToken);
        if (primary is not null)
        {
            primary.Sha256 = preparation.SourceSha256;
        }

        context.IngestOperations.Add(row);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToRecord(row, null);
        }
        catch (DbUpdateException error) when (IsUniqueConstraint(error))
        {
            await transaction.RollbackAsync(cancellationToken);
            await using MediaDesktopDbContext reload =
                await contextFactory.CreateDbContextAsync(cancellationToken);
            IngestOperationRow raced = await reload.IngestOperations
                .AsNoTracking()
                .SingleAsync(
                    operation => operation.ActiveInboxItemId == preparation.InboxItemId,
                    cancellationToken);
            return await ToRecordAsync(reload, raced, cancellationToken);
        }
    }

    public Task MarkStagedAsync(
        Guid operationId,
        StagedFile staged,
        CancellationToken cancellationToken) =>
        AdvanceAsync(
            operationId,
            IngestOperationStage.Created,
            IngestOperationStage.Staged,
            row => row.StagedPath = staged.FullPath,
            cancellationToken);

    public async Task AbandonForRetryAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        IngestOperationRow operation = await context.IngestOperations
            .Include(row => row.InboxItem)
            .SingleAsync(row => row.Id == operationId, cancellationToken);
        if (operation.Stage is not
            (IngestOperationStage.Created or IngestOperationStage.Staged))
        {
            throw new InvalidOperationException(
                "Only an uncommitted operation can be abandoned for retry.");
        }

        operation.Stage = IngestOperationStage.Failed;
        operation.ActiveInboxItemId = null;
        operation.ErrorCode = "recovery.abandoned_for_retry";
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        operation.InboxItem.State = InboxItemState.ReadyToTag;
        operation.InboxItem.RequiredCapability = null;
        operation.InboxItem.ErrorCode = null;
        operation.InboxItem.IsRetryable = false;
        operation.InboxItem.UpdatedAtUtc = operation.UpdatedAtUtc;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task MarkFileCommittedAsync(
        Guid operationId,
        CommittedFile committed,
        CancellationToken cancellationToken) =>
        AdvanceAsync(
            operationId,
            IngestOperationStage.Staged,
            IngestOperationStage.FileCommitted,
            row => row.TargetTemporaryPath = committed.FullPath,
            cancellationToken);

    public async Task<Guid> CommitAssetAsync(
        Guid operationId,
        CommittedFile committed,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        IngestOperationRow operation = await context.IngestOperations
            .Include(row => row.InboxItem)
                .ThenInclude(row => row.SourceFiles)
            .SingleAsync(row => row.Id == operationId, cancellationToken);
        if (operation.Stage is IngestOperationStage.DatabaseCommitted
            or IngestOperationStage.CleanupPending
            or IngestOperationStage.Completed)
        {
            Guid existingAssetId = await context.MediaAssets
                .Where(row => row.Sha256 == operation.SourceSha256)
                .Select(row => row.Id)
                .SingleAsync(cancellationToken);
            return existingAssetId;
        }

        if (operation.Stage != IngestOperationStage.FileCommitted)
        {
            throw new InvalidOperationException(
                "The file must be committed before the asset can be created.");
        }

        MetadataSnapshot snapshot = JsonSerializer.Deserialize<MetadataSnapshot>(
                operation.MetadataSnapshotJson)
            ?? throw new InvalidOperationException("The metadata snapshot is invalid.");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var asset = new MediaAssetRow
        {
            Id = Guid.NewGuid(),
            MediaKind = operation.InboxItem.MediaKind,
            ActualFilename = operation.TargetFilename,
            RelativePath = committed.RelativePath,
            NormalizedRelativePath = committed.RelativePath.ToUpperInvariant(),
            DisplayName = snapshot.DisplayName,
            Size = committed.Size,
            Sha256 = committed.Sha256,
            SeriesId = snapshot.SeriesId,
            MetadataVersion = snapshot.MetadataVersion,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.MediaAssets.Add(asset);
        context.MediaAssetAuthors.AddRange(snapshot.AuthorIds.Select(
            (id, index) => new MediaAssetAuthorRow
            {
                MediaAssetId = asset.Id,
                MediaAsset = asset,
                AuthorId = id,
                Ordinal = index
            }));
        context.MediaAssetTags.AddRange(snapshot.TagIds.Select(
            (id, index) => new MediaAssetTagRow
            {
                MediaAssetId = asset.Id,
                MediaAsset = asset,
                TagId = id,
                Ordinal = index
            }));
        operation.Stage = IngestOperationStage.DatabaseCommitted;
        operation.ErrorCode = null;
        operation.UpdatedAtUtc = now;
        operation.InboxItem.State = InboxItemState.Completed;
        operation.InboxItem.RequiredCapability = null;
        operation.InboxItem.ErrorCode = null;
        operation.InboxItem.IsRetryable = false;
        operation.InboxItem.UpdatedAtUtc = now;
        foreach (SourceFileRow source in operation.InboxItem.SourceFiles)
        {
            source.ActivePathKey = null;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return asset.Id;
    }

    public async Task MarkCleanupPendingAsync(
        Guid operationId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        IngestOperationRow operation = await context.IngestOperations
            .Include(row => row.InboxItem)
            .SingleAsync(row => row.Id == operationId, cancellationToken);
        operation.Stage = IngestOperationStage.CleanupPending;
        operation.ErrorCode = errorCode;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        operation.InboxItem.State = InboxItemState.CleanupPending;
        operation.InboxItem.ErrorCode = errorCode;
        operation.InboxItem.IsRetryable = true;
        operation.InboxItem.UpdatedAtUtc = operation.UpdatedAtUtc;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        IngestOperationRow operation = await context.IngestOperations
            .Include(row => row.InboxItem)
                .ThenInclude(row => row.SourceFiles)
            .SingleAsync(row => row.Id == operationId, cancellationToken);
        if (operation.Stage == IngestOperationStage.Completed)
        {
            return;
        }

        operation.Stage = IngestOperationStage.Completed;
        operation.ActiveInboxItemId = null;
        operation.ErrorCode = null;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        operation.InboxItem.State = InboxItemState.Completed;
        operation.InboxItem.ErrorCode = null;
        operation.InboxItem.IsRetryable = false;
        operation.InboxItem.UpdatedAtUtc = operation.UpdatedAtUtc;
        foreach (SourceFileRow source in operation.InboxItem.SourceFiles)
        {
            source.IsPresent = false;
            source.ActivePathKey = null;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(
        Guid operationId,
        string errorCode,
        bool isRetryable,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        IngestOperationRow? operation = await context.IngestOperations
            .Include(row => row.InboxItem)
            .SingleOrDefaultAsync(row => row.Id == operationId, cancellationToken);
        if (operation is null || operation.Stage == IngestOperationStage.Completed)
        {
            return;
        }

        operation.Stage = IngestOperationStage.Failed;
        operation.ActiveInboxItemId = null;
        operation.ErrorCode = errorCode;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        operation.InboxItem.State = InboxItemState.Failed;
        operation.InboxItem.RequiredCapability = null;
        operation.InboxItem.ErrorCode = errorCode;
        operation.InboxItem.IsRetryable = isRetryable;
        operation.InboxItem.UpdatedAtUtc = operation.UpdatedAtUtc;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IngestOperationRecord>> LoadIncompleteAsync(
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        IngestOperationRow[] rows = await context.IngestOperations
            .AsNoTracking()
            .Where(row => row.Stage != IngestOperationStage.Completed
                && row.Stage != IngestOperationStage.Failed)
            .ToArrayAsync(cancellationToken);
        var result = new List<IngestOperationRecord>(rows.Length);
        foreach (IngestOperationRow row in rows.OrderBy(item => item.CreatedAtUtc))
        {
            result.Add(await ToRecordAsync(context, row, cancellationToken));
        }

        return result;
    }

    private async Task AdvanceAsync(
        Guid operationId,
        IngestOperationStage expected,
        IngestOperationStage next,
        Action<IngestOperationRow> update,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        IngestOperationRow operation = await context.IngestOperations.SingleAsync(
            row => row.Id == operationId,
            cancellationToken);
        if (operation.Stage == next)
        {
            return;
        }

        if (operation.Stage != expected)
        {
            throw new InvalidOperationException(
                $"Cannot advance an ingest operation from {operation.Stage} to {next}.");
        }

        update(operation);
        operation.Stage = next;
        operation.ErrorCode = null;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task<IngestOperationRecord> ToRecordAsync(
        MediaDesktopDbContext context,
        IngestOperationRow row,
        CancellationToken cancellationToken)
    {
        Guid? assetId = await context.MediaAssets
            .AsNoTracking()
            .Where(asset => asset.Sha256 == row.SourceSha256)
            .Select(asset => (Guid?)asset.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return ToRecord(row, assetId);
    }

    private static IngestOperationRecord ToRecord(
        IngestOperationRow row,
        Guid? assetId) =>
        new(
            row.Id,
            row.InboxItemId,
            row.Stage,
            row.SourcePath,
            row.SourceSize,
            row.SourceLastWriteTimeUtc,
            row.SourceSha256,
            row.TargetFilename,
            row.StagedPath,
            assetId,
            row.ErrorCode);

    private static bool IsUniqueConstraint(DbUpdateException error) =>
        error.InnerException is SqliteException sqlite
        && sqlite.SqliteErrorCode == 19
        && sqlite.SqliteExtendedErrorCode == 2067;

    private sealed record MetadataSnapshot(
        string? DisplayName,
        Guid[] AuthorIds,
        Guid? SeriesId,
        Guid[] TagIds,
        long MetadataVersion)
    {
        public static MetadataSnapshot From(IngestPreparation preparation) =>
            new(
                preparation.DisplayName,
                preparation.AuthorIds.ToArray(),
                preparation.SeriesId,
                preparation.TagIds.ToArray(),
                preparation.MetadataVersion);
    }
}
