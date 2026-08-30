using Microsoft.EntityFrameworkCore;
using TraceFold.Media.Application.Diagnostics;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Domain.Processing;

namespace TraceFold.Media.Infrastructure.Persistence;

public sealed class EfBackgroundWorkSummaryStore(
    IReadOnlyDiagnosticDbContextFactory contextFactory)
    : IBackgroundWorkSummaryStore
{
    public async Task<BackgroundWorkSummary> LoadAsync(
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<ProcessingJobRow> processing =
            context.ProcessingJobs.AsNoTracking();
        IQueryable<IngestOperationRow> ingest =
            context.IngestOperations.AsNoTracking();

        int queued = await processing.CountAsync(
            row => row.Status == ProcessingJobStatus.Queued,
            cancellationToken);
        int running = await processing.CountAsync(
            row => row.Status == ProcessingJobStatus.Running,
            cancellationToken);
        int interrupted = await processing.CountAsync(
            row => row.Status == ProcessingJobStatus.Interrupted,
            cancellationToken);
        int failed = await processing.CountAsync(
            row => row.Status == ProcessingJobStatus.Failed,
            cancellationToken);
        int recoveryPending = await ingest.CountAsync(
            row => row.Stage == IngestOperationStage.RecoveryBlocked
                || row.Stage == IngestOperationStage.CleanupPending,
            cancellationToken);
        return new BackgroundWorkSummary(
            queued,
            running,
            interrupted,
            failed,
            recoveryPending);
    }
}
