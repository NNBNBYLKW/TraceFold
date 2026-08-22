using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Domain.Inbox;
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

    private static InboxViewModel CreateViewModel(
        RecordingInboxQuery query,
        MetadataEditorViewModel metadata,
        IOrdinaryIngestService? ingest = null) =>
        new(
            query,
            new CompletedScanner(),
            new RecordingActivityNotifier(),
            new ImmediateDispatcher(),
            new MissingPreviewResolver(),
            new InboxInspectorViewModel(metadata),
            ingest ?? new RecordingIngestService(() => true),
            new NoOpRecoveryService());

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
        : IOrdinaryIngestService
    {
        public bool ObservedFlushedMetadata { get; private set; }

        public Task<IngestResult> IngestAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken)
        {
            ObservedFlushedMetadata = metadataWasFlushed();
            return Task.FromResult(new IngestResult(
                IngestOutcome.Completed,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "ingest.completed"));
        }
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
