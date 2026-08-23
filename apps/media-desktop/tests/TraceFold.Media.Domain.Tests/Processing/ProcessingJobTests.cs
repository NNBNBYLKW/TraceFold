using TraceFold.Media.Domain.Processing;
using Xunit;

namespace TraceFold.Media.Domain.Tests.Processing;

public sealed class ProcessingJobTests
{
    [Fact]
    public void Progress_is_monotonic_and_bounded()
    {
        ProcessingJob job = ProcessingJob.Queue(Guid.NewGuid(), Guid.NewGuid(), 1);
        job.Start();
        job.Report("merge", 40);
        job.Report("verify", 95);

        Assert.Equal(95, job.ProgressPercent);
        Assert.Equal("verify", job.CurrentStepKey);
        Assert.Throws<InvalidOperationException>(() => job.Report("merge", 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => job.Report("merge", 101));
    }

    [Fact]
    public void Interrupted_job_is_not_resumable_in_place()
    {
        ProcessingJob job = ProcessingJob.Queue(Guid.NewGuid(), Guid.NewGuid(), 1);
        job.Start();

        job.Interrupt("processing.user_stopped");

        Assert.Equal(ProcessingJobStatus.Interrupted, job.Status);
        Assert.Throws<InvalidOperationException>(job.Start);
    }

    [Fact]
    public void Succeed_sets_complete_progress_and_clears_errors()
    {
        ProcessingJob job = ProcessingJob.Queue(Guid.NewGuid(), Guid.NewGuid(), 1);
        job.Start();
        job.Report("merge", 80);

        job.Succeed();

        Assert.Equal(ProcessingJobStatus.Succeeded, job.Status);
        Assert.Equal(100, job.ProgressPercent);
        Assert.Null(job.ErrorCode);
        Assert.False(job.IsRetryable);
    }

    [Fact]
    public void Queued_job_can_cancel_but_running_job_cannot()
    {
        ProcessingJob cancelled = ProcessingJob.Queue(Guid.NewGuid(), Guid.NewGuid(), 1);
        ProcessingJob running = ProcessingJob.Queue(Guid.NewGuid(), Guid.NewGuid(), 1);
        running.Start();

        cancelled.Cancel();

        Assert.Equal(ProcessingJobStatus.Cancelled, cancelled.Status);
        Assert.Throws<InvalidOperationException>(running.Cancel);
    }

    [Fact]
    public void Failure_records_retryability_and_terminalizes_the_job()
    {
        ProcessingJob job = ProcessingJob.Queue(Guid.NewGuid(), Guid.NewGuid(), 1);
        job.Start();

        job.Fail("processing.ffmpeg_failed", isRetryable: true);

        Assert.Equal(ProcessingJobStatus.Failed, job.Status);
        Assert.Equal("processing.ffmpeg_failed", job.ErrorCode);
        Assert.True(job.IsRetryable);
        Assert.Throws<InvalidOperationException>(job.Start);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Queue_rejects_non_positive_attempts(int attempt)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProcessingJob.Queue(Guid.NewGuid(), Guid.NewGuid(), attempt));
    }
}
