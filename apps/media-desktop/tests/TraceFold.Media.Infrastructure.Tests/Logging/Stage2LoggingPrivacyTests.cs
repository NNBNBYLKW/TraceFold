using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Logging;

public sealed class Stage2LoggingPrivacyTests
{
    [Fact]
    public async Task Failed_ingest_logs_operation_and_code_without_metadata_values()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"tracefold-log-privacy-{Guid.NewGuid():N}");
        string inbox = Directory.CreateDirectory(Path.Combine(root, "inbox")).FullName;
        string library = Directory.CreateDirectory(Path.Combine(root, "library")).FullName;
        string work = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
        string source = Path.Combine(inbox, "private-photo.jpg");
        const string secretDisplay = "SECRET-DISPLAY-NAME";
        const string secretAuthor = "SECRET-AUTHOR-NAME";
        const string secretSeries = "SECRET-SERIES-NAME";
        const string secretTag = "SECRET-TAG-NAME";
        await File.WriteAllBytesAsync(
            source,
            [1, 2, 3],
            TestContext.Current.CancellationToken);

        try
        {
            var info = new FileInfo(source);
            Guid itemId = Guid.NewGuid();
            Guid operationId = Guid.NewGuid();
            var candidate = new IngestCandidate(
                itemId,
                InboxItemState.ReadyToTag,
                MediaKind.Image,
                info.Name,
                null,
                [
                    new IngestSourceRecord(
                        source,
                        info.Length,
                        new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                        IsPresent: true,
                        IsPrimary: true)
                ],
                MetadataSelection.Create(secretDisplay, [], null, []),
                MetadataVersion: 0);
            var logger = new CapturingLogger<OrdinaryIngestService>();
            var service = new OrdinaryIngestService(
                new CompletedScanner(),
                new FailingIngestStore(candidate, operationId),
                new FixedSettingsStore(new LibraryFolders(inbox, library, work)),
                new FixedHasher(),
                new StageFailingStorage(),
                new MediaOperationCoordinator(),
                logger);

            IngestResult result = await service.IngestAsync(
                itemId,
                TestContext.Current.CancellationToken);

            string captured = string.Join(Environment.NewLine, logger.Messages);
            Assert.Equal(IngestOutcome.Failed, result.Outcome);
            Assert.Contains(operationId.ToString(), captured, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ingest.stage_failed", captured, StringComparison.Ordinal);
            Assert.DoesNotContain(secretDisplay, captured, StringComparison.Ordinal);
            Assert.DoesNotContain(secretAuthor, captured, StringComparison.Ordinal);
            Assert.DoesNotContain(secretSeries, captured, StringComparison.Ordinal);
            Assert.DoesNotContain(secretTag, captured, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FailingIngestStore(
        IngestCandidate candidate,
        Guid operationId) : IIngestStore
    {
        public Task<IngestCandidate?> LoadCandidateAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IngestCandidate?>(candidate);

        public Task<IngestOperationRecord?> FindActiveAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IngestOperationRecord?>(null);

        public Task<ConflictCheck> CheckConflictsAsync(
            string targetFilename,
            string sourceSha256,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ConflictCheck(null));

        public Task BlockConflictAsync(
            Guid inboxItemId,
            ConflictKind kind,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IngestOperationRecord> CreateAsync(
            IngestPreparation preparation,
            CancellationToken cancellationToken) =>
            Task.FromResult(new IngestOperationRecord(
                operationId,
                preparation.InboxItemId,
                IngestOperationStage.Created,
                preparation.SourcePath,
                preparation.SourceSize,
                preparation.SourceLastWriteTimeUtc,
                preparation.SourceSha256,
                preparation.TargetFilename,
                null,
                null,
                null));

        public Task FailAsync(
            Guid id,
            string errorCode,
            bool isRetryable,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkStagedAsync(
            Guid id,
            StagedFile staged,
            CancellationToken cancellationToken) => throw Unused();

        public Task AbandonForRetryAsync(
            Guid id,
            CancellationToken cancellationToken) => throw Unused();

        public Task MarkFileCommittedAsync(
            Guid id,
            CommittedFile committed,
            CancellationToken cancellationToken) => throw Unused();

        public Task<Guid> CommitAssetAsync(
            Guid id,
            CommittedFile committed,
            CancellationToken cancellationToken) => throw Unused();

        public Task MarkCleanupPendingAsync(
            Guid id,
            string errorCode,
            CancellationToken cancellationToken) => throw Unused();

        public Task CompleteAsync(
            Guid id,
            CancellationToken cancellationToken) => throw Unused();

        public Task<IReadOnlyList<IngestOperationRecord>> LoadIncompleteAsync(
            CancellationToken cancellationToken) => throw Unused();

        private static NotSupportedException Unused() => new();
    }

    private sealed class StageFailingStorage : IControlledMediaStorage
    {
        public Task<bool> TargetExistsAsync(
            LibraryFolders folders,
            string targetFilename,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<StagedFile> StageAsync(
            Guid operationId,
            LibraryFolders folders,
            string sourcePath,
            string expectedSha256,
            CancellationToken cancellationToken) =>
            Task.FromException<StagedFile>(
                new IOException("controlled stage failure"));

        public Task<CommittedFile> CommitAsync(
            Guid id,
            LibraryFolders folders,
            StagedFile staged,
            string targetFilename,
            CancellationToken cancellationToken) => throw Unused();

        public Task<SourceDeleteResult> DeleteSourceIfMatchesAsync(
            LibraryFolders folders,
            string sourcePath,
            long expectedSize,
            string expectedSha256,
            CancellationToken cancellationToken) => throw Unused();

        public Task<bool> VerifyCommittedAsync(
            LibraryFolders folders,
            CommittedFile committed,
            CancellationToken cancellationToken) => throw Unused();

        public Task CleanupTemporaryFilesAsync(
            Guid id,
            LibraryFolders folders,
            CancellationToken cancellationToken) => throw Unused();

        private static NotSupportedException Unused() => new();
    }

    private sealed class CompletedScanner : IInboxScanner
    {
        public Task<ScanResult> ScanAsync(
            ScanTrigger trigger,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ScanResult(
                ScanOutcome.Completed,
                1,
                0,
                1,
                DateTimeOffset.UtcNow,
                "ok"));
    }

    private sealed class FixedSettingsStore(LibraryFolders folders)
        : IDirectorySettingsStore
    {
        public Task<LibraryFolders?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<LibraryFolders?>(folders);

        public Task SaveAsync(
            LibraryFolders value,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedHasher : IFileHasher
    {
        public Task<string> ComputeSha256Async(
            string path,
            CancellationToken cancellationToken) =>
            Task.FromResult(new string('A', 64));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
