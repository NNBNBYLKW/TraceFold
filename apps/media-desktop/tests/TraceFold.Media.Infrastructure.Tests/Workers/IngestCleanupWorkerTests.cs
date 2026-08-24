using Microsoft.Extensions.Logging.Abstractions;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Startup;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Infrastructure.Workers;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Workers;

public sealed class IngestCleanupWorkerTests
{
    private static readonly LibraryFolders Folders = new(
        "C:/inbox",
        "C:/library",
        "C:/work");

    [Fact]
    public async Task Due_cleanup_failure_schedules_next_attempt_without_blocking_sibling()
    {
        IngestOperationRecord locked = Operation(1);
        IngestOperationRecord deletable = Operation(0);
        var store = new WorkerStore([locked, deletable]);
        var cleanup = new WorkerCleanup(async (operation, token) =>
        {
            if (operation.Id == locked.Id)
            {
                int failures = operation.CleanupAttemptCount + 1;
                await store.ScheduleCleanupRetryAsync(
                    operation.Id,
                    "ingest.cleanup_failed",
                    failures,
                    DateTimeOffset.Parse("2026-08-24T09:02:00Z"),
                    token);
                return new CleanupResult(false, "ingest.cleanup_failed");
            }

            store.Completed.Add(operation.Id);
            return new CleanupResult(true, null);
        });
        IngestCleanupWorker worker = CreateWorker(store, cleanup);

        await worker.RunDueAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, store.ScheduleCalls[locked.Id]);
        Assert.Contains(deletable.Id, store.Completed);
    }

    [Fact]
    public async Task Cancellation_stops_the_due_batch_immediately()
    {
        IngestOperationRecord first = Operation(0);
        IngestOperationRecord sibling = Operation(0);
        var store = new WorkerStore([first, sibling]);
        using var cancellation = new CancellationTokenSource();
        var cleanup = new WorkerCleanup((operation, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<CleanupResult>(cancellation.Token);
        });
        IngestCleanupWorker worker = CreateWorker(store, cleanup);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            worker.RunDueAsync(cancellation.Token));

        Assert.Equal(1, cleanup.CallCount);
    }

    [Fact]
    public async Task Startup_reactivates_exhausted_cleanup_once_after_runtime_gate_opens()
    {
        var store = new WorkerStore([]);
        var gate = new MediaRuntimeGate();
        var worker = CreateWorker(store, new WorkerCleanup(), gate: gate);
        await worker.StartAsync(TestContext.Current.CancellationToken);

        gate.Open();
        await store.Reactivated.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, store.ReactivateCount);
    }

    [Fact]
    public async Task Future_due_wait_is_interrupted_by_coalesced_signal()
    {
        var store = new WorkerStore([])
        {
            NextDue = DateTimeOffset.Parse("2026-08-25T09:00:00Z")
        };
        var signal = new IngestCleanupSignal();
        signal.Signal();
        signal.Signal();
        IngestCleanupWorker worker = CreateWorker(
            store,
            new WorkerCleanup(),
            signal: signal);

        await worker.WaitForWorkAsync(TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            signal.WaitAsync(cancellation.Token));

        signal.Signal();
        await signal.WaitAsync(TestContext.Current.CancellationToken);
    }

    private static IngestCleanupWorker CreateWorker(
        WorkerStore store,
        WorkerCleanup cleanup,
        IMediaRuntimeGate? gate = null,
        IIngestCleanupSignal? signal = null) =>
        new(
            store,
            cleanup,
            new FixedSettingsStore(),
            signal ?? new IngestCleanupSignal(),
            gate ?? new OpenGate(),
            new FixedTimeProvider(
                DateTimeOffset.Parse("2026-08-24T09:00:00Z")),
            NullLogger<IngestCleanupWorker>.Instance);

    private sealed class WorkerStore(
        IReadOnlyList<IngestOperationRecord> due)
        : IIngestCleanupWorkStore
    {
        public Dictionary<Guid, int> ScheduleCalls { get; } = [];
        public HashSet<Guid> Completed { get; } = [];
        public TaskCompletionSource Reactivated { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ReactivateCount { get; private set; }
        public DateTimeOffset? NextDue { get; set; }

        public Task<IReadOnlyList<IngestOperationRecord>> LoadDueCleanupAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken) => Task.FromResult(due);

        public Task<DateTimeOffset?> FindNextCleanupDueAsync(
            CancellationToken cancellationToken) => Task.FromResult(NextDue);

        public Task ScheduleCleanupRetryAsync(
            Guid operationId,
            string errorCode,
            int failureCount,
            DateTimeOffset? nextAttemptAtUtc,
            CancellationToken cancellationToken)
        {
            ScheduleCalls[operationId] =
                ScheduleCalls.GetValueOrDefault(operationId) + 1;
            return Task.CompletedTask;
        }

        public Task ResetCleanupRetryAsync(
            Guid operationId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ReactivateCleanupForStartupAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            ReactivateCount++;
            Reactivated.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class WorkerCleanup(
        Func<IngestOperationRecord, CancellationToken, Task<CleanupResult>>? run = null)
        : IIngestCleanupService
    {
        public int CallCount { get; private set; }

        public Task<CleanupResult> CleanupAsync(
            IngestOperationRecord operation,
            LibraryFolders folders,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return run?.Invoke(operation, cancellationToken)
                ?? Task.FromResult(new CleanupResult(true, null));
        }
    }

    private sealed class FixedSettingsStore : IDirectorySettingsStore
    {
        public Task<LibraryFolders?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<LibraryFolders?>(Folders);
        public Task SaveAsync(LibraryFolders value, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class OpenGate : IMediaRuntimeGate
    {
        public Task WaitUntilReadyAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public void Open()
        {
        }
    }

    private static IngestOperationRecord Operation(int cleanupAttemptCount) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            IngestOperationStage.CleanupPending,
            IngestDisposition.Create,
            null,
            "C:/inbox/photo.jpg",
            3,
            DateTimeOffset.UtcNow,
            new string('A', 64),
            "photo.jpg",
            "C:/work/payload",
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            null,
            null,
            IngestBackupState.NotRequired,
            null,
            null,
            null,
            null,
            "ingest.cleanup_failed",
            cleanupAttemptCount,
            DateTimeOffset.Parse("2026-08-24T09:00:00Z"));
}
