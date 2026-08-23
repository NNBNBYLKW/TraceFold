using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Processing;
using TraceFold.Media.Desktop.Pages;
using TraceFold.Media.Desktop.Services;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

public sealed class InboxViewModelTests
{
    [Fact]
    public async Task Search_reload_and_selection_flushes_previous_metadata()
    {
        var query = new RecordingInboxQuery();
        var metadataGateway = new RecordingMetadataGateway();
        var metadata = new MetadataEditorViewModel(
            metadataGateway,
            new BlockingDebounceScheduler());
        InboxViewModel viewModel = CreateViewModel(query, metadata);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        viewModel.SearchText = "杭州";
        await viewModel.ApplyFiltersAsync(TestContext.Current.CancellationToken);
        await viewModel.SelectAsync(
            viewModel.Items[0],
            TestContext.Current.CancellationToken);
        metadata.DisplayName = "unsaved";
        await viewModel.SelectAsync(
            viewModel.Items[1],
            TestContext.Current.CancellationToken);

        Assert.Equal("杭州", query.LastQuery!.Search);
        Assert.Equal(query.Cards[0].Id, Assert.Single(metadataGateway.SavedOwners).Id);
        Assert.Equal(query.Cards[1].Id, metadata.Owner!.Id);
    }

    [Fact]
    public async Task Ingest_command_flushes_metadata_before_ingest_and_clears_selection()
    {
        var query = new RecordingInboxQuery();
        var metadataGateway = new RecordingMetadataGateway();
        var metadata = new MetadataEditorViewModel(
            metadataGateway,
            new BlockingDebounceScheduler());
        var ingest = new RecordingIngestService(
            () => metadataGateway.SaveCount > 0);
        InboxViewModel viewModel = CreateViewModel(query, metadata, ingest);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        await viewModel.SelectAsync(
            viewModel.Items[0],
            TestContext.Current.CancellationToken);
        metadata.DisplayName = "final";

        await viewModel.IngestCommand.ExecuteAsync(null);

        Assert.True(ingest.ObservedFlushedMetadata);
        Assert.Null(viewModel.SelectedItem);
        Assert.Equal("入库完成。", viewModel.OperationMessage);
    }

    [Fact]
    public async Task Required_item_exposes_start_and_never_calls_ingest()
    {
        var query = new SingleItemInboxQuery(
            InboxItemState.RequiresProcessing,
            InboxAction.Preview | InboxAction.EditMetadata
                | InboxAction.OpenFolder | InboxAction.StartProcessing);
        var ingest = new RecordingIngestService(() => true);
        var processing = new RecordingProcessingCommandService();
        InboxViewModel viewModel = CreateViewModel(
            query,
            CreateMetadata(),
            ingest,
            processing);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        await viewModel.SelectAsync(
            Assert.Single(viewModel.Items),
            TestContext.Current.CancellationToken);

        Assert.True(viewModel.StartProcessingCommand.CanExecute(null));
        Assert.False(viewModel.IngestCommand.CanExecute(null));
        await viewModel.StartProcessingCommand.ExecuteAsync(null);

        Assert.Equal(1, processing.StartCalls);
        Assert.Equal(0, ingest.CallCount);
    }

    [Fact]
    public async Task Running_item_exposes_stop_and_card_progress()
    {
        var processing = new ProcessingSummaryDto(
            Guid.NewGuid(),
            ProcessingJobStatus.Running,
            "合并视频和音频",
            "合并音视频",
            63,
            null,
            false);
        var query = new SingleItemInboxQuery(
            InboxItemState.Processing,
            InboxAction.Preview | InboxAction.EditMetadata
                | InboxAction.OpenFolder | InboxAction.StopProcessing,
            processing);
        InboxViewModel viewModel = CreateViewModel(
            query,
            CreateMetadata(),
            processingCommands: new RecordingProcessingCommandService());
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        InboxCardViewModel card = Assert.Single(viewModel.Items);
        await viewModel.SelectAsync(card, TestContext.Current.CancellationToken);

        Assert.Equal("合并音视频 63%", card.ProgressText);
        Assert.True(viewModel.StopProcessingCommand.CanExecute(null));
    }

