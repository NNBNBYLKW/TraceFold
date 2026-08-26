using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Application.Tests.Library;

public sealed class LibraryBatchEditorServiceTests
{
    [Fact]
    public async Task Apply_is_not_sent_to_store_when_preview_has_errors()
    {
        LibraryBatchEditorDto editor = Editor(Asset(series: null));
        var store = new RecordingLibraryBatchEditStore(editor);
        var notifier = new RecordingLibraryActivityNotifier();
        LibraryBatchEditorService service = CreateService(store, notifier);
        LibraryBatchEditDraft draft = LibraryBatchEditDraft.Create(
            BatchMetadataDraft.Create(
                BatchMetadataMode.Add,
                BatchMetadataSetEdit.Unchanged,
                BatchMetadataSeriesEdit.Unchanged,
                new(
                    BatchMetadataFieldOperation.Add,
                    [BatchTaxonomyReference.New(
                        TaxonomyKind.Tag,
                        "Safe")])),
            Definition(new MetadataTemplateNamingRule("{series}")));

        LibraryBatchApplyResult result = await service.ApplyAsync(
            editor.Assets.Select(asset => asset.Selection).ToArray(),
            draft,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("library.batch.preview_invalid", result.ErrorCode);
        Assert.Equal(0, store.ApplyCallCount);
        Assert.Empty(notifier.Events);
    }

    [Fact]
    public async Task Successful_apply_publishes_one_combined_event()
    {
        LibraryBatchEditorDto editor = Editor(Asset());
        LibrarySelectionEntry[] selection =
            editor.Assets.Select(asset => asset.Selection).ToArray();
        LibraryBatchUndoReceipt receipt = Receipt(selection[0]);
        var store = new RecordingLibraryBatchEditStore(editor)
        {
            ApplyResult = new LibraryBatchStoreResult(
                true, 1, 0, 0, receipt, null)
        };
        var notifier = new RecordingLibraryActivityNotifier();
        LibraryBatchEditorService service = CreateService(store, notifier);

        LibraryBatchApplyResult result = await service.ApplyAsync(
            selection,
            LibraryBatchEditDraft.Create(
                null,
                Definition(new PrefixNamingRule("X-"))),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.True(result.CanUndo);
        Assert.Equal(1, store.ApplyCallCount);
        LibraryActivityEventArgs changed = Assert.Single(notifier.Events);
        Assert.Equal(LibraryActivityKind.BatchChanged, changed.Kind);
        Assert.Equal([selection[0].AssetId], changed.AssetIds);
    }

    [Fact]
    public async Task Prepare_failure_and_store_failure_are_stable_and_silent()
    {
        var notifier = new RecordingLibraryActivityNotifier();
        var prepareFailure = new RecordingLibraryBatchEditStore(null)
        {
            PrepareErrorCode = "library.batch.version_conflict"
        };
        LibraryBatchApplyResult rejected = await CreateService(
            prepareFailure,
            notifier).ApplyAsync(
                [new LibrarySelectionEntry(Guid.NewGuid(), 0, 0)],
                LibraryBatchEditDraft.Create(
                    null,
                    Definition(new PrefixNamingRule("X-"))),
                TestContext.Current.CancellationToken);

        LibraryBatchEditorDto editor = Editor(Asset());
        var applyFailure = new RecordingLibraryBatchEditStore(editor)
        {
            ApplyResult = new LibraryBatchStoreResult(
                false,
                0,
                1,
                0,
                null,
                "library.batch.persistence_failed")
        };
        LibraryBatchApplyResult failed = await CreateService(
            applyFailure,
            notifier).ApplyAsync(
                editor.Assets.Select(asset => asset.Selection).ToArray(),
                LibraryBatchEditDraft.Create(
                    null,
                    Definition(new PrefixNamingRule("X-"))),
                TestContext.Current.CancellationToken);

        Assert.Equal("library.batch.version_conflict", rejected.ErrorCode);
        Assert.Equal("library.batch.persistence_failed", failed.ErrorCode);
        Assert.Empty(notifier.Events);
    }

    private static LibraryBatchEditorService CreateService(
        ILibraryBatchEditStore store,
        ILibraryActivityNotifier notifier) =>
        new(store, new LibraryBatchPreviewService(new NamingRuleEngine()), notifier);

    private static NamingDefinition Definition(params INamingRule[] rules) =>
        NamingDefinition.Create(NamingSource.ActualFilename, rules);

    private static LibraryBatchEditorDto Editor(LibraryBatchAssetDto asset) =>
        new(
            [asset],
            [],
            [],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static LibraryBatchAssetDto Asset(
        TaxonomyTermDto? series = null) =>
        new(
            new LibrarySelectionEntry(Guid.NewGuid(), 0, 0),
            "asset.jpg",
            null,
            [],
            series,
            []);

    private static LibraryBatchUndoReceipt Receipt(
        LibrarySelectionEntry selection) =>
        new(
            Guid.NewGuid(),
            [
                new LibraryBatchUndoItem(
                    selection.AssetId,
                    selection.ExpectedMetadataVersion + 1,
                    null,
                    null,
                    [],
                    [])
            ]);

    private sealed class RecordingLibraryBatchEditStore(
        LibraryBatchEditorDto? editor) : ILibraryBatchEditStore
    {
        public string? PrepareErrorCode { get; init; }

        public int ApplyCallCount { get; private set; }

        public LibraryBatchStoreResult ApplyResult { get; init; } =
            new(false, 0, 0, 0, null, "not_configured");

        public Task<LibraryBatchPrepareResult> PrepareAsync(
            IReadOnlyList<LibrarySelectionEntry> selection,
            CancellationToken cancellationToken) =>
            Task.FromResult(new LibraryBatchPrepareResult(
                PrepareErrorCode is null,
                PrepareErrorCode is null ? editor : null,
                PrepareErrorCode));

        public Task<LibraryBatchStoreResult> ApplyAsync(
            IReadOnlyList<LibrarySelectionEntry> selection,
            LibraryBatchEditDraft draft,
            CancellationToken cancellationToken)
        {
            ApplyCallCount++;
            return Task.FromResult(ApplyResult);
        }
    }

    private sealed class RecordingLibraryActivityNotifier
        : ILibraryActivityNotifier
    {
        public event EventHandler<LibraryActivityEventArgs>? Changed;

        public List<LibraryActivityEventArgs> Events { get; } = [];

        public void Publish(
            LibraryActivityKind kind,
            IReadOnlyList<Guid> assetIds)
        {
            var args = new LibraryActivityEventArgs(kind, assetIds);
            Events.Add(args);
            Changed?.Invoke(this, args);
        }
    }
}
