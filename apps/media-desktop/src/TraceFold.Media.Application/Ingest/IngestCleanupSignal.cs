using System.Threading.Channels;

namespace TraceFold.Media.Application.Ingest;

public sealed class IngestCleanupSignal : IIngestCleanupSignal
{
    private readonly Channel<bool> signals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

    public void Signal() => signals.Writer.TryWrite(true);

    public async Task WaitAsync(CancellationToken cancellationToken) =>
        await signals.Reader.ReadAsync(cancellationToken);
}
