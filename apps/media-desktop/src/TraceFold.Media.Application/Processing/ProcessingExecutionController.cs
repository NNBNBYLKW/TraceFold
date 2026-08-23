namespace TraceFold.Media.Application.Processing;

public sealed class ProcessingExecutionController : IProcessingExecutionController
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, CancellationTokenSource> sources = [];

    public IDisposable Register(Guid jobId, CancellationTokenSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (sync)
        {
            if (!sources.TryAdd(jobId, source))
            {
                throw new InvalidOperationException(
                    "The processing job is already registered as running.");
            }
        }

        return new Registration(this, jobId, source);
    }

    public bool Cancel(Guid jobId)
    {
        CancellationTokenSource? source;
        lock (sync)
        {
            sources.TryGetValue(jobId, out source);
        }

        if (source is null)
        {
            return false;
        }

        source.Cancel();
        return true;
    }

    private void Remove(Guid jobId, CancellationTokenSource source)
    {
        lock (sync)
        {
            if (sources.TryGetValue(jobId, out CancellationTokenSource? current)
                && ReferenceEquals(current, source))
            {
                sources.Remove(jobId);
            }
        }
    }

    private sealed class Registration(
        ProcessingExecutionController owner,
        Guid jobId,
        CancellationTokenSource source) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.Remove(jobId, source);
            }
        }
    }
}
