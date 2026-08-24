using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Ingest;
using Xunit;

namespace TraceFold.Media.Application.Tests.Ingest;

public sealed class IngestCleanupServiceTests
{
    [Theory]
    [InlineData(IngestCleanupKind.InboxSource)]
    [InlineData(IngestCleanupKind.ProcessingOutput)]
    [InlineData(IngestCleanupKind.ReplacementBackup)]
    public async Task Locked_cleanup_kind_schedules_the_exact_first_retry(
        IngestCleanupKind kind)
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-24T09:00:00Z");
        IngestOperationRecord operation = Operation();
        var store = new CleanupStore(
            [Entry(operation.Id, kind, "C:/controlled/locked.bin")]);
        var signal = new CleanupSignal();
        var cleanup = new IngestCleanupService(
            store,
            new ThrowingCleanupStorage(new IOException("locked")),
            workStore: store,
            timeProvider: new FixedTimeProvider(now),
            signal: signal);

        CleanupResult result = await cleanup.CleanupAsync(
            operation,
            new LibraryFolders("C:/inbox", "C:/library", "C:/work"),
            TestContext.Current.CancellationToken);

        Assert.False(result.Completed);
        Assert.Equal(1, store.ScheduledFailureCount);
        Assert.Equal(now.AddSeconds(30), store.ScheduledAtUtc);
        Assert.Equal("ingest.cleanup_failed", store.ScheduledErrorCode);
        Assert.Equal(1, signal.Count);
    }

    [Fact]
    public async Task Missing_source_is_completed_without_scheduling_retry()
    {
        IngestOperationRecord operation = Operation();
        var store = new CleanupStore(
            [Entry(operation.Id, IngestCleanupKind.InboxSource, "C:/inbox/missing")]);
        var cleanup = new IngestCleanupService(
            store,
            new CleanupStorage(SourceDeleteOutcome.SourceMissing),
            workStore: store,
            timeProvider: TimeProvider.System);

        CleanupResult result = await cleanup.CleanupAsync(
            operation,
            new LibraryFolders("C:/inbox", "C:/library", "C:/work"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Completed);
        Assert.Single(store.CompletedCleanupEntryIds);
        Assert.Null(store.ScheduledFailureCount);
    }

    [Fact]
    public async Task Cancellation_is_propagated_without_scheduling_retry()
    {
        IngestOperationRecord operation = Operation();
        var store = new CleanupStore(
            [Entry(operation.Id, IngestCleanupKind.InboxSource, "C:/inbox/file")]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cleanup = new IngestCleanupService(
            store,
            new ThrowingCleanupStorage(
                new OperationCanceledException(cancellation.Token)),
            workStore: store);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cleanup.CleanupAsync(operation, new LibraryFolders(
                "C:/inbox", "C:/library", "C:/work"), cancellation.Token));

        Assert.Null(store.ScheduledFailureCount);
    }

    [Fact]
    public async Task Cleanup_marks_each_entry_only_after_deleted_or_already_missing()
    {
        IngestOperationRecord operation = Operation();
        IngestCleanupEntryRecord[] entries =
        [
            Entry(operation.Id, IngestCleanupKind.InboxSource, "C:/inbox/a.mp4"),
            Entry(operation.Id, IngestCleanupKind.InboxSource, "C:/inbox/a.m4a"),
            Entry(operation.Id, IngestCleanupKind.ProcessingOutput, "C:/work/output.mp4")
        ];
        var store = new CleanupStore(entries);
        var storage = new CleanupStorage(
            SourceDeleteOutcome.Deleted,
            SourceDeleteOutcome.SourceMissing,
            SourceDeleteOutcome.SourceChanged);
        var cleanup = new IngestCleanupService(store, storage);

        CleanupResult result = await cleanup.CleanupAsync(
            operation,
            new LibraryFolders("C:/inbox", "C:/library", "C:/work"),
            TestContext.Current.CancellationToken);

        Assert.False(result.Completed);
        Assert.Equal(2, store.CompletedCleanupEntryIds.Count);
        Assert.Equal("ingest.cleanup_source_changed", result.ErrorCode);
    }

    private static IngestOperationRecord Operation() =>
        new(
            Guid.NewGuid(), Guid.NewGuid(), IngestOperationStage.DatabaseCommitted,
            IngestDisposition.Create, null,
            "C:/work/output.mp4", 3, DateTimeOffset.UtcNow, new string('A', 64),
            "output.mp4", null, Guid.NewGuid(), null, null, null, null, null, null,
            IngestBackupState.NotRequired, null, null, null, null, null, 0, null);

    private static IngestCleanupEntryRecord Entry(
        Guid operationId,
        IngestCleanupKind kind,
        string path) =>
        new(
            Guid.NewGuid(),
            operationId,
            new IngestCleanupFile(kind, path, 3, new string('A', 64)),
            false);

    private sealed class CleanupStore(IReadOnlyList<IngestCleanupEntryRecord> entries)
        : IIngestStore, IIngestCleanupWorkStore
    {
        public List<Guid> CompletedCleanupEntryIds { get; } = [];

        public int? ScheduledFailureCount { get; private set; }

        public DateTimeOffset? ScheduledAtUtc { get; private set; }

        public string? ScheduledErrorCode { get; private set; }

        public Task<IReadOnlyList<IngestCleanupEntryRecord>> LoadCleanupEntriesAsync(Guid operationId, CancellationToken cancellationToken) => Task.FromResult(entries);
        public Task MarkCleanupEntryCompletedAsync(Guid operationId, Guid entryId, CancellationToken cancellationToken)
        {
            CompletedCleanupEntryIds.Add(entryId);
            return Task.CompletedTask;
        }
        public Task MarkCleanupPendingAsync(Guid operationId, string errorCode, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CompleteAsync(Guid operationId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ScheduleCleanupRetryAsync(Guid operationId, string errorCode, int failureCount, DateTimeOffset? nextAttemptAtUtc, CancellationToken cancellationToken)
        {
            ScheduledErrorCode = errorCode;
            ScheduledFailureCount = failureCount;
            ScheduledAtUtc = nextAttemptAtUtc;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<IngestOperationRecord>> LoadDueCleanupAsync(DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DateTimeOffset?> FindNextCleanupDueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ResetCleanupRetryAsync(Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IngestCandidate?> LoadCandidateAsync(Guid inboxItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IngestOperationRecord?> FindActiveAsync(Guid inboxItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IngestOperationRecord> CreateAsync(IngestPreparation preparation, IngestExecutionPlan plan, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkStagedAsync(Guid operationId, StagedFile staged, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AbandonForRetryAsync(Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkFileCommittedAsync(Guid operationId, CommittedFile committed, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> CommitAssetAsync(Guid operationId, CommittedFile committed, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FailAsync(Guid operationId, string errorCode, bool isRetryable, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<IngestOperationRecord>> LoadIncompleteAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CleanupStorage(params SourceDeleteOutcome[] outcomes)
        : IControlledMediaStorage
    {
        private readonly Queue<SourceDeleteOutcome> outcomes = new(outcomes);

        public Task<SourceDeleteResult> DeleteCleanupFileIfMatchesAsync(LibraryFolders folders, IngestCleanupFile cleanupFile, CancellationToken cancellationToken) =>
            Task.FromResult(new SourceDeleteResult(outcomes.Dequeue()));
        public Task CleanupTemporaryFilesAsync(Guid operationId, LibraryFolders folders, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> TargetExistsAsync(LibraryFolders folders, string targetFilename, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StagedFile> StageAsync(Guid operationId, LibraryFolders folders, string sourcePath, string expectedSha256, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CommittedFile> CommitAsync(Guid operationId, LibraryFolders folders, StagedFile staged, string targetFilename, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SourceDeleteResult> DeleteSourceIfMatchesAsync(LibraryFolders folders, string sourcePath, long expectedSize, string expectedSha256, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> VerifyCommittedAsync(LibraryFolders folders, CommittedFile committed, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingCleanupStorage(Exception error)
        : IControlledMediaStorage
    {
        public Task<SourceDeleteResult> DeleteCleanupFileIfMatchesAsync(
            LibraryFolders folders,
            IngestCleanupFile cleanupFile,
            CancellationToken cancellationToken) =>
            Task.FromException<SourceDeleteResult>(error);

        public Task<bool> TargetExistsAsync(LibraryFolders folders, string targetFilename, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StagedFile> StageAsync(Guid operationId, LibraryFolders folders, string sourcePath, string expectedSha256, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CommittedFile> CommitAsync(Guid operationId, LibraryFolders folders, StagedFile staged, string targetFilename, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SourceDeleteResult> DeleteSourceIfMatchesAsync(LibraryFolders folders, string sourcePath, long expectedSize, string expectedSha256, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> VerifyCommittedAsync(LibraryFolders folders, CommittedFile committed, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CleanupTemporaryFilesAsync(Guid operationId, LibraryFolders folders, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CleanupSignal : IIngestCleanupSignal
    {
        public int Count { get; private set; }

        public void Signal() => Count++;

        public Task WaitAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
