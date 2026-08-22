using Microsoft.UI.Dispatching;

namespace TraceFold.Media.Desktop.Services;

public sealed class DispatcherQueueAdapter(DispatcherQueue dispatcherQueue)
    : IUiDispatcher
{
    public void Enqueue(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (dispatcherQueue.HasThreadAccess)
        {
            callback();
            return;
        }

        if (!dispatcherQueue.TryEnqueue(() => callback()))
        {
            throw new InvalidOperationException(
                "The UI dispatcher rejected the callback.");
        }
    }
}
