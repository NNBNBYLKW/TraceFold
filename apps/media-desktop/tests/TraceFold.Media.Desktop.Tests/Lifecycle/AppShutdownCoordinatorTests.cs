using Microsoft.Extensions.Logging;
using TraceFold.Media.Desktop.Lifecycle;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Lifecycle;

public sealed class AppShutdownCoordinatorTests
{
    [Fact]
    public async Task StopAsync_called_concurrently_stops_the_host_once()
    {
        var stopper = new RecordingHostStopper();
        var coordinator = new AppShutdownCoordinator(
            stopper,
            new RecordingLogger<AppShutdownCoordinator>(),
            TimeSpan.FromSeconds(1));

        await Task.WhenAll(
            coordinator.StopAsync(CancellationToken.None),
            coordinator.StopAsync(CancellationToken.None));

        Assert.Equal(1, stopper.CallCount);
    }

    [Fact]
    public async Task StopAsync_logs_timeout_and_returns_without_throwing()
    {
        var logger = new RecordingLogger<AppShutdownCoordinator>();
        var stopper = new RecordingHostStopper(
            cancellationToken =>
                Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        var coordinator = new AppShutdownCoordinator(
            stopper,
            logger,
            TimeSpan.FromMilliseconds(20));

        await coordinator.StopAsync(CancellationToken.None);

        Assert.True(stopper.LastToken.IsCancellationRequested);
        Assert.Contains(
            logger.Events,
            item => item.Contains(
                "DesktopShutdownTimedOut",
                StringComparison.Ordinal));
    }

    private sealed class RecordingHostStopper(
        Func<CancellationToken, Task>? callback = null) : IDesktopHostStopper
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public CancellationToken LastToken { get; private set; }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            LastToken = cancellationToken;
            return callback?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Events { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Events.Add(formatter(state, exception));
        }
    }
}
