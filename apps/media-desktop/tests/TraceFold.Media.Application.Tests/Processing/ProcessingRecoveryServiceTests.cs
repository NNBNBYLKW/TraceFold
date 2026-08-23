using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Domain.Processing;
using Xunit;

namespace TraceFold.Media.Application.Tests.Processing;

public sealed class ProcessingRecoveryServiceTests
{
    [Fact]
    public async Task Recovery_interrupts_stale_jobs_and_cleans_only_their_workspaces()
    {
        ProcessingJobRecord[] stale = [Job(), Job()];
        var store = new RecoveryStore(stale);
        var workspace = new RecoveryWorkspace();
        var recovery = new ProcessingRecoveryService(
            store,
            workspace,
            new RecoverySettings(),
            new RecoveryActivity());

        await recovery.ReconcileAsync(TestContext.Current.CancellationToken);

        Assert.Equal(stale.Select(job => job.Id), workspace.CleanedJobIds);
        Assert.Equal("processing.startup_interrupted", store.ErrorCode);
    }

    private static ProcessingJobRecord Job()
    {
        Guid id = Guid.NewGuid();
        Guid item = Guid.NewGuid();
        var plan = new ProcessingPlanDefinition(
            Guid.NewGuid(), id, item, "p", "1", "P", "c", "o.mp4",
            Path.Combine(".tracefold", "processing", id.ToString("N")), [], []);
        return new ProcessingJobRecord(
            id, item, 1, ProcessingJobStatus.Interrupted, null, 0,
            "processing.startup_interrupted", true, Guid.NewGuid().ToString("N"),
            plan, null);
    }

    private sealed class RecoveryStore(IReadOnlyList<ProcessingJobRecord> jobs) : IProcessingStore
    {
        public string? ErrorCode { get; private set; }

        public Task<IReadOnlyList<ProcessingJobRecord>> InterruptRecoverableAsync(string errorCode, CancellationToken cancellationToken)
        {
            ErrorCode = errorCode;
            return Task.FromResult(jobs);
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

    private sealed class RecoveryWorkspace : IProcessingWorkspace
    {
        public List<Guid> CleanedJobIds { get; } = [];

        public Task CleanupUnverifiedAsync(Guid jobId, LibraryFolders folders, CancellationToken cancellationToken)
        {
            CleanedJobIds.Add(jobId);
            return Task.CompletedTask;
        }

        public Task<ProcessingWorkspacePaths> PrepareAsync(Guid jobId, LibraryFolders folders, string outputFilename, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> PromoteVerifiedAsync(Guid jobId, LibraryFolders folders, ProcessingWorkspacePaths paths, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecoverySettings : IDirectorySettingsStore
    {
        public Task<LibraryFolders?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<LibraryFolders?>(new("C:\\i", "C:\\l", "C:\\w"));
        public Task SaveAsync(LibraryFolders folders, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecoveryActivity : IInboxActivityNotifier
    {
        public event EventHandler<InboxActivityEventArgs>? Changed;
        public void Publish(InboxActivityKind kind) => Changed?.Invoke(this, new InboxActivityEventArgs(kind));
        public Task WaitForCandidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
