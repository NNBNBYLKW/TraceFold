using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;
using Xunit;

namespace TraceFold.Media.Application.Tests.Ingest;

public sealed class BatchIngestServiceTests
{
    [Fact]
    public async Task Preview_reclassifies_current_facts_without_calling_ingest()
    {
        Guid ready = Guid.NewGuid();
        Guid processing = Guid.NewGuid();
        Guid conflict = Guid.NewGuid();
        var ingest = new RecordingIngestService();
        var service = new BatchIngestService(
            new FakeInboxSelectionStore(
                Ready(ready, 0),
                RequiresProcessing(processing, 1),
                Conflict(conflict, 2)),
            ingest);

        BatchIngestPreviewDto preview = await service.PreviewAsync(
            Select(ready, processing, conflict),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, preview.ReadyCount);
        Assert.Equal(1, preview.RequiresProcessingCount);
        Assert.Equal(1, preview.ConflictCount);
        Assert.Empty(ingest.Calls);
    }

    [Fact]
    public async Task Execute_is_serial_continues_after_item_results_and_never_resolves_conflicts()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        Guid third = Guid.NewGuid();
        var ingest = new RecordingIngestService(
            (first, Result(IngestOutcome.Completed)),
            (second, Result(IngestOutcome.ConflictBlocked)),
            (third, Result(IngestOutcome.Completed)));
        var service = new BatchIngestService(
            CurrentReadyStore(first, second, third), ingest);

        BatchIngestResult result = await service.ExecuteAsync(
            Select(first, second, third),
            progress: null,
            stopAfterCurrent: CancellationToken.None,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([first, second, third], ingest.Calls);
        Assert.Equal(1, ingest.MaxConcurrentCalls);
        Assert.Equal(2, result.CompletedCount);
        Assert.Equal(1, result.ConflictCount);
    }

    [Fact]
    public async Task Execute_rejects_a_stale_version_before_calling_single_item_ingest()
    {
        Guid id = Guid.NewGuid();
        var ingest = new RecordingIngestService();
        FakeInboxSelectionStore store = CurrentReadyStore(id, currentVersion: 4);
        var service = new BatchIngestService(store, ingest);

        BatchIngestResult result = await service.ExecuteAsync(
            [new InboxSelectionEntry(id, 3, 0)],
            null,
            CancellationToken.None,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            BatchIngestItemOutcome.ChangedOrInvalid,
            Assert.Single(result.Items).Outcome);
        Assert.Empty(ingest.Calls);
    }

    [Fact]
    public async Task Stop_requested_during_first_item_marks_later_items_not_started()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        using var stop = new CancellationTokenSource();
        var ingest = new RecordingIngestService(async id =>
        {
            stop.Cancel();
            await Task.Yield();
            return Result(IngestOutcome.Completed);
        });
        var service = new BatchIngestService(
            CurrentReadyStore(first, second), ingest);

        BatchIngestResult result = await service.ExecuteAsync(
            Select(first, second),
            null,
            stop.Token,
            TestContext.Current.CancellationToken);

