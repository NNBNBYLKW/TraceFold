using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Desktop.Lifecycle;
using TraceFold.Media.Desktop.Services;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Lifecycle;

public sealed class AppShutdownCoordinatorTests
{
    [Fact]
    public async Task StopAsync_called_concurrently_stops_the_host_once()
    {
        var stopper = new RecordingHostStopper();
        var coordinator = new AppShutdownCoordinator(
            new RecordingEditorRegistry(),
            new RecordingMediaOperationCoordinator(),
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
            new RecordingEditorRegistry(),
            new RecordingMediaOperationCoordinator(),
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

    [Fact]
    public async Task StopAsync_flushes_metadata_before_draining_and_stopping_host()
    {
        var sequence = new List<string>();
        var coordinator = new AppShutdownCoordinator(
            new RecordingEditorRegistry(sequence),
            new RecordingMediaOperationCoordinator(sequence),
            new RecordingHostStopper(_ =>
            {
                sequence.Add("stop");
                return Task.CompletedTask;
            }),
            new RecordingLogger<AppShutdownCoordinator>());

        await coordinator.StopAsync(CancellationToken.None);

        Assert.Equal(["flush", "drain", "stop"], sequence);
    }

    [Fact]
    public async Task StopAsync_continues_after_metadata_flush_failure()
    {
        var sequence = new List<string>();
        var logger = new RecordingLogger<AppShutdownCoordinator>();
        var coordinator = new AppShutdownCoordinator(
            new RecordingEditorRegistry(sequence, throwOnFlush: true),
            new RecordingMediaOperationCoordinator(sequence),
            new RecordingHostStopper(_ =>
            {
                sequence.Add("stop");
                return Task.CompletedTask;
            }),
            logger);

        await coordinator.StopAsync(CancellationToken.None);

        Assert.Equal(["flush", "drain", "stop"], sequence);
        Assert.Contains(
            logger.Events,
            item => item.Contains(
                "MetadataFlushDuringShutdownFailed",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task StopAsync_still_invokes_host_stop_after_drain_timeout()
    {
        var sequence = new List<string>();
        var logger = new RecordingLogger<AppShutdownCoordinator>();
        var stopper = new RecordingHostStopper(_ =>
        {
            sequence.Add("stop");
            return Task.CompletedTask;
        });
        var coordinator = new AppShutdownCoordinator(
            new RecordingEditorRegistry(sequence),
            new RecordingMediaOperationCoordinator(
                sequence,
                token => Task.Delay(Timeout.InfiniteTimeSpan, token)),
            stopper,
            logger,
            TimeSpan.FromMilliseconds(20));

        await coordinator.StopAsync(CancellationToken.None);

        Assert.Equal(["flush", "drain", "stop"], sequence);
        Assert.Equal(1, stopper.CallCount);
        Assert.Contains(
            logger.Events,
            item => item.Contains(
                "MediaOperationDrainTimedOut",
                StringComparison.Ordinal));
    }

    private sealed class RecordingEditorRegistry(
        List<string>? sequence = null,
        bool throwOnFlush = false) : IMetadataEditorRegistry
    {
        public void Register(IMetadataEditorFlushable editor) => _ = editor;

        public Task FlushAllAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sequence?.Add("flush");
            return throwOnFlush
                ? Task.FromException(new InvalidOperationException("flush failed"))
                : Task.CompletedTask;
        }
    }

    private sealed class RecordingMediaOperationCoordinator(
        List<string>? sequence = null,
        Func<CancellationToken, Task>? drain = null) : IMediaOperationCoordinator
    {
        public IMediaOperationLease Begin(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StopAcceptingAndDrainAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sequence?.Add("drain");
            return drain?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }
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
