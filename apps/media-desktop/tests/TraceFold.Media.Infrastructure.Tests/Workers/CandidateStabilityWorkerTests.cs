using Microsoft.Extensions.Logging.Abstractions;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Infrastructure.Workers;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Workers;

public sealed class CandidateStabilityWorkerTests
{
    [Fact]
    public async Task RunCycleAsync_waits_two_seconds_then_runs_stability_scan()
    {
        var scanner = new RecordingScanner();
        var timeProvider = new ImmediateTimeProvider();
        var worker = new CandidateStabilityWorker(
            scanner,
            new WaitingStore(hasWaitingCandidates: true),
            new RecordingActivityNotifier(),
            timeProvider,
            NullLogger<CandidateStabilityWorker>.Instance);

        await worker.RunCycleAsync(CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(2)], timeProvider.DueTimes);
        Assert.Equal([ScanTrigger.StabilityCheck], scanner.Triggers);
    }

    [Fact]
    public async Task RunCycleAsync_waits_for_signal_without_scanning_when_idle()
    {
        var scanner = new RecordingScanner();
        var activity = new RecordingActivityNotifier();
        var worker = new CandidateStabilityWorker(
            scanner,
            new WaitingStore(hasWaitingCandidates: false),
            activity,
            new ImmediateTimeProvider(),
            NullLogger<CandidateStabilityWorker>.Instance);

        await worker.RunCycleAsync(CancellationToken.None);

        Assert.Equal(1, activity.WaitCount);
        Assert.Empty(scanner.Triggers);
    }

    private sealed class RecordingScanner : IInboxScanner
    {
        public List<ScanTrigger> Triggers { get; } = [];

        public Task<ScanResult> ScanAsync(
            ScanTrigger trigger,
            CancellationToken cancellationToken)
        {
            Triggers.Add(trigger);
            return Task.FromResult(new ScanResult(
                ScanOutcome.Completed,
                0,
                0,
                0,
                DateTimeOffset.UtcNow,
                string.Empty));
        }
    }

    private sealed class WaitingStore(bool hasWaitingCandidates) : IInboxScanStore
    {
        public Task<IReadOnlyDictionary<string, DateTimeOffset>> LoadFirstObservedAsync(
            IReadOnlySet<string> normalizedPaths,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ScanPersistenceResult> ReconcileAsync(
            ScanReconciliation reconciliation,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> HasWaitingCandidatesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(hasWaitingCandidates);

        public Task ClearMissingAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingActivityNotifier : IInboxActivityNotifier
    {
        public event EventHandler<InboxActivityEventArgs>? Changed;

        public int WaitCount { get; private set; }

        public void Publish(InboxActivityKind kind) =>
            Changed?.Invoke(this, new InboxActivityEventArgs(kind));

        public Task WaitForCandidateAsync(CancellationToken cancellationToken)
        {
            WaitCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ImmediateTimeProvider : TimeProvider
    {
        public List<TimeSpan> DueTimes { get; } = [];

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            DueTimes.Add(dueTime);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new ImmediateTimer();
        }

        private sealed class ImmediateTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => false;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
