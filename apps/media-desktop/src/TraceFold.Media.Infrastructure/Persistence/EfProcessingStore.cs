using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Processing;
using TraceFold.Media.Infrastructure.Filesystem;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfProcessingStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory) : IProcessingStore
{
    private readonly SemaphoreSlim claimSemaphore = new(1, 1);

    public async Task<ProcessingCandidate?> LoadCandidateAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        InboxItemRow? item = await context.InboxItems
            .AsNoTracking()
            .Include(row => row.SourceFiles)
            .SingleOrDefaultAsync(row => row.Id == inboxItemId, cancellationToken);
        if (item is null)
        {
            return null;
        }

        int previousAttempts = await context.ProcessingJobs
            .AsNoTracking()
            .CountAsync(row => row.InboxItemId == inboxItemId, cancellationToken);
        return new ProcessingCandidate(
            item.Id,
            item.State,
            item.MediaKind,
            item.ActualFilename,
            item.RequiredCapability ?? string.Empty,
            item.SourceFiles
                .Where(row => row.IsPresent && row.ActivePathKey != null)
                .OrderByDescending(row => row.IsPrimary)
                .ThenBy(row => row.Filename, StringComparer.OrdinalIgnoreCase)
                .Select(row => new ProcessingCandidateSource(
                    row.Id,
                    row.NormalizedPath,
                    row.Filename,
                    row.Extension,
                    row.Size,
                    row.LastWriteTimeUtc,
                    row.Sha256,
                    row.IsPrimary))
                .ToArray(),
            previousAttempts);
    }

    public async Task<ProcessingJobRecord?> FindActiveAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        ProcessingJobRow? row = await WithGraph(context.ProcessingJobs.AsNoTracking())
            .SingleOrDefaultAsync(
                item => item.ActiveInboxItemId == inboxItemId,
                cancellationToken);
        return row is null ? null : ToRecord(row, null);
    }

    public async Task<QueueProcessingResult> QueueAsync(
        PlannedProcessing planned,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(planned);
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        ProcessingJobRow? existing = await WithGraph(context.ProcessingJobs)
            .SingleOrDefaultAsync(
                row => row.ActiveInboxItemId == planned.Plan.InboxItemId,
                cancellationToken);
        if (existing is not null)
        {
            return new QueueProcessingResult(ToRecord(existing, null), Created: false);
        }

        InboxItemRow item = await context.InboxItems.SingleAsync(
            row => row.Id == planned.Plan.InboxItemId,
            cancellationToken);
        if (item.State is not (InboxItemState.RequiresProcessing
            or InboxItemState.ProcessingInterrupted
            or InboxItemState.Failed)
            || (item.State == InboxItemState.Failed && item.RequiredCapability is null))
        {
            throw new InvalidOperationException("The inbox item cannot be queued.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        ProcessingPlanRow planRow = CreatePlanRow(planned.Plan, now);
        var job = new ProcessingJobRow
        {
            Id = planned.JobId,
            PlanId = planRow.Id,
            Plan = planRow,
            InboxItemId = item.Id,
            InboxItem = item,
            ActiveInboxItemId = item.Id,
            Attempt = planned.Attempt,
            Status = ProcessingJobStatus.Queued,
            ProgressPercent = 0,
            CorrelationId = Guid.NewGuid().ToString("N"),
            QueuedAtUtc = now,
            UpdatedAtUtc = now
        };
        planRow.Job = job;
        context.ProcessingPlans.Add(planRow);
        context.ProcessingJobs.Add(job);
        item.State = InboxItemState.ProcessingQueued;
        item.RequiredCapability = planned.Plan.Capability;
        item.ErrorCode = null;
        item.IsRetryable = false;
        item.UpdatedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new QueueProcessingResult(ToRecord(job, null), Created: true);
    }

    public async Task<ProcessingJobRecord?> TryClaimNextAsync(
        CancellationToken cancellationToken)
    {
        await claimSemaphore.WaitAsync(cancellationToken);
        try
        {
            await using MediaDesktopDbContext context =
                await contextFactory.CreateDbContextAsync(cancellationToken);
            await using IDbContextTransaction transaction =
                await context.Database.BeginTransactionAsync(cancellationToken);
            ProcessingJobRow[] queued = await WithGraph(context.ProcessingJobs)
                .Where(row => row.Status == ProcessingJobStatus.Queued)
                .ToArrayAsync(cancellationToken);
            ProcessingJobRow? job = queued
                .OrderBy(row => row.QueuedAtUtc)
                .ThenBy(row => row.Id)
                .FirstOrDefault();
            if (job is null)
            {
                return null;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            job.Status = ProcessingJobStatus.Running;
            job.StartedAtUtc = now;
            job.UpdatedAtUtc = now;
            job.InboxItem.State = InboxItemState.Processing;
            job.InboxItem.ErrorCode = null;
            job.InboxItem.IsRetryable = false;
            job.InboxItem.UpdatedAtUtc = now;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToRecord(job, null);
        }
        finally
        {
            claimSemaphore.Release();
        }
    }

    public async Task UpdateProgressAsync(
        Guid jobId,
        ProcessingProgressUpdate update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentException.ThrowIfNullOrWhiteSpace(update.StepKey);
        ArgumentOutOfRangeException.ThrowIfLessThan(update.Percent, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(update.Percent, 100);
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        ProcessingJobRow job = await context.ProcessingJobs.SingleAsync(
            row => row.Id == jobId,
            cancellationToken);
        if (job.Status != ProcessingJobStatus.Running)
        {
            throw new InvalidOperationException(
                "Only a running processing job can report progress.");
        }

        if (update.Percent >= job.ProgressPercent)
        {
            job.CurrentStepKey = update.StepKey;
            job.ProgressPercent = update.Percent;
        }

        job.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(
        Guid jobId,
        ProcessedOutputDraft output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ValidateOutputDraft(output);
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        ProcessingJobRow job = await WithGraph(context.ProcessingJobs)
            .SingleAsync(row => row.Id == jobId, cancellationToken);
        if (job.Status == ProcessingJobStatus.Succeeded && job.Output is not null)
        {
            return;
        }

        if (job.Status != ProcessingJobStatus.Running)
        {
            throw new InvalidOperationException(
                "Only a running processing job can complete.");
        }

        await RequireMatchingControlledOutputAsync(context, output, cancellationToken);
        ProcessedOutputRow[] olderOutputs = await context.ProcessedOutputs
            .Where(row => row.ActiveInboxItemId == job.InboxItemId)
            .ToArrayAsync(cancellationToken);
        foreach (ProcessedOutputRow older in olderOutputs)
        {
            older.ActiveInboxItemId = null;
            older.IsValid = false;
        }

        var outputRow = new ProcessedOutputRow
        {
            JobId = job.Id,
            Job = job,
            InboxItemId = job.InboxItemId,
            ActiveInboxItemId = job.InboxItemId,
            RelativePath = output.RelativePath,
            Size = output.Size,
            LastWriteTimeUtc = output.LastWriteTimeUtc,
            Sha256 = output.Sha256,
            DurationTicks = output.Duration.Ticks,
            HasVideo = output.HasVideo,
            HasAudio = output.HasAudio,
            IsValid = true
        };
        DateTimeOffset now = DateTimeOffset.UtcNow;
        context.ProcessedOutputs.Add(outputRow);
        job.Output = outputRow;
        job.Status = ProcessingJobStatus.Succeeded;
        job.ActiveInboxItemId = null;
        job.ProgressPercent = 100;
        job.ErrorCode = null;
        job.IsRetryable = false;
        job.FinishedAtUtc = now;
        job.UpdatedAtUtc = now;
        job.InboxItem.State = InboxItemState.ReadyToTag;
        job.InboxItem.RequiredCapability = null;
        job.InboxItem.ErrorCode = null;
        job.InboxItem.IsRetryable = false;
        job.InboxItem.UpdatedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> CancelQueuedAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        ProcessingJobRow? job = await context.ProcessingJobs
            .Include(row => row.InboxItem)
            .SingleOrDefaultAsync(row => row.Id == jobId, cancellationToken);
        if (job is null || job.Status != ProcessingJobStatus.Queued)
        {
            return false;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        job.Status = ProcessingJobStatus.Cancelled;
        job.ActiveInboxItemId = null;
        job.ErrorCode = null;
        job.IsRetryable = false;
        job.FinishedAtUtc = now;
        job.UpdatedAtUtc = now;
        job.InboxItem.State = InboxItemState.RequiresProcessing;
        job.InboxItem.RequiredCapability = job.InboxItem.RequiredCapability
            ?? (await context.ProcessingPlans
                .Where(row => row.Id == job.PlanId)
                .Select(row => row.Capability)
                .SingleAsync(cancellationToken));
        job.InboxItem.ErrorCode = null;
        job.InboxItem.IsRetryable = false;
        job.InboxItem.UpdatedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task InterruptAsync(
        Guid jobId,
        string errorCode,
        CancellationToken cancellationToken) =>
        TerminalizeAsync(
            jobId,
            ProcessingJobStatus.Interrupted,
            InboxItemState.ProcessingInterrupted,
            errorCode,
            isRetryable: true,
            cancellationToken);

    public Task FailAsync(
        Guid jobId,
        string errorCode,
        bool isRetryable,
        CancellationToken cancellationToken) =>
        TerminalizeAsync(
            jobId,
            ProcessingJobStatus.Failed,
            InboxItemState.Failed,
            errorCode,
            isRetryable,
            cancellationToken);

    public async Task<IReadOnlyList<ProcessingJobRecord>> InterruptRecoverableAsync(
        string errorCode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        ProcessingJobRow[] recoverable = await WithGraph(context.ProcessingJobs)
            .Where(row => row.Status == ProcessingJobStatus.Queued
                || row.Status == ProcessingJobStatus.Running)
            .ToArrayAsync(cancellationToken);
        ProcessingJobRow[] jobs = recoverable
            .OrderBy(row => row.QueuedAtUtc)
            .ThenBy(row => row.Id)
            .ToArray();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (ProcessingJobRow job in jobs)
        {
            job.Status = ProcessingJobStatus.Interrupted;
            job.ActiveInboxItemId = null;
            job.ErrorCode = errorCode;
            job.IsRetryable = true;
            job.FinishedAtUtc = now;
            job.UpdatedAtUtc = now;
            job.InboxItem.State = InboxItemState.ProcessingInterrupted;
            job.InboxItem.RequiredCapability = job.Plan.Capability;
            job.InboxItem.ErrorCode = errorCode;
            job.InboxItem.IsRetryable = true;
            job.InboxItem.UpdatedAtUtc = now;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return jobs.Select(row => ToRecord(row, null)).ToArray();
    }

    public async Task<ValidatedProcessingOutput?> LoadValidatedOutputAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        ProcessedOutputRow? output = await context.ProcessedOutputs
            .Include(row => row.Job)
                .ThenInclude(row => row.Plan)
                    .ThenInclude(row => row.Inputs)
            .SingleOrDefaultAsync(
                row => row.ActiveInboxItemId == inboxItemId && row.IsValid,
                cancellationToken);
        if (output is null)
        {
            return null;
        }

        AppSettingsRow? settings = await context.AppSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == 1, cancellationToken);
        string? fullPath = null;
        if (settings is not null)
        {
            try
            {
                fullPath = ControlledPathGuard.RequireDescendant(
                    settings.WorkDirectory,
                    Path.Combine(settings.WorkDirectory, output.RelativePath));
            }
            catch (ControlledPathException)
            {
                fullPath = null;
            }
        }

        if (fullPath is null
            || !await MatchesOutputAsync(fullPath, output, cancellationToken))
        {
            output.IsValid = false;
            output.ActiveInboxItemId = null;
            await context.SaveChangesAsync(cancellationToken);
            return null;
        }

        return new ValidatedProcessingOutput(
            ToOutputRecord(output, fullPath),
            ToInputs(output.Job.Plan.Inputs));
    }

    public async Task<Guid?> InvalidateForSourceChangeAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        InboxItemRow? item = await context.InboxItems.SingleOrDefaultAsync(
            row => row.Id == inboxItemId,
            cancellationToken);
        if (item is null)
        {
            return null;
        }

        ProcessedOutputRow? output = await context.ProcessedOutputs
            .SingleOrDefaultAsync(
                row => row.ActiveInboxItemId == inboxItemId,
                cancellationToken);
        if (output is not null)
        {
            output.IsValid = false;
            output.ActiveInboxItemId = null;
        }

        ProcessingJobRow? activeJob = await context.ProcessingJobs
            .SingleOrDefaultAsync(
                row => row.ActiveInboxItemId == inboxItemId,
                cancellationToken);
        Guid? runningJobId = activeJob?.Status == ProcessingJobStatus.Running
            ? activeJob.Id
            : null;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (activeJob is not null)
        {
            activeJob.Status = ProcessingJobStatus.Interrupted;
            activeJob.ActiveInboxItemId = null;
            activeJob.ErrorCode = "processing.source_changed";
            activeJob.IsRetryable = true;
            activeJob.FinishedAtUtc = now;
            activeJob.UpdatedAtUtc = now;
        }

        item.State = InboxItemState.WaitingForStability;
        item.RequiredCapability = null;
        item.ErrorCode = null;
        item.IsRetryable = false;
        item.UpdatedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return runningJobId;
    }

    private async Task TerminalizeAsync(
        Guid jobId,
        ProcessingJobStatus jobStatus,
        InboxItemState itemState,
        string errorCode,
        bool isRetryable,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        ProcessingJobRow? job = await context.ProcessingJobs
            .Include(row => row.InboxItem)
            .Include(row => row.Plan)
            .SingleOrDefaultAsync(row => row.Id == jobId, cancellationToken);
        if (job is null || job.Status is not
            (ProcessingJobStatus.Queued or ProcessingJobStatus.Running))
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        job.Status = jobStatus;
        job.ActiveInboxItemId = null;
        job.ErrorCode = errorCode;
        job.IsRetryable = isRetryable;
        job.FinishedAtUtc = now;
        job.UpdatedAtUtc = now;
        job.InboxItem.State = itemState;
        job.InboxItem.RequiredCapability = job.Plan.Capability;
        job.InboxItem.ErrorCode = errorCode;
        job.InboxItem.IsRetryable = isRetryable;
        job.InboxItem.UpdatedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static ProcessingPlanRow CreatePlanRow(
        ProcessingPlanDefinition plan,
        DateTimeOffset createdAtUtc)
    {
        var row = new ProcessingPlanRow
        {
            Id = plan.Id,
            InboxItemId = plan.InboxItemId,
            ProcessorKey = plan.ProcessorKey,
            ProcessorVersion = plan.ProcessorVersion,
            ProcessorDisplayName = plan.ProcessorDisplayName,
            Capability = plan.Capability,
            OutputFilename = plan.OutputFilename,
            WorkDirectoryRelativePath = plan.WorkDirectoryRelativePath,
            StepKeysJson = JsonSerializer.Serialize(plan.StepKeys),
            CreatedAtUtc = createdAtUtc
        };
        foreach (ProcessingPlanInput input in plan.Inputs)
        {
            row.Inputs.Add(new ProcessingPlanInputRow
            {
                Id = Guid.NewGuid(),
                PlanId = row.Id,
                Plan = row,
                SourceFileId = input.SourceFileId,
                Role = input.Role,
                FullPath = input.FullPath,
                Filename = input.Filename,
                Size = input.Size,
                LastWriteTimeUtc = input.LastWriteTimeUtc,
                Sha256 = input.Sha256
            });
        }

        return row;
    }

    private static IQueryable<ProcessingJobRow> WithGraph(
        IQueryable<ProcessingJobRow> query) =>
        query
            .Include(row => row.InboxItem)
            .Include(row => row.Plan)
                .ThenInclude(row => row.Inputs)
            .Include(row => row.Output);

    private static ProcessingJobRecord ToRecord(
        ProcessingJobRow row,
        string? outputFullPath)
    {
        ProcessingPlanRow plan = row.Plan;
        var definition = new ProcessingPlanDefinition(
            plan.Id,
            row.Id,
            plan.InboxItemId,
            plan.ProcessorKey,
            plan.ProcessorVersion,
            plan.ProcessorDisplayName,
            plan.Capability,
            plan.OutputFilename,
            plan.WorkDirectoryRelativePath,
            JsonSerializer.Deserialize<string[]>(plan.StepKeysJson) ?? [],
            ToInputs(plan.Inputs));
        return new ProcessingJobRecord(
            row.Id,
            row.InboxItemId,
            row.Attempt,
            row.Status,
            row.CurrentStepKey,
            row.ProgressPercent,
            row.ErrorCode,
            row.IsRetryable,
            row.CorrelationId,
            definition,
            row.Output is null
                ? null
                : ToOutputRecord(
                    row.Output,
                    outputFullPath ?? row.Output.RelativePath));
    }

    private static IReadOnlyList<ProcessingPlanInput> ToInputs(
        IEnumerable<ProcessingPlanInputRow> rows) =>
        rows.OrderBy(row => row.Role, StringComparer.Ordinal)
            .Select(row => new ProcessingPlanInput(
                row.SourceFileId,
                row.Role,
                row.FullPath,
                row.Filename,
                row.Size,
                row.LastWriteTimeUtc,
                row.Sha256))
            .ToArray();

    private static ProcessedOutputRecord ToOutputRecord(
        ProcessedOutputRow output,
        string fullPath) =>
        new(
            output.JobId,
            output.InboxItemId,
            fullPath,
            output.RelativePath,
            output.Size,
            output.LastWriteTimeUtc,
            output.Sha256,
            TimeSpan.FromTicks(output.DurationTicks),
            output.HasVideo,
            output.HasAudio,
            output.IsValid);

    private static void ValidateOutputDraft(ProcessedOutputDraft output)
    {
        if (output.Size <= 0
            || output.Duration <= TimeSpan.Zero
            || !output.HasVideo
            || !output.HasAudio
            || output.Sha256.Length != 64
            || Path.IsPathRooted(output.RelativePath))
        {
            throw new InvalidOperationException("The processed output is not valid.");
        }
    }

    private static async Task RequireMatchingControlledOutputAsync(
        MediaDesktopDbContext context,
        ProcessedOutputDraft output,
        CancellationToken cancellationToken)
    {
        AppSettingsRow settings = await context.AppSettings
            .AsNoTracking()
            .SingleAsync(row => row.Id == 1, cancellationToken);
        string expectedPath = ControlledPathGuard.RequireDescendant(
            settings.WorkDirectory,
            Path.Combine(settings.WorkDirectory, output.RelativePath));
        if (!StringComparer.OrdinalIgnoreCase.Equals(expectedPath, Path.GetFullPath(output.FullPath)))
        {
            throw new InvalidOperationException(
                "The processed output path does not match its controlled relative path.");
        }

        var expected = new ProcessedOutputRow
        {
            RelativePath = output.RelativePath,
            Sha256 = output.Sha256,
            Size = output.Size,
            LastWriteTimeUtc = output.LastWriteTimeUtc
        };
        if (!await MatchesOutputAsync(expectedPath, expected, cancellationToken))
        {
            throw new InvalidOperationException(
                "The processed output no longer matches its verified identity.");
        }
    }

    private static async Task<bool> MatchesOutputAsync(
        string fullPath,
        ProcessedOutputRow expected,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(fullPath);
        if (!file.Exists
            || file.Length != expected.Size
            || new DateTimeOffset(file.LastWriteTimeUtc) != expected.LastWriteTimeUtc)
        {
            return false;
        }

        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        string actualSha256 = Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken));
        file.Refresh();
        return file.Exists
            && file.Length == expected.Size
            && new DateTimeOffset(file.LastWriteTimeUtc) == expected.LastWriteTimeUtc
            && StringComparer.Ordinal.Equals(actualSha256, expected.Sha256);
    }
}
