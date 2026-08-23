using TraceFold.Media.Application.Processing;
using TraceFold.Media.Domain.Processing;
using Xunit;

namespace TraceFold.Media.Application.Tests.Processing;

public sealed class ProcessingInvalidationServiceTests
{
    [Fact]
    public async Task Source_change_invalidates_the_item_and_cancels_only_returned_job()
    {
        Guid itemId = Guid.NewGuid();
        Guid runningJobId = Guid.NewGuid();
        var store = new InvalidationStore(runningJobId);
        var controller = new InvalidationController();
        var service = new ProcessingInvalidationService(store, controller);

        await service.InvalidateAsync(itemId, TestContext.Current.CancellationToken);

        Assert.Equal(itemId, store.InvalidatedItemId);
        Assert.Equal([runningJobId], controller.CancelledJobIds);
    }

    private sealed class InvalidationStore(Guid? runningJobId) : IProcessingStore
    {
        public Guid? InvalidatedItemId { get; private set; }

        public Task<Guid?> InvalidateForSourceChangeAsync(Guid inboxItemId, CancellationToken cancellationToken)
        {
            InvalidatedItemId = inboxItemId;
            return Task.FromResult(runningJobId);
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
        public Task<IReadOnlyList<ProcessingJobRecord>> InterruptRecoverableAsync(string errorCode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ValidatedProcessingOutput?> LoadValidatedOutputAsync(Guid inboxItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class InvalidationController : IProcessingExecutionController
    {
        public List<Guid> CancelledJobIds { get; } = [];

        public IDisposable Register(Guid jobId, CancellationTokenSource source) => throw new NotSupportedException();

        public bool Cancel(Guid jobId)
        {
            CancelledJobIds.Add(jobId);
            return true;
        }
    }
}