        Assert.Equal([first], ingest.Calls);
        Assert.True(result.StoppedByUser);
        Assert.Equal(BatchIngestItemOutcome.NotStarted, result.Items[1].Outcome);
    }

    [Fact]
    public async Task Returned_failure_does_not_stop_a_later_item()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        var ingest = new RecordingIngestService(
            (first, Result(IngestOutcome.Failed)),
            (second, Result(IngestOutcome.Completed)));
        var service = new BatchIngestService(
            CurrentReadyStore(first, second), ingest);

        BatchIngestResult result = await service.ExecuteAsync(
            Select(first, second), null, CancellationToken.None,
            TestContext.Current.CancellationToken);

        Assert.Equal([first, second], ingest.Calls);
        Assert.Equal(BatchIngestItemOutcome.Failed, result.Items[0].Outcome);
        Assert.Equal(BatchIngestItemOutcome.Completed, result.Items[1].Outcome);
    }

    [Fact]
    public async Task Thrown_orchestration_failure_marks_the_remainder_not_started()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        var ingest = new RecordingIngestService(id =>
            throw new InvalidOperationException("controlled failure"));
        var service = new BatchIngestService(
            CurrentReadyStore(first, second), ingest);

        BatchIngestResult result = await service.ExecuteAsync(
            Select(first, second), null, CancellationToken.None,
            TestContext.Current.CancellationToken);

        Assert.Equal([first], ingest.Calls);
        Assert.Equal(BatchIngestItemOutcome.Failed, result.Items[0].Outcome);
        Assert.Equal("batch.ingest.orchestration_failed", result.Items[0].ErrorCode);
        Assert.Equal(BatchIngestItemOutcome.NotStarted, result.Items[1].Outcome);
    }

    private static FakeInboxSelectionStore CurrentReadyStore(
        Guid id,
        long currentVersion) =>
        new(new InboxSelectionItemDto(
            id,
            currentVersion,
            0,
            MediaKind.Image,
            InboxItemState.ReadyToTag,
            InboxAction.Ingest | InboxAction.EditMetadata,
            "item-0"));

    private static FakeInboxSelectionStore CurrentReadyStore(params Guid[] ids) =>
        new(ids.Select((id, ordinal) => Ready(id, ordinal)).ToArray());

    private static InboxSelectionItemDto Ready(Guid id, int ordinal) =>
        Item(
            id,
            ordinal,
            InboxItemState.ReadyToTag,
            InboxAction.Ingest | InboxAction.EditMetadata);

    private static InboxSelectionItemDto RequiresProcessing(Guid id, int ordinal) =>
        Item(
            id,
            ordinal,
            InboxItemState.RequiresProcessing,
            InboxAction.StartProcessing | InboxAction.EditMetadata);

    private static InboxSelectionItemDto Conflict(Guid id, int ordinal) =>
        Item(
            id,
            ordinal,
            InboxItemState.ConflictBlocked,
            InboxAction.ResolveConflict | InboxAction.EditMetadata);

    private static InboxSelectionItemDto Item(
        Guid id,
        int ordinal,
        InboxItemState state,
        InboxAction actions) =>
        new(id, 0, ordinal, MediaKind.Video, state, actions, $"item-{ordinal}");

    private static IReadOnlyList<InboxSelectionEntry> Select(params Guid[] ids) =>
        ids.Select((id, ordinal) =>
            new InboxSelectionEntry(id, 0, ordinal)).ToArray();

    private static IngestResult Result(IngestOutcome outcome) =>
        new(outcome, null, null, null, outcome.ToString());

    private sealed class FakeInboxSelectionStore(
        params InboxSelectionItemDto[] items) : IInboxSelectionStore
    {
        public Task<IReadOnlyList<InboxSelectionItemDto>> LoadAsync(
            IReadOnlyList<InboxSelectionEntry> selection,
            CancellationToken cancellationToken)
        {
            HashSet<Guid> requested = selection
                .Select(entry => entry.InboxItemId)
                .ToHashSet();
            return Task.FromResult<IReadOnlyList<InboxSelectionItemDto>>(
                items.Where(item => requested.Contains(item.InboxItemId))
                    .OrderBy(item => item.Ordinal)
                    .ToArray());
        }
    }

    private sealed class RecordingIngestService : IIngestService
    {
        private readonly Func<Guid, Task<IngestResult>> handler;
        private int activeCalls;

        public RecordingIngestService(params (Guid Id, IngestResult Result)[] results)
            : this(id => Task.FromResult(
                results.Single(item => item.Id == id).Result))
        {
        }

        public RecordingIngestService(Func<Guid, Task<IngestResult>> handler)
        {
            this.handler = handler;
        }

        public List<Guid> Calls { get; } = [];

        public int MaxConcurrentCalls { get; private set; }

        public async Task<IngestResult> IngestAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken)
        {
            Calls.Add(inboxItemId);
            int active = Interlocked.Increment(ref activeCalls);
            MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, active);
            try
            {
                return await handler(inboxItemId);
            }
            finally
            {
                Interlocked.Decrement(ref activeCalls);
            }
        }
    }
}
