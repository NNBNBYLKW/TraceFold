namespace TraceFold.Media.Application.Processing;

public sealed class ProcessingInvalidationService(
    IProcessingStore store,
    IProcessingExecutionController controller)
    : IProcessingInvalidationService
{
    public async Task InvalidateAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        Guid? runningJobId = await store.InvalidateForSourceChangeAsync(
            inboxItemId,
            cancellationToken);
        if (runningJobId.HasValue)
        {
            controller.Cancel(runningJobId.Value);
        }
    }
}
