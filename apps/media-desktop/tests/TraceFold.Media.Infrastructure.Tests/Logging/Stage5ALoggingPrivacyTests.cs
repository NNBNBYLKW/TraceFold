using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Logging;

public sealed class Stage5ALoggingPrivacyTests
{
    [Fact]
    public async Task Batch_logs_keep_counts_and_codes_without_private_values()
    {
        const string path = @"C:\PRIVATE\video.mp4";
        const string display = "秘密显示名";
        const string author = "秘密作者";
        const string tag = "秘密标签";
        string hash = new('A', 64);
        Guid metadataFirst = Guid.NewGuid();
        Guid metadataSecond = Guid.NewGuid();
        Guid processingId = Guid.NewGuid();
        Guid ingestId = Guid.NewGuid();
        var logs = new CapturingLoggerProvider();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            builder.AddProvider(logs));
        var notifier = new RecordingNotifier();

        var metadataService = new BatchMetadataService(
            new FailingBatchMetadataStore(),
            notifier,
            loggerFactory.CreateLogger<BatchMetadataService>());
        BatchMetadataCommand metadataCommand = BatchMetadataCommand.Create(
            Selection(metadataFirst, metadataSecond),
            BatchMetadataMode.Add,
            new BatchMetadataSetEdit(
                BatchMetadataFieldOperation.Add,
                [BatchTaxonomyReference.New(TaxonomyKind.Author, author)]),
            BatchMetadataSeriesEdit.Unchanged,
            new BatchMetadataSetEdit(
                BatchMetadataFieldOperation.Add,
                [BatchTaxonomyReference.New(TaxonomyKind.Tag, tag)]));
        await metadataService.ApplyAsync(
            metadataCommand,
            TestContext.Current.CancellationToken);

        var selectionStore = new FixedSelectionStore(
            Item(processingId, InboxItemState.RequiresProcessing,
                InboxAction.StartProcessing, path),
            Item(ingestId, InboxItemState.ReadyToTag,
                InboxAction.Ingest, display));
        var processing = new BatchProcessingService(
            selectionStore,
            new FailingProcessingCommands($"{display}|{author}|{hash}"),
            loggerFactory.CreateLogger<BatchProcessingService>());
        await processing.StartAsync(
            Selection(processingId),
            TestContext.Current.CancellationToken);

        var ingest = new BatchIngestService(
            selectionStore,
            new FailingIngestService($"{path}|{display}|{tag}|{hash}"),
            loggerFactory.CreateLogger<BatchIngestService>());
        await ingest.ExecuteAsync(
            Selection(ingestId),
            progress: null,
            stopAfterCurrent: CancellationToken.None,
            cancellationToken: TestContext.Current.CancellationToken);

        string captured = string.Join(Environment.NewLine, logs.Messages);
        Assert.Contains("batch.metadata.version_conflict", captured,
            StringComparison.Ordinal);
        Assert.Contains("batch.processing.needs_attention", captured,
            StringComparison.Ordinal);
        Assert.Contains("batch.ingest.failed", captured,
            StringComparison.Ordinal);
        Assert.Contains("SelectedCount", captured, StringComparison.Ordinal);
        foreach (string secret in new[] { path, display, author, tag, hash })
        {
            Assert.DoesNotContain(secret, captured, StringComparison.Ordinal);
        }
    }

    private static IReadOnlyList<InboxSelectionEntry> Selection(
        params Guid[] ids) => ids.Select((id, ordinal) =>
            new InboxSelectionEntry(id, 0, ordinal)).ToArray();

    private static InboxSelectionItemDto Item(
        Guid id,
        InboxItemState state,
        InboxAction actions,
        string safeName) =>
        new(id, 0, 0, MediaKind.Video, state, actions, safeName);

    private sealed class FailingBatchMetadataStore : IBatchMetadataStore
    {
        public Task<BatchMetadataEditorDto?> PrepareAsync(
            IReadOnlyList<InboxSelectionEntry> selection,
            CancellationToken cancellationToken) =>
            Task.FromResult<BatchMetadataEditorDto?>(null);

        public Task<BatchMetadataResult> ApplyAsync(
            BatchMetadataWrite write,
            CancellationToken cancellationToken) =>
            Task.FromResult(new BatchMetadataResult(
                false,
                0,
                0,
                write.Selection.Count,
                "batch.metadata.version_conflict"));
    }

    private sealed class FixedSelectionStore(
        params InboxSelectionItemDto[] items) : IInboxSelectionStore
    {
        public Task<IReadOnlyList<InboxSelectionItemDto>> LoadAsync(
            IReadOnlyList<InboxSelectionEntry> selection,
            CancellationToken cancellationToken)
        {
            HashSet<Guid> ids = selection
                .Select(entry => entry.InboxItemId)
                .ToHashSet();
            return Task.FromResult<IReadOnlyList<InboxSelectionItemDto>>(
                items.Where(item => ids.Contains(item.InboxItemId)).ToArray());
        }
    }

    private sealed class FailingProcessingCommands(string privateMessage)
        : IProcessingCommandService
    {
        public Task<ProcessingCommandResult> StartAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessingCommandResult(
                ProcessingCommandOutcome.Failed,
                null,
                privateMessage));

        public Task<ProcessingCommandResult> CancelQueuedAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ProcessingCommandResult> StopAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ProcessingCommandResult> RetryAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FailingIngestService(string privateMessage)
        : IIngestService
    {
        public Task<IngestResult> IngestAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new IngestResult(
                IngestOutcome.Failed,
                null,
                null,
                null,
                privateMessage));
    }

    private sealed class RecordingNotifier : IInboxActivityNotifier
    {
        public event EventHandler<InboxActivityEventArgs>? Changed;

        public void Publish(InboxActivityKind kind) =>
            Changed?.Invoke(this, new InboxActivityEventArgs(kind));

        public Task WaitForCandidateAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(Messages);

        public void Dispose()
        {
        }

        private sealed class Logger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                messages.Add(formatter(state, exception));
        }
    }
}
