using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Domain.Inbox;
using Xunit;

namespace TraceFold.Media.Application.Tests.Processing;

public sealed class BatchProcessingServiceTests
{
    [Fact]
    public async Task Start_calls_the_single_item_command_only_for_actionable_items_in_order()
    {
        Guid image = Guid.NewGuid();
        Guid paired = Guid.NewGuid();
        Guid active = Guid.NewGuid();
        var facts = new FakeInboxSelectionStore(
            Ready(image, ordinal: 0),
            RequiresProcessing(paired, ordinal: 1),
            ProcessingQueued(active, ordinal: 2));
        var commands = new RecordingProcessingCommandService(
            new ProcessingCommandResult(
                ProcessingCommandOutcome.Queued, Guid.NewGuid(), "queued"));
        var service = new BatchProcessingService(facts, commands);

        BatchProcessingResult result = await service.StartAsync(
            Select(image, paired, active), TestContext.Current.CancellationToken);

        Assert.Equal([paired], commands.StartedIds);
        Assert.Equal(BatchProcessingItemOutcome.NotRequired, result.Items[0].Outcome);
        Assert.Equal(BatchProcessingItemOutcome.Queued, result.Items[1].Outcome);
        Assert.Equal(BatchProcessingItemOutcome.AlreadyActive, result.Items[2].Outcome);
    }

    [Theory]
    [InlineData(ProcessingCommandOutcome.SourceMissing)]
    [InlineData(ProcessingCommandOutcome.SourceChanged)]
    [InlineData(ProcessingCommandOutcome.ToolUnavailable)]
    [InlineData(ProcessingCommandOutcome.Failed)]
    [InlineData(ProcessingCommandOutcome.NotEligible)]
    public async Task Unsafe_start_results_are_reported_as_needs_attention(
        ProcessingCommandOutcome outcome)
    {
        Guid id = Guid.NewGuid();
        BatchProcessingService service = ServiceFor(id, outcome);

        BatchProcessingResult result = await service.StartAsync(
            Select(id), TestContext.Current.CancellationToken);

        Assert.Equal(
            BatchProcessingItemOutcome.NeedsAttention,
            Assert.Single(result.Items).Outcome);
    }

    [Fact]
    public async Task Shutdown_result_marks_current_and_remaining_items_not_started()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        var commands = new RecordingProcessingCommandService(
            new ProcessingCommandResult(
                ProcessingCommandOutcome.ShuttingDown, null, "closing"));
        var service = new BatchProcessingService(
            new FakeInboxSelectionStore(
                RequiresProcessing(first, 0),
                RequiresProcessing(second, 1)),
            commands);

        BatchProcessingResult result = await service.StartAsync(
            Select(first, second), TestContext.Current.CancellationToken);

