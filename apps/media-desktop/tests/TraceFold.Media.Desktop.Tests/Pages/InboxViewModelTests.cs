using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Ingest;
using TraceFold.Media.Domain.Processing;
using TraceFold.Media.Desktop.Dialogs;
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

    [Fact]
    public async Task Closing_automatic_conflict_dialog_does_not_resolve_case()
    {
        var query = new ConflictFlowInboxQuery(blocked: false);
        ConflictCaseDto conflict = ConflictCase(query.Id);
        var dialog = new RecordingConflictDialogService { NextResult = null };
        var resolution = new RecordingConflictResolutionService();
        var ingest = new ConflictIngestService(query.Block, conflict.Id);
        InboxViewModel viewModel = CreateViewModel(
            query,
            CreateMetadata(),
            ingest,
            conflicts: new FixedConflictQueryService(conflict),
            conflictResolution: resolution,
            conflictDialog: dialog);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        await viewModel.SelectAsync(
            Assert.Single(viewModel.Items),
            TestContext.Current.CancellationToken);

        await viewModel.IngestCommand.ExecuteAsync(null);

        Assert.Equal(1, dialog.ShowCount);
        Assert.Equal(0, resolution.CallCount);
        Assert.Equal(InboxItemState.ConflictBlocked, viewModel.Inspector.State);
    }

    [Fact]
    public async Task Reopen_command_resolves_selected_single_target_and_refreshes_details()
    {
        var query = new ConflictFlowInboxQuery(blocked: true);
        ConflictCaseDto conflict = ConflictCase(query.Id);
        Guid matchId = Assert.Single(conflict.Matches).Id;
        var dialog = new RecordingConflictDialogService
        {
            NextResult = new ConflictDialogResult(
                ConflictResolution.Replace,
                matchId)
        };
        var resolution = new RecordingConflictResolutionService();
        InboxViewModel viewModel = CreateViewModel(
            query,
            CreateMetadata(),
            conflicts: new FixedConflictQueryService(conflict),
            conflictResolution: resolution,
            conflictDialog: dialog);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        await viewModel.SelectAsync(
            Assert.Single(viewModel.Items),
            TestContext.Current.CancellationToken);
        int detailsBefore = query.GetDetailsCallCount;

        await viewModel.ResolveConflictCommand.ExecuteAsync(null);

        Assert.Equal((conflict.Id, ConflictResolution.Replace, (Guid?)matchId),
            resolution.LastCall);
        Assert.True(query.GetDetailsCallCount > detailsBefore);
    }

    private static InboxViewModel CreateViewModel(
        IInboxQueryService query,
        MetadataEditorViewModel metadata,
        IIngestService? ingest = null,
        IProcessingCommandService? processingCommands = null,
        IProcessingDiagnosticsService? diagnostics = null,
        IConflictQueryService? conflicts = null,
        IConflictResolutionService? conflictResolution = null,
        IConflictDialogService? conflictDialog = null) =>
        new(
            query,
            new CompletedScanner(),
            new RecordingActivityNotifier(),
            new ImmediateDispatcher(),
            new MissingPreviewResolver(),
            new InboxInspectorViewModel(metadata),
            ingest ?? new RecordingIngestService(() => true),
            new NoOpRecoveryService(),
            conflicts ?? new FixedConflictQueryService(null),
            conflictResolution ?? new RecordingConflictResolutionService(),
            conflictDialog ?? new RecordingConflictDialogService(),
            processingCommands ?? new RecordingProcessingCommandService(),
            diagnostics);

    private static ConflictCaseDto ConflictCase(Guid inboxItemId)
    {
        Guid matchId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var metadata = new ConflictMetadataSnapshot(null, [], null, []);
        var technical = new ConflictTechnicalMetadata("jpg", 100, 100, null);
        var incoming = new ConflictMediaDto(
            inboxItemId,
            true,
            false,
            null,
            "photo.jpg",
            "photo.jpg",
            MediaKind.Image,
            3,
            DateTimeOffset.UtcNow,
            new string('A', 64),
            ConflictReason.None,
            metadata,
            technical,
            null);
        var match = new ConflictMediaDto(
            matchId,
            false,
            true,
            Guid.NewGuid(),
            "photo.jpg",
            "旧图片",
            MediaKind.Image,
            3,
            DateTimeOffset.UtcNow,
            new string('B', 64),
            ConflictReason.Name,
            metadata,
            technical,
            null);
        return new ConflictCaseDto(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            inboxItemId,
            incoming,
            [match],
            null,
            null,
            matchId,
            true);
    }

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

    private sealed class ConflictIngestService(Action block, Guid caseId)
        : IIngestService
    {
        public Task<IngestResult> IngestAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken)
        {
            block();
            return Task.FromResult(new IngestResult(
                IngestOutcome.ConflictBlocked,
                null,
                null,
                caseId,
                "ingest.conflict"));
        }
    }

    private sealed class FixedConflictQueryService(ConflictCaseDto? conflict)
        : IConflictQueryService
    {
        public Task<ConflictCaseDto?> GetOpenAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) =>
            Task.FromResult(conflict);
    }

    private sealed class RecordingConflictResolutionService
        : IConflictResolutionService
    {
        public int CallCount { get; private set; }

        public (Guid CaseId, ConflictResolution Resolution, Guid? MatchId)? LastCall
            { get; private set; }

        public Task<IngestResult> ResolveAsync(
            Guid conflictCaseId,
            ConflictResolution resolution,
            Guid? selectedMatchId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastCall = (conflictCaseId, resolution, selectedMatchId);
            return Task.FromResult(new IngestResult(
                resolution == ConflictResolution.Skip
                    ? IngestOutcome.Skipped
                    : IngestOutcome.Completed,
                Guid.NewGuid(),
                resolution == ConflictResolution.Skip ? null : Guid.NewGuid(),
                conflictCaseId,
                "ok"));
        }
    }

    private sealed class RecordingConflictDialogService
        : IConflictDialogService
    {
        public ConflictDialogResult? NextResult { get; set; }

        public int ShowCount { get; private set; }

        public Task<ConflictDialogResult?> ShowAsync(
            ConflictCaseDto conflict,
            CancellationToken cancellationToken)
        {
            ShowCount++;
            return Task.FromResult(NextResult);
        }
    }

    private sealed class ConflictFlowInboxQuery : IInboxQueryService
    {
        private InboxItemState state;

        public ConflictFlowInboxQuery(bool blocked)
        {
            state = blocked
                ? InboxItemState.ConflictBlocked
                : InboxItemState.ReadyToTag;
        }

        public Guid Id { get; } = Guid.NewGuid();

        public int GetDetailsCallCount { get; private set; }

        public void Block() => state = InboxItemState.ConflictBlocked;

        public Task<IReadOnlyList<InboxCardDto>> SearchAsync(
            InboxQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InboxCardDto>>([Card()]);

        public Task<InboxDetailsDto?> GetDetailsAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken)
        {
            GetDetailsCallCount++;
            InboxCardDto card = Card();
            var owner = new MetadataOwner(MetadataOwnerKind.InboxItem, Id);
            return Task.FromResult<InboxDetailsDto?>(new InboxDetailsDto(
                card,
                [],
                null,
                new MetadataDocument(owner, null, [], null, [], 0),
                state == InboxItemState.ConflictBlocked
                    ? InboxAction.Preview | InboxAction.ResolveConflict
                    : InboxAction.Preview | InboxAction.EditMetadata
                        | InboxAction.Ingest,
                null,
                null,
                false));
        }

        public Task ClearMissingAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        private InboxCardDto Card() => new(
            Id,
            "photo.jpg",
            "photo.jpg",
            MediaKind.Image,
            state,
            state.ToString(),
            state == InboxItemState.ReadyToTag);
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
