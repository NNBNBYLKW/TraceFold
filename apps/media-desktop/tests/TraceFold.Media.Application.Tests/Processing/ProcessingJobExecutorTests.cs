using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Domain.Processing;
using Xunit;

namespace TraceFold.Media.Application.Tests.Processing;

public sealed class ProcessingJobExecutorTests
{
    [Fact]
    public async Task Executor_runs_one_claimed_job_and_persists_monotonic_progress()
    {
        var processor = new ExecutorProcessor();
        var store = new ExecutorStore([Job(), Job()]);
        ProcessingJobExecutor executor = CreateExecutor(store, processor);

        bool claimed = await executor.ExecuteNextAsync(
            TestContext.Current.CancellationToken);

        Assert.True(claimed);
        Assert.Single(store.CompletedJobs);
        Assert.Single(store.StillQueuedJobs);
        Assert.True(store.ProgressUpdates.SequenceEqual(
            store.ProgressUpdates.OrderBy(update => update.Percent)));
        Assert.Equal(1, store.MaxConcurrentProgressWrites);
    }

    [Fact]
    public async Task Cancelled_execution_is_interrupted_then_the_next_call_claims_next()
    {
        var processor = new ExecutorProcessor(cancelFirst: true);
        var store = new ExecutorStore([Job(), Job()]);
        ProcessingJobExecutor executor = CreateExecutor(store, processor);

        await executor.ExecuteNextAsync(TestContext.Current.CancellationToken);
        bool secondClaimed = await executor.ExecuteNextAsync(
            TestContext.Current.CancellationToken);

        Assert.Single(store.InterruptedJobs);
        Assert.True(secondClaimed);
        Assert.Single(store.CompletedJobs);
    }

    private static ProcessingJobExecutor CreateExecutor(
        ExecutorStore store,
        ExecutorProcessor processor) =>
        new(
            store,
            new MediaProcessorRegistry([processor]),
            new ProcessingExecutionController(),
            new ProcessingProgressBuffer(store),
            new ExecutorWorkspace(),
            new ExecutorSettings(),
            new ExecutorActivity());

    private static ProcessingJobRecord Job()
    {
        Guid itemId = Guid.NewGuid();
        Guid jobId = Guid.NewGuid();
        var plan = new ProcessingPlanDefinition(
            Guid.NewGuid(), jobId, itemId,
            "executor.fake", "1", "Fake", "fake_capability", "output.mp4",
            Path.Combine(".tracefold", "processing", jobId.ToString("N")),
            ["work"], []);
        return new ProcessingJobRecord(
            jobId, itemId, 1, ProcessingJobStatus.Running,
            null, 0, null, false, Guid.NewGuid().ToString("N"), plan, null);
    }

    private sealed class ExecutorProcessor(bool cancelFirst = false) : IMediaProcessor
    {
        private int calls;

        public MediaProcessorDescriptor Descriptor { get; } =
            new("executor.fake", "1", "fake_capability", 100, "Fake");

        public ProcessingPlanDefinition CreatePlan(Guid jobId, ProcessingCandidate candidate) =>
            throw new NotSupportedException();

        public Task<ProcessedOutputDraft> ExecuteAsync(
            ProcessingExecutionContext context,
            IProgress<ProcessingProgressUpdate> progress,
            CancellationToken cancellationToken)
        {
            calls++;
            if (cancelFirst && calls == 1)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            progress.Report(new ProcessingProgressUpdate("work", 20));
            progress.Report(new ProcessingProgressUpdate("work", 80));
            return Task.FromResult(new ProcessedOutputDraft(
                "C:\\work\\output.mp4", "output.mp4", 1,
                DateTimeOffset.UtcNow, new string('A', 64),
                TimeSpan.FromSeconds(1), true, true));
        }
    }

    private sealed class ExecutorStore(IEnumerable<ProcessingJobRecord> jobs)
        : IProcessingStore
    {
        private readonly Queue<ProcessingJobRecord> queued = new(jobs);
        private int concurrentProgressWrites;

        public List<ProcessingProgressUpdate> ProgressUpdates { get; } = [];

        public List<Guid> CompletedJobs { get; } = [];

        public List<Guid> InterruptedJobs { get; } = [];

        public int MaxConcurrentProgressWrites { get; private set; }

        public IReadOnlyList<ProcessingJobRecord> StillQueuedJobs => queued.ToArray();

        public Task<ProcessingJobRecord?> TryClaimNextAsync(CancellationToken cancellationToken) =>
            Task.FromResult(queued.TryDequeue(out ProcessingJobRecord? job) ? job : null);

        public async Task UpdateProgressAsync(Guid jobId, ProcessingProgressUpdate update, CancellationToken cancellationToken)
        {
            int concurrent = Interlocked.Increment(ref concurrentProgressWrites);
            MaxConcurrentProgressWrites = Math.Max(
                MaxConcurrentProgressWrites,
                concurrent);
            try
            {
                await Task.Yield();
                ProgressUpdates.Add(update);
            }
            finally
            {
                Interlocked.Decrement(ref concurrentProgressWrites);
            }
        }

        public Task CompleteAsync(Guid jobId, ProcessedOutputDraft output, CancellationToken cancellationToken)
        {
            CompletedJobs.Add(jobId);
            return Task.CompletedTask;
        }

        public Task InterruptAsync(Guid jobId, string errorCode, CancellationToken cancellationToken)
        {
            InterruptedJobs.Add(jobId);
            return Task.CompletedTask;
        }

        public Task FailAsync(Guid jobId, string errorCode, bool isRetryable, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ProcessingCandidate?> LoadCandidateAsync(Guid inboxItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProcessingJobRecord?> FindActiveAsync(Guid inboxItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<QueueProcessingResult> QueueAsync(PlannedProcessing planned, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CancelQueuedAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProcessingJobRecord>> InterruptRecoverableAsync(string errorCode, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProcessingJobRecord>>([]);
        public Task<ValidatedProcessingOutput?> LoadValidatedOutputAsync(Guid inboxItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid?> InvalidateForSourceChangeAsync(Guid inboxItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ExecutorWorkspace : IProcessingWorkspace
    {
        public Task<ProcessingWorkspacePaths> PrepareAsync(Guid jobId, LibraryFolders folders, string outputFilename, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> PromoteVerifiedAsync(Guid jobId, LibraryFolders folders, ProcessingWorkspacePaths paths, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CleanupUnverifiedAsync(Guid jobId, LibraryFolders folders, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ExecutorSettings : IDirectorySettingsStore
    {
        public Task<LibraryFolders?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<LibraryFolders?>(new("C:\\inbox", "C:\\library", "C:\\work"));

        public Task SaveAsync(LibraryFolders folders, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ExecutorActivity : IInboxActivityNotifier
    {
        public event EventHandler<InboxActivityEventArgs>? Changed;
        public void Publish(InboxActivityKind kind) => Changed?.Invoke(this, new InboxActivityEventArgs(kind));
        public Task WaitForCandidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