        Assert.Equal([first], commands.StartedIds);
        Assert.All(result.Items, item =>
            Assert.Equal(BatchProcessingItemOutcome.NotStarted, item.Outcome));
    }

    [Fact]
    public async Task Missing_current_item_is_reported_without_a_command_call()
    {
        Guid missing = Guid.NewGuid();
        var commands = new RecordingProcessingCommandService(
            new ProcessingCommandResult(
                ProcessingCommandOutcome.Queued, Guid.NewGuid(), "queued"));
        var service = new BatchProcessingService(
            new FakeInboxSelectionStore(), commands);

        BatchProcessingResult result = await service.StartAsync(
            Select(missing), TestContext.Current.CancellationToken);

        Assert.Equal(BatchProcessingItemOutcome.NeedsAttention,
            Assert.Single(result.Items).Outcome);
        Assert.Empty(commands.StartedIds);
    }

    [Fact]
    public async Task Unexpected_command_failure_preserves_results_stops_and_finishes_safely()
    {
        const string privateMessage = "PRIVATE command failure detail";
        Guid first = Guid.NewGuid();
        Guid failing = Guid.NewGuid();
        Guid later = Guid.NewGuid();
        var commands = new ThrowingSecondProcessingCommandService(privateMessage);
        var logger = new RecordingLogger<BatchProcessingService>();
        var service = new BatchProcessingService(
            new FakeInboxSelectionStore(
                RequiresProcessing(first, 0),
                RequiresProcessing(failing, 1),
                RequiresProcessing(later, 2)),
            commands,
            logger);

        BatchProcessingResult result = await service.StartAsync(
            Select(first, failing, later),
            TestContext.Current.CancellationToken);

        Assert.Equal([first, failing], commands.StartedIds);
        Assert.Collection(
            result.Items,
            item =>
            {
                Assert.Equal(BatchProcessingItemOutcome.Queued, item.Outcome);
                Assert.Null(item.ErrorCode);
            },
            item =>
            {
                Assert.Equal(BatchProcessingItemOutcome.NeedsAttention, item.Outcome);
                Assert.Equal("batch.processing.orchestration_failed", item.ErrorCode);
            },
            item =>
            {
                Assert.Equal(BatchProcessingItemOutcome.NotStarted, item.Outcome);
                Assert.Equal("batch.processing.not_started", item.ErrorCode);
            });
        string captured = string.Join(Environment.NewLine, logger.Messages);
        Assert.Contains("Batch processing finished", captured, StringComparison.Ordinal);
        Assert.Contains("NeedsAttentionCount 1", captured, StringComparison.Ordinal);
        Assert.Contains("NotStartedCount 1", captured, StringComparison.Ordinal);
        Assert.DoesNotContain(privateMessage, captured, StringComparison.Ordinal);
        Assert.DoesNotContain(logger.Exceptions, exception => exception is not null);
    }

    private static BatchProcessingService ServiceFor(
        Guid id,
        ProcessingCommandOutcome outcome) =>
        new(
            new FakeInboxSelectionStore(RequiresProcessing(id, 0)),
            new RecordingProcessingCommandService(
                new ProcessingCommandResult(outcome, null, outcome.ToString())));

    private static InboxSelectionItemDto Ready(Guid id, int ordinal) =>
        Item(
            id,
            ordinal,
            MediaKind.Image,
            InboxItemState.ReadyToTag,
            InboxAction.Preview | InboxAction.EditMetadata | InboxAction.Ingest);

    private static InboxSelectionItemDto RequiresProcessing(Guid id, int ordinal) =>
        Item(
            id,
            ordinal,
            MediaKind.Video,
            InboxItemState.RequiresProcessing,
            InboxAction.Preview | InboxAction.EditMetadata
                | InboxAction.StartProcessing);

    private static InboxSelectionItemDto ProcessingQueued(Guid id, int ordinal) =>
        Item(
            id,
            ordinal,
            MediaKind.Video,
            InboxItemState.ProcessingQueued,
            InboxAction.Preview | InboxAction.EditMetadata
                | InboxAction.CancelProcessing);

    private static InboxSelectionItemDto Item(
        Guid id,
        int ordinal,
        MediaKind mediaKind,
        InboxItemState state,
        InboxAction actions) =>
        new(id, 0, ordinal, mediaKind, state, actions, $"item-{ordinal}");

    private static IReadOnlyList<InboxSelectionEntry> Select(params Guid[] ids) =>
        ids.Select((id, ordinal) =>
            new InboxSelectionEntry(id, 0, ordinal)).ToArray();

    private sealed class FakeInboxSelectionStore(
        params InboxSelectionItemDto[] items) : IInboxSelectionStore
    {
        public Task<IReadOnlyList<InboxSelectionItemDto>> LoadAsync(
            IReadOnlyList<InboxSelectionEntry> selection,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InboxSelectionItemDto>>(items);
    }

    private sealed class RecordingProcessingCommandService(
        ProcessingCommandResult result) : IProcessingCommandService
    {
        public List<Guid> StartedIds { get; } = [];

        public Task<ProcessingCommandResult> StartAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken)
        {
            StartedIds.Add(inboxItemId);
            return Task.FromResult(result);
        }

        public Task<ProcessingCommandResult> CancelQueuedAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProcessingCommandResult> StopAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProcessingCommandResult> RetryAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingSecondProcessingCommandService(
        string privateMessage) : IProcessingCommandService
    {
        public List<Guid> StartedIds { get; } = [];

        public Task<ProcessingCommandResult> StartAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken)
        {
            StartedIds.Add(inboxItemId);
            if (StartedIds.Count == 2)
            {
                throw new InvalidOperationException(privateMessage);
            }

            return Task.FromResult(new ProcessingCommandResult(
                ProcessingCommandOutcome.Queued,
                Guid.NewGuid(),
                "queued"));
        }

        public Task<ProcessingCommandResult> CancelQueuedAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProcessingCommandResult> StopAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProcessingCommandResult> RetryAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public List<Exception?> Exceptions { get; } = [];

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
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }
}
