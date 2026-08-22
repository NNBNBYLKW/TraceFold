namespace TraceFold.Media.Desktop.Services;

public sealed class ProductionDebounceScheduler(TimeProvider timeProvider)
    : IDebounceScheduler
{
    public Task DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken) =>
        Task.Delay(delay, timeProvider, cancellationToken);
}