    [Fact]
    public async Task Missing_processing_tool_disables_start_but_keeps_refresh_available()
    {
        var query = new SingleItemInboxQuery(
            InboxItemState.RequiresProcessing,
            InboxAction.Preview | InboxAction.EditMetadata
                | InboxAction.OpenFolder | InboxAction.StartProcessing);
        InboxViewModel viewModel = CreateViewModel(
            query,
            CreateMetadata(),
            processingCommands: new RecordingProcessingCommandService(),
            diagnostics: new FixedProcessingDiagnostics(available: false));
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        await viewModel.SelectAsync(
            Assert.Single(viewModel.Items),
            TestContext.Current.CancellationToken);

        Assert.False(viewModel.StartProcessingCommand.CanExecute(null));
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task Reloading_selected_item_refreshes_card_and_inspector_details()
    {
        var query = new SingleItemInboxQuery(
            InboxItemState.ProcessingQueued,
            InboxAction.Preview | InboxAction.CancelProcessing);
        InboxViewModel viewModel = CreateViewModel(query, CreateMetadata());
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        await viewModel.SelectAsync(
            Assert.Single(viewModel.Items),
            TestContext.Current.CancellationToken);

        query.SetState(
            InboxItemState.ReadyToTag,
            InboxAction.Preview | InboxAction.EditMetadata | InboxAction.Ingest);
        await viewModel.ApplyFiltersAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InboxItemState.ReadyToTag, viewModel.SelectedItem?.State);
        Assert.Equal(InboxItemState.ReadyToTag, viewModel.Inspector.State);
        Assert.Equal(string.Empty, viewModel.Inspector.ProcessingStatusText);
    }

    private static InboxViewModel CreateViewModel(
        IInboxQueryService query,
        MetadataEditorViewModel metadata,
        IIngestService? ingest = null,
        IProcessingCommandService? processingCommands = null,
        IProcessingDiagnosticsService? diagnostics = null) =>
        new(
            query,
            new CompletedScanner(),
            new RecordingActivityNotifier(),
            new ImmediateDispatcher(),
            new MissingPreviewResolver(),
            new InboxInspectorViewModel(metadata),
            ingest ?? new RecordingIngestService(() => true),
            new NoOpRecoveryService(),
            processingCommands ?? new RecordingProcessingCommandService(),
            diagnostics);

    private static MetadataEditorViewModel CreateMetadata() =>
        new(
            new RecordingMetadataGateway(),
            new BlockingDebounceScheduler());

    private sealed class RecordingInboxQuery : IInboxQueryService
    {
        public RecordingInboxQuery()
        {
            Cards =
            [
                Card(Guid.Parse("11111111-1111-1111-1111-111111111111"), "first.jpg"),
                Card(Guid.Parse("22222222-2222-2222-2222-222222222222"), "second.jpg")
            ];
        }

        public IReadOnlyList<InboxCardDto> Cards { get; }

        public InboxQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<InboxCardDto>> SearchAsync(
            InboxQuery query,
            CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(Cards);
        }

        public Task<InboxDetailsDto?> GetDetailsAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken)
        {
            InboxCardDto card = Cards.Single(item => item.Id == inboxItemId);
            var owner = new MetadataOwner(MetadataOwnerKind.InboxItem, inboxItemId);
            return Task.FromResult<InboxDetailsDto?>(new InboxDetailsDto(
                card,
                [],
                null,
                new MetadataDocument(owner, null, [], null, [], 0),
                InboxAction.Preview | InboxAction.EditMetadata | InboxAction.Ingest,
                null,
                null,
                false));
        }

