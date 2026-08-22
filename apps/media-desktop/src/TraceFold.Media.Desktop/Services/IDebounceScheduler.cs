namespace TraceFold.Media.Desktop.Services;

public interface IDebounceScheduler
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
