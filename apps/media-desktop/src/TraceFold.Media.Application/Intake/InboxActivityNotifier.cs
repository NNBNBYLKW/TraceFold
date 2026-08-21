using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace TraceFold.Media.Application.Intake;

public enum InboxActivityKind
{
    CandidateWaiting,
    ScanCompleted,
    ItemChanged
}

public sealed class InboxActivityEventArgs(InboxActivityKind kind) : EventArgs
{
    public InboxActivityKind Kind { get; } = kind;
}

public interface IInboxActivityNotifier
{
    event EventHandler<InboxActivityEventArgs>? Changed;

    void Publish(InboxActivityKind kind);

    Task WaitForCandidateAsync(CancellationToken cancellationToken);
}

public sealed class InboxActivityNotifier(
    ILogger<InboxActivityNotifier>? logger = null) : IInboxActivityNotifier
{
    private readonly Channel<bool> _candidateSignals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

    public event EventHandler<InboxActivityEventArgs>? Changed;

    public void Publish(InboxActivityKind kind)
    {
        if (kind == InboxActivityKind.CandidateWaiting)
        {
            _candidateSignals.Writer.TryWrite(true);
        }

        var eventArgs = new InboxActivityEventArgs(kind);
        Delegate[] listeners = Changed?.GetInvocationList() ?? [];
        foreach (EventHandler<InboxActivityEventArgs> listener in
                 listeners.Cast<EventHandler<InboxActivityEventArgs>>())
        {
            try
            {
                listener(this, eventArgs);
            }
            catch (Exception error)
            {
                logger?.LogError(
                    error,
                    "InboxActivityListenerFailed Kind={Kind}",
                    kind);
            }
        }
    }

    public async Task WaitForCandidateAsync(CancellationToken cancellationToken)
    {
        await _candidateSignals.Reader.ReadAsync(cancellationToken);
    }
}
