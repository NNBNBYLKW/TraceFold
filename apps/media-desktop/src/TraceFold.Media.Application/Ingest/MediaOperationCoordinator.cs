namespace TraceFold.Media.Application.Ingest;

public sealed class MediaOperationCoordinator : IMediaOperationCoordinator
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _shutdown = new();
    private bool _accepting = true;
    private int _activeCount;
    private TaskCompletionSource _drained = CompletedSource();

    public IMediaOperationLease Begin(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (!_accepting)
            {
                throw new MediaOperationRejectedException();
            }

            if (_activeCount == 0)
            {
                _drained = NewSource();
            }

            _activeCount++;
            CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _shutdown.Token);
            return new Lease(this, linked);
        }
    }

    public async Task StopAcceptingAndDrainAsync(
        CancellationToken cancellationToken)
    {
        Task drained;
        bool cancel;
        lock (_sync)
        {
            cancel = _accepting;
            _accepting = false;
            drained = _drained.Task;
        }

        if (cancel)
        {
            _shutdown.Cancel();
        }

        await drained.WaitAsync(cancellationToken);
    }

    private void Release()
    {
        TaskCompletionSource? completed = null;
        lock (_sync)
        {
            if (_activeCount <= 0)
            {
                throw new InvalidOperationException(
                    "The media operation count is already zero.");
            }

            _activeCount--;
            if (_activeCount == 0)
            {
                completed = _drained;
            }
        }

        completed?.TrySetResult();
    }

    private static TaskCompletionSource NewSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource CompletedSource()
    {
        TaskCompletionSource source = NewSource();
        source.SetResult();
        return source;
    }

    private sealed class Lease(
        MediaOperationCoordinator owner,
        CancellationTokenSource linked) : IMediaOperationLease
    {
        private int _disposed;

        public CancellationToken CancellationToken => linked.Token;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                linked.Dispose();
                owner.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
