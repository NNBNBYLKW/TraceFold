using Microsoft.Extensions.Logging.Abstractions;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Startup;
using TraceFold.Media.Domain.Processing;
using TraceFold.Media.Infrastructure.Workers;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Workers;

public sealed class ProcessingQueueWorkerTests
{
    [Fact]
    public async Task Cycle_claims_only_one_job_at_a_time()
    {
        var executor = new WorkerExecutor([true, true]);
        var worker = CreateWorker(executor, new WorkerSignal(), new WorkerStore());

        await worker.RunCycleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, executor.CallCount);
    }

    [Fact]
    public async Task Empty_cycle_waits_for_a_signal()
    {
        var signal = new WorkerSignal();
        var worker = CreateWorker(
            new WorkerExecutor([false]),
            signal,
            new WorkerStore());

        Task cycle = worker.RunCycleAsync(TestContext.Current.CancellationToken);
        await signal.WaitEntered;
        signal.Release();
        await cycle;

        Assert.Equal(1, signal.WaitCount);
    }

    [Fact]
    public async Task Stop_interrupts_recoverable_jobs_after_worker_has_stopped()
    {
        var gate = new MediaRuntimeGate();
        var store = new WorkerStore();
        var worker = CreateWorker(
            new WorkerExecutor([false]),
            new WorkerSignal(),
            store,
            gate);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        gate.Open();

        await worker.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal("processing.shutdown_interrupted", store.ErrorCode);
    }

    private static ProcessingQueueWorker CreateWorker(
        WorkerExecutor executor,
        WorkerSignal signal,
        WorkerStore store,
        IMediaRuntimeGate? gate = null) =>
        new(executor, signal, store, gate ?? new OpenGate(),
            NullLogger<ProcessingQueueWorker>.Instance);

    private sealed class WorkerExecutor(IEnumerable<bool> results)
        : IProcessingJobExecutor
    {
        private readonly Queue<bool> results = new(results);

        public int CallCount { get; private set; }

        public Task<bool> ExecuteNextAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(results.TryDequeue(out bool result) && result);
        }
    }

    private sealed class WorkerSignal : IProcessingQueueSignal
    {
        private readonly TaskCompletionSource entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitEntered => entered.Task;

        public int WaitCount { get; private set; }

        public void Signal() => release.TrySetResult();

        public void Release() => release.TrySetResult();

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            WaitCount++;
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class WorkerStore : IProcessingStore
    {
        public string? ErrorCode { get; private set; }

        public Task<IReadOnlyList<ProcessingJobRecord>> InterruptRecoverableAsync(
            string errorCode,
            CancellationToken cancellationToken)
        {
            ErrorCode = errorCode;
            return Task.FromResult<IReadOnlyList<ProcessingJobRecord>>([]);
        }

        public Task<ProcessingCandidate?> LoadCandidateAsync(Guid inboxItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProcessingJobRecord?> FindActiveAsync(Guid inboxItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<QueueProcessingResult> QueueAsync(PlannedProcessing planned, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProcessingJobRecord?> TryClaimNextAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateProgressAsync(Guid jobId, ProcessingProgressUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteAsync(Guid jobId, ProcessedOutputDraft output, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancelQueuedAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task InterruptAsync(Guid jobId, string errorCode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FailAsync(Guid jobId, string errorCode, bool isRetryable, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ValidatedProcessingOutput?> LoadValidatedOutputAsync(Guid inboxItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid?> InvalidateForSourceChangeAsync(Guid inboxItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class OpenGate : IMediaRuntimeGate
    {
        public Task WaitUntilReadyAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Open()
        {
        }
    }
}
