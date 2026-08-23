using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Ingest;
using Xunit;

namespace TraceFold.Media.Application.Tests.Ingest;

public sealed class IngestCleanupServiceTests
{
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
            "C:/work/output.mp4", 3, DateTimeOffset.UtcNow, new string('A', 64),
            "output.mp4", null, Guid.NewGuid(), null);

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
        : IIngestStore
    {
        public List<Guid> CompletedCleanupEntryIds { get; } = [];

        public Task<IReadOnlyList<IngestCleanupEntryRecord>> LoadCleanupEntriesAsync(Guid operationId, CancellationToken cancellationToken) => Task.FromResult(entries);
        public Task MarkCleanupEntryCompletedAsync(Guid operationId, Guid entryId, CancellationToken cancellationToken)
        {
            CompletedCleanupEntryIds.Add(entryId);
            return Task.CompletedTask;
        }
        public Task MarkCleanupPendingAsync(Guid operationId, string errorCode, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CompleteAsync(Guid operationId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IngestCandidate?> LoadCandidateAsync(Guid inboxItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IngestOperationRecord?> FindActiveAsync(Guid inboxItemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ConflictCheck> CheckConflictsAsync(string targetFilename, string sourceSha256, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task BlockConflictAsync(Guid inboxItemId, ConflictKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IngestOperationRecord> CreateAsync(IngestPreparation preparation, CancellationToken cancellationToken) => throw new NotSupportedException();
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
}
