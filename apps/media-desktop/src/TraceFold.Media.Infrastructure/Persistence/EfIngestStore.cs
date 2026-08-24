using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
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
            : ToRecord(row);
    }

    public async Task<IngestOperationRecord> CreateAsync(
        IngestPreparation preparation,
        IngestExecutionPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(plan);
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
            return ToRecord(existing);
        }

        InboxItemRow item = await context.InboxItems.SingleAsync(
            row => row.Id == preparation.InboxItemId,
            cancellationToken);
        ConflictCaseRow? conflict = null;
        if (plan.Disposition == IngestDisposition.Create)
        {
            if (item.State != InboxItemState.ReadyToTag
                || plan.ConflictCaseId is not null
                || !StringComparer.OrdinalIgnoreCase.Equals(
                    plan.RequestedTargetFilename,
                    preparation.TargetFilename))
            {
                throw new InvalidOperationException(
                    "Only a ready inbox item can begin an ordinary ingest operation.");
            }
        }
        else
        {
            if (item.State != InboxItemState.ConflictBlocked
                || plan.ConflictCaseId is not Guid conflictCaseId)
            {
                throw new InvalidOperationException(
                    "A resolved ingest requires one blocked conflict case.");
            }

            conflict = await context.ConflictCases
                .Include(row => row.Matches)
                .SingleOrDefaultAsync(
                    row => row.Id == conflictCaseId
                        && row.ActiveInboxItemId == preparation.InboxItemId
                        && row.OperationId == null,
                    cancellationToken);
            ValidateChosenPlan(conflict, plan);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string? oldAssetSnapshotJson = null;
        if (plan.Disposition == IngestDisposition.ReplaceRegistered)
        {
            MediaAssetRow asset = await context.MediaAssets
                .AsNoTracking()
                .Include(row => row.Authors)
                .Include(row => row.Tags)
                .SingleAsync(
                    row => row.Id == plan.ReplacementAssetId,
                    cancellationToken);
            if (asset.MetadataVersion != plan.ReplacementMetadataVersion
                || plan.Target is null
                || !StringComparer.OrdinalIgnoreCase.Equals(
                    asset.RelativePath,
                    plan.Target.RelativePath))
            {
                throw new InvalidOperationException(
                    "The registered replacement asset changed before ingest creation.");
            }

            oldAssetSnapshotJson = JsonSerializer.Serialize(
                OldAssetSnapshot.From(asset));
        }

        var row = new IngestOperationRow
        {
            Id = Guid.NewGuid(),
            InboxItemId = preparation.InboxItemId,
            InboxItem = item,
            ActiveInboxItemId = preparation.InboxItemId,
            Stage = IngestOperationStage.Created,
            Disposition = plan.Disposition,
            ConflictCaseId = plan.ConflictCaseId,
            SourcePath = preparation.Input.FullPath,
            SourceSize = preparation.Input.Size,
            SourceLastWriteTimeUtc = preparation.Input.LastWriteTimeUtc,
            SourceSha256 = preparation.Input.Sha256,
            TargetFilename = plan.RequestedTargetFilename,
            ReplacementAssetId = plan.ReplacementAssetId,
            ReplacementAssetMetadataVersion = plan.ReplacementMetadataVersion,
            ExpectedTargetRelativePath = plan.Target?.RelativePath,
            ExpectedTargetSize = plan.Target?.Size,
            ExpectedTargetLastWriteTimeUtc = plan.Target?.LastWriteTimeUtc,
            ExpectedTargetSha256 = plan.Target?.Sha256,
            BackupState = plan.Target is null
                ? IngestBackupState.NotRequired
                : IngestBackupState.Pending,
            OldAssetSnapshotJson = oldAssetSnapshotJson,
            CleanupAttemptCount = 0,
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
        SourceFileRow[] sources = await context.SourceFiles
            .Where(source => source.InboxItemId == preparation.InboxItemId)
            .ToArrayAsync(cancellationToken);
        foreach (IngestCleanupFile cleanupFile in preparation.Input.CleanupFiles
                     .Where(file => file.Kind == IngestCleanupKind.InboxSource))
        {
            SourceFileRow? source = sources.SingleOrDefault(row =>
                StringComparer.OrdinalIgnoreCase.Equals(
                    Path.GetFullPath(row.NormalizedPath),
                    Path.GetFullPath(cleanupFile.FullPath)));
            if (source is not null)
            {
                source.Sha256 = cleanupFile.Sha256;
            }
        }

        context.IngestOperations.Add(row);
        context.IngestCleanupEntries.AddRange(
            preparation.Input.CleanupFiles.Select(
                (file, ordinal) => new IngestCleanupEntryRow
                {
                    Id = Guid.NewGuid(),
                    OperationId = row.Id,
                    Operation = row,
                    Ordinal = ordinal,
                    CleanupKind = file.Kind.ToString().ToLowerInvariant(),
                    ControlledPath = file.FullPath,
                    Size = file.Size,
                    Sha256 = file.Sha256.ToUpperInvariant()
                }));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToRecord(row);
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
            return ToRecord(raced);
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

    public async Task MarkBackupCreatedAsync(
        Guid operationId,
        ReplacementBackup backup,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backup);
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        IngestOperationRow operation = await context.IngestOperations
            .Include(row => row.CleanupEntries)
            .SingleAsync(row => row.Id == operationId, cancellationToken);
        if (operation.BackupState == IngestBackupState.Created)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(
                    operation.BackupRelativePath,
                    backup.RelativePath)
                || operation.BackupSize != backup.Size
                || !StringComparer.OrdinalIgnoreCase.Equals(
                    operation.BackupSha256,
                    backup.Sha256))
            {
                throw new InvalidOperationException(
                    "The persisted replacement backup has a different identity.");
            }

            return;
        }

        if (operation.Stage != IngestOperationStage.Staged
            || operation.BackupState != IngestBackupState.Pending)
        {
            throw new InvalidOperationException(
                "Only a staged replacement can persist its backup.");
        }

        operation.BackupState = IngestBackupState.Created;
        operation.BackupRelativePath = backup.RelativePath;
        operation.BackupSize = backup.Size;
        operation.BackupSha256 = backup.Sha256.ToUpperInvariant();
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        int nextOrdinal = operation.CleanupEntries.Count == 0
            ? 0
            : checked(operation.CleanupEntries.Max(entry => entry.Ordinal) + 1);
        var cleanupEntry = new IngestCleanupEntryRow
        {
            Id = Guid.NewGuid(),
            OperationId = operation.Id,
            Operation = operation,
            Ordinal = nextOrdinal,
            CleanupKind = IngestCleanupKind.ReplacementBackup
                .ToString()
                .ToLowerInvariant(),
            ControlledPath = backup.FullPath,
            Size = backup.Size,
            Sha256 = backup.Sha256.ToUpperInvariant()
        };
        context.IngestCleanupEntries.Add(cleanupEntry);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MarkBackupRestoredAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        IngestOperationRow operation = await context.IngestOperations
            .SingleAsync(row => row.Id == operationId, cancellationToken);
        if (operation.BackupState == IngestBackupState.Restored)
        {
            return;
        }

        if (operation.BackupState != IngestBackupState.Created)
        {
            throw new InvalidOperationException(
                "Only a verified replacement backup can be marked restored.");
        }

        operation.BackupState = IngestBackupState.Restored;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkRecoveryBlockedAsync(
        Guid operationId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        IngestOperationRow operation = await context.IngestOperations
            .Include(row => row.InboxItem)
            .SingleAsync(row => row.Id == operationId, cancellationToken);
        if (operation.Stage == IngestOperationStage.RecoveryBlocked)
        {
            return;
        }

        if (operation.Stage is IngestOperationStage.Completed
            or IngestOperationStage.Failed)
        {
            throw new InvalidOperationException(
                "A terminal ingest operation cannot become recovery blocked.");
        }

        operation.RecoveryBlockedFromStage = operation.Stage;
        operation.Stage = IngestOperationStage.RecoveryBlocked;
        operation.ErrorCode = errorCode;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        operation.InboxItem.State = InboxItemState.RecoveryBlocked;
        operation.InboxItem.RequiredCapability = null;
        operation.InboxItem.ErrorCode = errorCode;
        operation.InboxItem.IsRetryable = false;
        operation.InboxItem.UpdatedAtUtc = operation.UpdatedAtUtc;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ReplacementDatabaseMatchesExpectedAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        IngestOperationRow operation = await context.IngestOperations
            .AsNoTracking()
            .SingleAsync(row => row.Id == operationId, cancellationToken);
        if (operation.Stage != IngestOperationStage.FileCommitted
            || operation.AssetId is not null
            || operation.BackupState != IngestBackupState.Created
            || string.IsNullOrWhiteSpace(operation.ExpectedTargetRelativePath))
        {
            return false;
        }

        if (operation.Disposition == IngestDisposition.ReplaceRegistered)
        {
            MediaAssetRow? asset = await context.MediaAssets
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    row => row.Id == operation.ReplacementAssetId,
                    cancellationToken);
            return asset is not null
                && asset.MetadataVersion
                    == operation.ReplacementAssetMetadataVersion
                && StringComparer.OrdinalIgnoreCase.Equals(
                    asset.RelativePath,
                    operation.ExpectedTargetRelativePath);
        }

        if (operation.Disposition != IngestDisposition.ReplaceUnregistered)
        {
            return false;
        }

        string normalized = operation.ExpectedTargetRelativePath.ToUpperInvariant();
        return !await context.MediaAssets
            .AsNoTracking()
            .AnyAsync(
                asset => asset.NormalizedRelativePath == normalized,
                cancellationToken);
    }

    public async Task<bool> OperationDatabaseHasNewVersionAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        IngestOperationRow operation = await context.IngestOperations
            .AsNoTracking()
            .SingleAsync(row => row.Id == operationId, cancellationToken);
        if (operation.AssetId is not Guid assetId)
        {
            return false;
        }

        MediaAssetRow? asset = await context.MediaAssets
            .AsNoTracking()
            .Include(row => row.Authors)
            .Include(row => row.Tags)
            .SingleOrDefaultAsync(row => row.Id == assetId, cancellationToken);
        if (asset is null)
        {
            return false;
        }

        MetadataSnapshot? snapshot = JsonSerializer.Deserialize<MetadataSnapshot>(
            operation.MetadataSnapshotJson);
        if (snapshot is null)
        {
            return false;
        }

        long expectedMetadataVersion = operation.Disposition
            == IngestDisposition.ReplaceRegistered
                ? checked((operation.ReplacementAssetMetadataVersion ?? -2) + 1)
                : snapshot.MetadataVersion;
        return (operation.Disposition != IngestDisposition.ReplaceRegistered
                || operation.ReplacementAssetId == asset.Id)
            && StringComparer.OrdinalIgnoreCase.Equals(
                asset.RelativePath,
                operation.TargetFilename)
            && StringComparer.OrdinalIgnoreCase.Equals(
                asset.ActualFilename,
                operation.TargetFilename)
            && asset.Size == operation.SourceSize
            && StringComparer.OrdinalIgnoreCase.Equals(
                asset.Sha256,
                operation.SourceSha256)
            && asset.DisplayName == snapshot.DisplayName
            && asset.SeriesId == snapshot.SeriesId
            && asset.MetadataVersion == expectedMetadataVersion
            && asset.Authors
                .OrderBy(link => link.Ordinal)
                .Select(link => link.AuthorId)
                .SequenceEqual(snapshot.AuthorIds)
            && asset.Tags
                .OrderBy(link => link.Ordinal)
                .Select(link => link.TagId)
                .SequenceEqual(snapshot.TagIds);
    }

    public async Task ResumeRecoveryAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        IngestOperationRow operation = await context.IngestOperations
            .Include(row => row.InboxItem)
            .SingleAsync(row => row.Id == operationId, cancellationToken);
        if (operation.Stage != IngestOperationStage.RecoveryBlocked
            || operation.RecoveryBlockedFromStage is not IngestOperationStage resumed)
        {
            throw new InvalidOperationException(
                "Only a recovery-blocked operation can resume.");
        }

        operation.Stage = resumed;
        operation.RecoveryBlockedFromStage = null;
        operation.ErrorCode = null;
        operation.UpdatedAtUtc = DateTimeOffset.UtcNow;
        operation.InboxItem.State = resumed switch
        {
            IngestOperationStage.CleanupPending => InboxItemState.CleanupPending,
            IngestOperationStage.DatabaseCommitted => InboxItemState.Completed,
            _ => InboxItemState.Ingesting
        };
        operation.InboxItem.ErrorCode = null;
        operation.InboxItem.IsRetryable = false;
        operation.InboxItem.UpdatedAtUtc = operation.UpdatedAtUtc;
        await context.SaveChangesAsync(cancellationToken);
    }

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
            row =>
            {
                row.TargetTemporaryPath = committed.FullPath;
                row.TargetFilename = committed.RelativePath;
            },
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
            return operation.AssetId
                ?? throw new InvalidOperationException(
                    "Committed operation has no asset id.");
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
        MediaAssetRow asset;
        if (operation.Disposition == IngestDisposition.ReplaceRegistered)
        {
            asset = await context.MediaAssets
                .Include(row => row.Authors)
                .Include(row => row.Tags)
                .SingleAsync(
                    row => row.Id == operation.ReplacementAssetId,
                    cancellationToken);
            if (asset.MetadataVersion
                    != operation.ReplacementAssetMetadataVersion
                || !StringComparer.OrdinalIgnoreCase.Equals(
                    asset.RelativePath,
                    operation.ExpectedTargetRelativePath)
                || !StringComparer.OrdinalIgnoreCase.Equals(
                    asset.RelativePath,
                    committed.RelativePath))
            {
                throw new InvalidOperationException(
                    "The registered replacement asset changed before commit.");
            }

            context.MediaAssetAuthors.RemoveRange(asset.Authors);
            context.MediaAssetTags.RemoveRange(asset.Tags);
            asset.DisplayName = snapshot.DisplayName;
            asset.Size = committed.Size;
            asset.Sha256 = committed.Sha256.ToUpperInvariant();
            asset.SeriesId = snapshot.SeriesId;
            asset.MetadataVersion = checked(asset.MetadataVersion + 1);
            asset.UpdatedAtUtc = now;
        }
        else
        {
            asset = new MediaAssetRow
            {
                Id = Guid.NewGuid(),
                MediaKind = operation.InboxItem.MediaKind,
                ActualFilename = committed.RelativePath,
                RelativePath = committed.RelativePath,
                NormalizedRelativePath = committed.RelativePath.ToUpperInvariant(),
                DisplayName = snapshot.DisplayName,
                Size = committed.Size,
                Sha256 = committed.Sha256.ToUpperInvariant(),
                SeriesId = snapshot.SeriesId,
                MetadataVersion = snapshot.MetadataVersion,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            context.MediaAssets.Add(asset);
        }

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
        operation.AssetId = asset.Id;
        operation.ErrorCode = null;
        operation.UpdatedAtUtc = now;
        operation.InboxItem.State = InboxItemState.Completed;
        operation.InboxItem.RequiredCapability = null;
        operation.InboxItem.ErrorCode = null;
        operation.InboxItem.IsRetryable = false;
        operation.InboxItem.UpdatedAtUtc = now;
        if (operation.ConflictCaseId is Guid conflictCaseId)
        {
            ConflictCaseRow conflict = await context.ConflictCases
                .Include(row => row.Matches)
                .SingleAsync(
                    row => row.Id == conflictCaseId
                        && row.ActiveInboxItemId == operation.InboxItemId
                        && row.OperationId == null,
                    cancellationToken);
            ConflictCase aggregate = RehydrateChosenConflict(conflict);
            aggregate.Resolve();
            conflict.Status = aggregate.Status;
            conflict.Resolution = aggregate.Resolution;
            conflict.SelectedMatchId = aggregate.SelectedMatchId;
            conflict.OperationId = operation.Id;
            conflict.ActiveInboxItemId = null;
            conflict.ResolvedAtUtc = now;
            conflict.UpdatedAtUtc = now;
        }

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

        ProcessedOutputRow[] processedOutputs = await context.ProcessedOutputs
            .Where(output => output.ActiveInboxItemId == operation.InboxItemId)
            .ToArrayAsync(cancellationToken);
        foreach (ProcessedOutputRow output in processedOutputs)
        {
            output.IsValid = false;
            output.ActiveInboxItemId = null;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkCleanupEntryCompletedAsync(
        Guid operationId,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        IngestCleanupEntryRow? entry = await context.IngestCleanupEntries
            .SingleOrDefaultAsync(
                row => row.Id == entryId && row.OperationId == operationId,
                cancellationToken);
        if (entry is null || entry.CompletedAtUtc.HasValue)
        {
            return;
        }

        entry.CompletedAtUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IngestCleanupEntryRecord>> LoadCleanupEntriesAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        IngestCleanupEntryRow[] entries = await context.IngestCleanupEntries
            .AsNoTracking()
            .Where(row => row.OperationId == operationId)
            .OrderBy(row => row.Ordinal)
            .ToArrayAsync(cancellationToken);
        return entries.Select(row => new IngestCleanupEntryRecord(
            row.Id,
            row.OperationId,
            new IngestCleanupFile(
                Enum.Parse<IngestCleanupKind>(row.CleanupKind, ignoreCase: true),
                row.ControlledPath,
                row.Size,
                row.Sha256),
            row.CompletedAtUtc.HasValue)).ToArray();
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
        return rows
            .OrderBy(item => item.CreatedAtUtc)
            .Select(ToRecord)
            .ToArray();
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

    private static IngestOperationRecord ToRecord(IngestOperationRow row) =>
        new(
            row.Id,
            row.InboxItemId,
            row.Stage,
            row.Disposition,
            row.ConflictCaseId,
            row.SourcePath,
            row.SourceSize,
            row.SourceLastWriteTimeUtc,
            row.SourceSha256,
            row.TargetFilename,
            row.StagedPath,
            row.AssetId,
            row.ReplacementAssetId,
            row.ReplacementAssetMetadataVersion,
            row.ExpectedTargetRelativePath,
            row.ExpectedTargetSize,
            row.ExpectedTargetLastWriteTimeUtc,
            row.ExpectedTargetSha256,
            row.BackupState,
            row.BackupRelativePath,
            row.BackupSize,
            row.BackupSha256,
            row.RecoveryBlockedFromStage,
            row.ErrorCode,
            row.CleanupAttemptCount,
            row.NextCleanupAttemptAtUtc);

    private static bool IsUniqueConstraint(DbUpdateException error) =>
        error.InnerException is SqliteException sqlite
        && sqlite.SqliteErrorCode == 19
        && sqlite.SqliteExtendedErrorCode == 2067;

    private static void ValidateChosenPlan(
        ConflictCaseRow? conflict,
        IngestExecutionPlan plan)
    {
        if (conflict is null || conflict.Status != ConflictCaseStatus.Chosen)
        {
            throw new InvalidOperationException(
                "The conflict case has no persisted resolution.");
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(
                plan.RequestedTargetFilename,
                plan.Disposition == IngestDisposition.KeepBoth
                    ? conflict.TargetFilename
                    : plan.Target?.RelativePath))
        {
            throw new InvalidOperationException(
                "The execution target filename differs from the persisted choice.");
        }

        if (plan.Disposition == IngestDisposition.KeepBoth)
        {
            if (conflict.Resolution != ConflictResolution.KeepBoth
                || conflict.SelectedMatchId is not null
                || plan.Target is not null)
            {
                throw new InvalidOperationException(
                    "The Keep Both plan does not match the persisted choice.");
            }

            return;
        }

        if (conflict.Resolution != ConflictResolution.Replace
            || conflict.SelectedMatchId is not Guid selectedMatchId
            || plan.Target is null)
        {
            throw new InvalidOperationException(
                "The replacement plan does not match the persisted choice.");
        }

        ConflictMatchRow selected = conflict.Matches.SingleOrDefault(
                match => match.Id == selectedMatchId)
            ?? throw new InvalidOperationException(
                "The persisted replacement target is missing.");
        bool registeredPlan =
            plan.Disposition == IngestDisposition.ReplaceRegistered;
        if (plan.Disposition is not
                (IngestDisposition.ReplaceRegistered
                    or IngestDisposition.ReplaceUnregistered)
            || selected.IsRegistered != registeredPlan
            || selected.AssetId != plan.ReplacementAssetId
            || selected.AssetMetadataVersion
                != plan.ReplacementMetadataVersion
            || !StringComparer.OrdinalIgnoreCase.Equals(
                selected.RelativePath,
                plan.Target.RelativePath)
            || selected.Size != plan.Target.Size
            || selected.LastWriteTimeUtc != plan.Target.LastWriteTimeUtc
            || !StringComparer.OrdinalIgnoreCase.Equals(
                selected.Sha256,
                plan.Target.Sha256))
        {
            throw new InvalidOperationException(
                "The replacement plan target differs from the persisted match.");
        }
    }

    private static ConflictCase RehydrateChosenConflict(ConflictCaseRow row)
    {
        ConflictCase aggregate = ConflictCase.Open(
            row.Id,
            row.InboxItemId,
            row.InputSha256,
            row.TargetFilename,
            row.Matches.Select(match => match.Id).ToArray());
        aggregate.Choose(
            row.Resolution
                ?? throw new InvalidOperationException(
                    "A chosen conflict case has no resolution."),
            row.SelectedMatchId);
        return aggregate;
    }

    private sealed record OldAssetSnapshot(
        Guid Id,
        string ActualFilename,
        string RelativePath,
        MediaKind MediaKind,
        string? DisplayName,
        long Size,
        string Sha256,
        Guid? SeriesId,
        Guid[] AuthorIds,
        Guid[] TagIds,
        long MetadataVersion,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc)
    {
        public static OldAssetSnapshot From(MediaAssetRow asset) =>
            new(
                asset.Id,
                asset.ActualFilename,
                asset.RelativePath,
                asset.MediaKind,
                asset.DisplayName,
                asset.Size,
                asset.Sha256,
                asset.SeriesId,
                asset.Authors
                    .OrderBy(link => link.Ordinal)
                    .Select(link => link.AuthorId)
                    .ToArray(),
                asset.Tags
                    .OrderBy(link => link.Ordinal)
                    .Select(link => link.TagId)
                    .ToArray(),
                asset.MetadataVersion,
                asset.CreatedAtUtc,
                asset.UpdatedAtUtc);
    }

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
