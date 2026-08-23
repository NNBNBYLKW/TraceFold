namespace TraceFold.Media.Application.Processing;

public sealed class ProcessingProgressBuffer(IProcessingStore store)
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, JobProgressState> states = [];

    public void Publish(Guid jobId, ProcessingProgressUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (sync)
        {
            if (!states.TryGetValue(jobId, out JobProgressState? state))
            {
                state = new JobProgressState();
                states.Add(jobId, state);
            }

            if (update.Percent < state.HighestPercent)
            {
                return;
            }

            state.HighestPercent = update.Percent;
            state.Pending = update;
            state.Writer ??= DrainAsync(jobId, state);
        }
    }

    public async Task FlushAsync(Guid jobId, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task? writer;
            lock (sync)
            {
                writer = states.TryGetValue(jobId, out JobProgressState? state)
                    ? state.Writer
                    : null;
            }

            if (writer is null)
            {
                return;
            }

            await writer.WaitAsync(cancellationToken);
        }
    }

    private async Task DrainAsync(Guid jobId, JobProgressState state)
    {
        while (true)
        {
            ProcessingProgressUpdate? update;
            lock (sync)
            {
                update = state.Pending;
                state.Pending = null;
                if (update is null)
                {
                    state.Writer = null;
                    states.Remove(jobId);
                    return;
                }
            }

            await store.UpdateProgressAsync(jobId, update, CancellationToken.None);
        }
    }

    private sealed class JobProgressState
    {
        public int HighestPercent { get; set; } = -1;

        public ProcessingProgressUpdate? Pending { get; set; }

        public Task? Writer { get; set; }
    }
}
