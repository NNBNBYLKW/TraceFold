namespace TraceFold.Media.Application.Startup;

public interface IMediaRuntimeGate
{
    Task WaitUntilReadyAsync(CancellationToken cancellationToken);

    void Open();
}

public sealed class MediaRuntimeGate : IMediaRuntimeGate
{
    private readonly TaskCompletionSource _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitUntilReadyAsync(CancellationToken cancellationToken) =>
        _ready.Task.WaitAsync(cancellationToken);

    public void Open() => _ready.TrySetResult();
}
