using System.Text;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Application.Tests.Metadata;

public sealed class BatchMetadataServiceTests
{
    [Fact]
    public async Task Apply_converts_new_names_but_does_not_persist_them_separately()
    {
        var store = new RecordingBatchMetadataStore
        {
            ApplyResult = new BatchMetadataResult(true, 2, 0, 2, null)
        };
        var notifier = new RecordingInboxActivityNotifier();
        var service = new BatchMetadataService(store, notifier);
        BatchMetadataCommand command = AddAuthors(
            BatchTaxonomyReference.New(TaxonomyKind.Author, " 作者甲 "));

        BatchMetadataResult result = await service.ApplyAsync(
            command, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        BatchTaxonomyToken token = Assert.Single(store.LastWrite!.Authors);
        Assert.Equal("作者甲", token.NewName!.Display);
        Assert.Equal(
            "作者甲".Normalize(NormalizationForm.FormKC).ToUpperInvariant(),
            token.NewName.Normalized);
        Assert.Equal([InboxActivityKind.ItemChanged], notifier.Published);
    }

    [Fact]
    public async Task Invalid_new_name_never_reaches_the_atomic_store()
    {
        var store = new RecordingBatchMetadataStore();
        var service = new BatchMetadataService(
            store, new RecordingInboxActivityNotifier());

        BatchMetadataResult result = await service.ApplyAsync(
            AddAuthors(BatchTaxonomyReference.New(TaxonomyKind.Author, "   ")),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("batch.metadata.taxonomy_invalid", result.ErrorCode);
        Assert.Null(store.LastWrite);
    }

    [Fact]
    public async Task No_op_success_does_not_publish_a_refresh_storm()
    {
        var notifier = new RecordingInboxActivityNotifier();
        var store = new RecordingBatchMetadataStore
        {
            ApplyResult = new BatchMetadataResult(true, 0, 3, 0, null)
        };
        var service = new BatchMetadataService(store, notifier);

        await service.ApplyAsync(
            AddAuthors(ExistingAuthor()), TestContext.Current.CancellationToken);

        Assert.Empty(notifier.Published);
    }

    [Fact]
    public async Task Duplicate_references_are_normalized_and_deduplicated_in_order()
    {
        Guid existing = Guid.NewGuid();
        var store = new RecordingBatchMetadataStore
        {
            ApplyResult = new BatchMetadataResult(true, 1, 0, 1, null)
        };
        var service = new BatchMetadataService(
            store, new RecordingInboxActivityNotifier());
        BatchMetadataCommand command = BatchMetadataCommand.Create(
            [Selection()],
            BatchMetadataMode.Add,
            new(
                BatchMetadataFieldOperation.Add,
                [
                    BatchTaxonomyReference.Existing(TaxonomyKind.Author, existing),
                    BatchTaxonomyReference.New(TaxonomyKind.Author, "作者甲"),
                    BatchTaxonomyReference.New(TaxonomyKind.Author, " 作者甲 "),
                    BatchTaxonomyReference.Existing(TaxonomyKind.Author, existing)
                ]),
            BatchMetadataSeriesEdit.Unchanged,
            BatchMetadataSetEdit.Unchanged);

        await service.ApplyAsync(command, TestContext.Current.CancellationToken);

        Assert.Collection(
            store.LastWrite!.Authors,
            token => Assert.Equal(existing, token.ExistingId),
            token => Assert.Equal("作者甲", token.NewName!.Display));
    }

    [Fact]
    public async Task Externally_constructed_invalid_selection_returns_a_stable_error()
    {
        var store = new RecordingBatchMetadataStore();
        var service = new BatchMetadataService(
            store, new RecordingInboxActivityNotifier());
        var command = new BatchMetadataCommand(
            [],
            BatchMetadataMode.Add,
            new(BatchMetadataFieldOperation.Add, [ExistingAuthor()]),
            BatchMetadataSeriesEdit.Unchanged,
            BatchMetadataSetEdit.Unchanged);

        BatchMetadataResult result = await service.ApplyAsync(
            command, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("batch.metadata.selection_invalid", result.ErrorCode);
        Assert.Null(store.LastWrite);
    }

    private static BatchMetadataCommand AddAuthors(
        BatchTaxonomyReference author) =>
        BatchMetadataCommand.Create(
            [Selection()],
            BatchMetadataMode.Add,
            new(BatchMetadataFieldOperation.Add, [author]),
            BatchMetadataSeriesEdit.Unchanged,
            BatchMetadataSetEdit.Unchanged);

    private static BatchTaxonomyReference ExistingAuthor() =>
        BatchTaxonomyReference.Existing(TaxonomyKind.Author, Guid.NewGuid());

    private static InboxSelectionEntry Selection() =>
        new(Guid.NewGuid(), 0, 0);

    private sealed class RecordingBatchMetadataStore : IBatchMetadataStore
    {
        public BatchMetadataWrite? LastWrite { get; private set; }

        public BatchMetadataResult ApplyResult { get; init; } =
            new(false, 0, 0, 0, "not_configured");

        public Task<BatchMetadataEditorDto?> PrepareAsync(
            IReadOnlyList<InboxSelectionEntry> selection,
            CancellationToken cancellationToken) =>
            Task.FromResult<BatchMetadataEditorDto?>(null);

        public Task<BatchMetadataResult> ApplyAsync(
            BatchMetadataWrite write,
            CancellationToken cancellationToken)
        {
            LastWrite = write;
            return Task.FromResult(ApplyResult);
        }
    }

    private sealed class RecordingInboxActivityNotifier : IInboxActivityNotifier
    {
        public event EventHandler<InboxActivityEventArgs>? Changed;

        public List<InboxActivityKind> Published { get; } = [];

        public void Publish(InboxActivityKind kind)
        {
            Published.Add(kind);
            Changed?.Invoke(this, new InboxActivityEventArgs(kind));
        }

        public Task WaitForCandidateAsync(CancellationToken cancellationToken) =>
            Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