        public Task ClearMissingAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        private static InboxCardDto Card(Guid id, string filename) =>
            new(
                id,
                filename,
                filename,
                MediaKind.Image,
                InboxItemState.ReadyToTag,
                "待入库",
                true);
    }

    private sealed class RecordingMetadataGateway : IMetadataEditorService
    {
        public int SaveCount { get; private set; }

        public List<MetadataOwner> SavedOwners { get; } = [];

        public Task<MetadataDocument?> LoadAsync(
            MetadataOwner owner,
            CancellationToken cancellationToken) =>
            Task.FromResult<MetadataDocument?>(new MetadataDocument(
                owner,
                null,
                [],
                null,
                [],
                0));

        public Task<MetadataSaveResult> SaveAsync(
            MetadataOwner owner,
            MetadataUpdate update,
            CancellationToken cancellationToken)
        {
            SaveCount++;
            SavedOwners.Add(owner);
            return Task.FromResult(new MetadataSaveResult(
                true,
                update.ExpectedVersion + 1,
                null));
        }
    }

    private sealed class RecordingIngestService(Func<bool> metadataWasFlushed)
        : IIngestService
    {
        public bool ObservedFlushedMetadata { get; private set; }

        public int CallCount { get; private set; }

        public Task<IngestResult> IngestAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ObservedFlushedMetadata = metadataWasFlushed();
            return Task.FromResult(new IngestResult(
                IngestOutcome.Completed,
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                "ingest.completed"));
        }
    }

    private sealed class SingleItemInboxQuery : IInboxQueryService
    {
        private readonly Guid id = Guid.NewGuid();
        private InboxDetailsDto details;

        public SingleItemInboxQuery(
            InboxItemState state,
            InboxAction allowedActions,
            ProcessingSummaryDto? processing = null)
        {
            details = CreateDetails(state, allowedActions, processing);
        }

        public void SetState(
            InboxItemState state,
            InboxAction allowedActions,
            ProcessingSummaryDto? processing = null)
        {
            details = CreateDetails(state, allowedActions, processing);
        }

        private InboxDetailsDto CreateDetails(
            InboxItemState state,
            InboxAction allowedActions,
            ProcessingSummaryDto? processing)
        {
            var card = new InboxCardDto(
                id,
                "clip.mp4",
                "clip.mp4",
                MediaKind.Video,
                state,
                state.ToString(),
                false,
                processing);
            var owner = new MetadataOwner(MetadataOwnerKind.InboxItem, card.Id);
            return new InboxDetailsDto(
                card,
                [],
                null,
                new MetadataDocument(owner, null, [], null, [], 0),
                allowedActions,
                null,
                "mp4_m4a_merge",
                false,
                processing);
        }

        public Task<IReadOnlyList<InboxCardDto>> SearchAsync(
            InboxQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InboxCardDto>>([details.Card]);

        public Task<InboxDetailsDto?> GetDetailsAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            Task.FromResult<InboxDetailsDto?>(details);

        public Task ClearMissingAsync(Guid inboxItemId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RecordingProcessingCommandService
        : IProcessingCommandService
    {
        public int StartCalls { get; private set; }

        public Task<ProcessingCommandResult> StartAsync(Guid inboxItemId, CancellationToken cancellationToken)
        {
            StartCalls++;
            return Task.FromResult(new ProcessingCommandResult(
                ProcessingCommandOutcome.Queued,
                Guid.NewGuid(),
                "已加入处理队列。"));
        }

        public Task<ProcessingCommandResult> CancelQueuedAsync(Guid inboxItemId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessingCommandResult(
                ProcessingCommandOutcome.Cancelled, Guid.NewGuid(), "已取消排队。"));

        public Task<ProcessingCommandResult> StopAsync(Guid inboxItemId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessingCommandResult(
                ProcessingCommandOutcome.StopRequested, Guid.NewGuid(), "正在停止处理。"));

        public Task<ProcessingCommandResult> RetryAsync(Guid inboxItemId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessingCommandResult(
                ProcessingCommandOutcome.Queued, Guid.NewGuid(), "已加入处理队列。"));
    }

    private sealed class FixedProcessingDiagnostics(bool available)
        : IProcessingDiagnosticsService
    {
        public Task<ProcessingDiagnosticsDto> CheckAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessingDiagnosticsDto(
                available,
                available ? "test" : null,
                available ? null : "processing.tool_missing"));
    }

    private sealed class CompletedScanner : IInboxScanner
    {
        public Task<ScanResult> ScanAsync(
            ScanTrigger trigger,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ScanResult(
                ScanOutcome.Completed,
                2,
                0,
                2,
                DateTimeOffset.UtcNow,
                "扫描完成"));
    }

    private sealed class RecordingActivityNotifier : IInboxActivityNotifier
    {
        public event EventHandler<InboxActivityEventArgs>? Changed;

        public void Publish(InboxActivityKind kind) =>
            Changed?.Invoke(this, new InboxActivityEventArgs(kind));

        public Task WaitForCandidateAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class MissingPreviewResolver : IMediaPreviewResolver
    {
        public Task<PreviewDescriptor> ResolveAsync(
            MetadataOwner owner,
            CancellationToken cancellationToken) =>
            throw new PreviewNotRegisteredException();
    }

    private sealed class NoOpRecoveryService : IIngestRecoveryService
    {
        public Task ReconcileAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<RecoveryResult> RetryAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RecoveryResult(true, "ok"));
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public void Enqueue(Action callback) => callback();
    }

    private sealed class BlockingDebounceScheduler : IDebounceScheduler
    {
        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
