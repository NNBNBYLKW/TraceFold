using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
using TraceFold.Media.Desktop.Dialogs;
using TraceFold.Media.Desktop.Pages;
using TraceFold.Media.Desktop.Services;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

public sealed class LibraryViewModelTests
{
    [Fact]
    public async Task Search_reloads_cards_and_selection_loads_formal_metadata()
    {
        var library = new RecordingLibraryQueryService();
        var metadataGateway = new RecordingMetadataGateway();
        LibraryViewModel viewModel = CreateViewModel(
            library,
            metadataService: metadataGateway);

        viewModel.SearchText = "杭州";
        await viewModel.ApplySearchAsync(TestContext.Current.CancellationToken);
        await viewModel.SelectAsync(
            viewModel.Items[0],
            TestContext.Current.CancellationToken);

        Assert.Equal("杭州", library.LastQuery?.SearchText);
        Assert.Equal(
            new MetadataOwner(MetadataOwnerKind.MediaAsset, library.Result[0].Id),
            metadataGateway.LoadedOwner);
    }

    [Fact]
    public async Task Reveal_command_passes_only_registered_asset_id()
    {
        var reveal = new FakeRevealService();
        var library = new RecordingLibraryQueryService();
        LibraryViewModel viewModel = CreateViewModel(library, reveal);
        await viewModel.ApplySearchAsync(TestContext.Current.CancellationToken);
        await viewModel.SelectAsync(
            viewModel.Items[0],
            TestContext.Current.CancellationToken);

        await viewModel.RevealSelectedCommand.ExecuteAsync(null);

        Assert.Equal(library.Result[0].Id, reveal.RevealedId);
    }

    [Fact]
    public async Task Applying_filter_clears_selection_but_sorting_preserves_it()
    {
        var library = new RecordingLibraryQueryService();
        LibraryViewModel viewModel = CreateViewModel(library);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        viewModel.Selection.ReplaceFromGrid(
            [viewModel.Items[0]],
            viewModel.Items.ToArray());
        LibraryQuery filter = LibraryQuery.Create(
            null,
            LibraryMediaFilter.All,
            [],
            null,
            [],
            false,
            false,
            true,
            LibraryDisplayNameFilter.All,
            LibrarySortField.CreatedAt,
            LibrarySortDirection.Descending);

        await viewModel.ApplyFilterAsync(
            filter,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, viewModel.Selection.SelectedCount);
        Assert.Equal("查询条件已变化，已取消选择。", viewModel.StatusMessage);

        viewModel.Selection.ReplaceFromGrid(
            [viewModel.Items[0]],
            viewModel.Items.ToArray());
        await viewModel.ChangeSortAsync(
            LibrarySortField.ActualFilename,
            LibrarySortDirection.Ascending,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, viewModel.Selection.SelectedCount);
        Assert.Equal(
            LibrarySortField.ActualFilename,
            library.LastQuery?.SortField);
    }

    [Fact]
    public async Task Current_filter_clears_selection_only_when_query_changes()
    {
        var library = new RecordingLibraryQueryService();
        LibraryViewModel viewModel = CreateViewModel(library);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        viewModel.Selection.ReplaceFromGrid(
            viewModel.Items,
            viewModel.Items);
        viewModel.Filter.BeginEdit();
        viewModel.Filter.ApplyDraft();

        await viewModel.ApplyCurrentFilterAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(2, viewModel.Selection.SelectedCount);
        Assert.NotEqual("查询条件已变化，已取消选择。", viewModel.StatusMessage);

        viewModel.Filter.BeginEdit();
        viewModel.Filter.MissingTags = true;
        viewModel.Filter.ApplyDraft();
        await viewModel.ApplyCurrentFilterAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(0, viewModel.Selection.SelectedCount);
        Assert.Equal("查询条件已变化，已取消选择。", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Load_reads_persisted_sort_only_once()
    {
        var sort = new RecordingSortPreferenceService(
            new LibrarySortPreference(
                LibrarySortField.DisplayName,
                LibrarySortDirection.Ascending));
        LibraryViewModel viewModel = CreateViewModel(
            new RecordingLibraryQueryService(),
            sortPreferences: sort);

        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, sort.LoadCount);
        Assert.Equal(LibrarySortField.DisplayName, viewModel.Filter.AppliedQuery.SortField);
    }

    [Fact]
    public void Multi_selection_summary_counts_images_and_videos()
    {
        LibrarySelectionSummaryViewModel summary =
            LibrarySelectionSummaryViewModel.From([
                SummaryCard(MediaKind.Image),
                SummaryCard(MediaKind.Image),
                SummaryCard(MediaKind.Video)
            ]);

        Assert.Equal(3, summary.TotalCount);
        Assert.Equal(2, summary.ImageCount);
        Assert.Equal(1, summary.VideoCount);
    }

    [Fact]
    public async Task Batch_edit_passes_only_versioned_selection_and_refreshes_once()
    {
        var dialog = new RecordingLibraryBatchEditDialogService(applied: true);
        var library = new RecordingLibraryQueryService();
        LibraryViewModel viewModel = CreateViewModel(
            library,
            dialog: dialog);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        viewModel.Selection.ReplaceFromGrid(
            viewModel.Items.Take(2),
            viewModel.Items);

        await viewModel.BatchEditCommand.ExecuteAsync(null);

        Assert.Equal(2, dialog.Selection.Count);
        Assert.All(dialog.Selection, entry =>
            Assert.True(entry.ExpectedMetadataVersion >= 0));
        Assert.Equal(2, library.SearchCallCount);
    }

    [Fact]
    public async Task Batch_result_and_activity_notification_are_coalesced_to_one_refresh()
    {
        var notifier = new LibraryActivityNotifier();
        var library = new RecordingLibraryQueryService();
        var dialog = new RecordingLibraryBatchEditDialogService(
            applied: true,
            onShow: () => notifier.Publish(
                LibraryActivityKind.BatchChanged,
                library.Result.Select(item => item.Id).ToArray()));
        LibraryViewModel viewModel = CreateViewModel(
            library,
            dialog: dialog,
            notifier: notifier);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        viewModel.Selection.ReplaceFromGrid(viewModel.Items, viewModel.Items);

        await viewModel.BatchEditCommand.ExecuteAsync(null);

        Assert.Equal(2, library.SearchCallCount);
    }

    [Fact]
    public async Task Failed_batch_dialog_preserves_selection_without_reload()
    {
        var dialog = new RecordingLibraryBatchEditDialogService(applied: false);
        var library = new RecordingLibraryQueryService();
        LibraryViewModel viewModel = CreateViewModel(library, dialog: dialog);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        viewModel.Selection.ReplaceFromGrid(viewModel.Items, viewModel.Items);

        await viewModel.BatchEditCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Selection.SelectedCount);
        Assert.Equal(1, library.SearchCallCount);
    }

    [Fact]
    public async Task Successful_batch_refresh_drops_assets_no_longer_matching_query()
    {
        var library = new RecordingLibraryQueryService();
        var dialog = new RecordingLibraryBatchEditDialogService(
            applied: true,
            onShow: () => library.Result.RemoveAt(1));
        LibraryViewModel viewModel = CreateViewModel(library, dialog: dialog);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        viewModel.Selection.ReplaceFromGrid(viewModel.Items, viewModel.Items);

        await viewModel.BatchEditCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Items);
        Assert.Equal(1, viewModel.Selection.SelectedCount);
    }

    [Fact]
    public void Undo_state_change_hides_the_banner()
    {
        var batchEditor = new RecordingLibraryBatchEditorService(
            new LibraryBatchUndoState(true, 2));
        LibraryViewModel viewModel = CreateViewModel(
            new RecordingLibraryQueryService(),
            batchEditor: batchEditor);

        batchEditor.PublishUndoState(LibraryBatchUndoState.Unavailable);

        Assert.False(viewModel.CanUndoBatch);
    }

    [Fact]
    public async Task Undo_success_refreshes_once_and_reports_restored_count()
    {
        var library = new RecordingLibraryQueryService();
        var batchEditor = new RecordingLibraryBatchEditorService(
            new LibraryBatchUndoState(true, 2),
            new LibraryBatchUndoResult(true, 2, null));
        LibraryViewModel viewModel = CreateViewModel(
            library,
            batchEditor: batchEditor);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        await viewModel.UndoBatchCommand.ExecuteAsync(null);

        Assert.Equal(2, library.SearchCallCount);
        Assert.Contains("已撤销", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.False(viewModel.CanUndoBatch);
    }

    [Theory]
    [InlineData(
        "library.batch.undo.version_conflict",
        "资料已在撤销后发生变化，无法撤销上一批修改。")]
    [InlineData(
        "library.batch.undo.unavailable",
        "当前没有可撤销的批量修改。")]
    public async Task Stable_undo_errors_are_mapped_and_consume_the_banner(
        string errorCode,
        string expectedMessage)
    {
        var batchEditor = new RecordingLibraryBatchEditorService(
            new LibraryBatchUndoState(true, 2),
            new LibraryBatchUndoResult(
                false,
                0,
                errorCode));
        LibraryViewModel viewModel = CreateViewModel(
            new RecordingLibraryQueryService(),
            batchEditor: batchEditor);

        await viewModel.UndoBatchCommand.ExecuteAsync(null);

        Assert.False(viewModel.CanUndoBatch);
        Assert.Equal(expectedMessage, viewModel.StatusMessage);
    }

    [Fact]
    public async Task Unrelated_single_save_preserves_batch_undo()
    {
        var library = new RecordingLibraryQueryService();
        Guid protectedAssetId = library.Result[1].Id;
        var batchEditor = new RecordingLibraryBatchEditorService(
            new LibraryBatchUndoState(true, 1),
            protectedAssetId: protectedAssetId);
        LibraryViewModel viewModel = CreateViewModel(
            library,
            batchEditor: batchEditor);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        await viewModel.SelectAsync(
            viewModel.Items[0],
            TestContext.Current.CancellationToken);

        viewModel.Inspector.Metadata.DisplayName = "单项修改";
        await viewModel.Inspector.Metadata.FlushAsync(
            TestContext.Current.CancellationToken);

        Assert.Contains(library.Result[0].Id, batchEditor.InvalidatedAssetIds);
        Assert.True(viewModel.CanUndoBatch);
    }

    private static LibraryCardViewModel SummaryCard(MediaKind kind) =>
        new(new LibraryAssetDto(
            Guid.NewGuid(),
            kind == MediaKind.Image ? "image.jpg" : "video.mp4",
            "Asset",
            kind,
            DateTimeOffset.Parse("2026-08-26T00:00:00Z")));

    private static LibraryViewModel CreateViewModel(
        ILibraryQueryService library,
        IAssetRevealService? reveal = null,
        ILibrarySortPreferenceService? sortPreferences = null,
        IMetadataEditorService? metadataService = null,
        ILibraryBatchEditDialogService? dialog = null,
        ILibraryBatchEditorService? batchEditor = null,
        ILibraryActivityNotifier? notifier = null)
    {
        var preview = new FixedPreviewResolver();
        return new LibraryViewModel(
            library,
            sortPreferences ?? new RecordingSortPreferenceService(
                LibrarySortPreference.Default),
            new LibrarySelectionViewModel(),
            new LibraryFilterViewModel(new EmptyTaxonomySearchService()),
            new LibraryInspectorViewModel(
                CreateEditor(metadataService ?? new RecordingMetadataGateway()),
                preview),
            reveal ?? new FakeRevealService(),
            preview,
            dialog ?? new RecordingLibraryBatchEditDialogService(false),
            batchEditor ?? new RecordingLibraryBatchEditorService(
                LibraryBatchUndoState.Unavailable),
            notifier ?? new LibraryActivityNotifier());
    }

    private static MetadataEditorViewModel CreateEditor(
        IMetadataEditorService service) =>
        new(service, new BlockingDebounceScheduler());

    private sealed class RecordingLibraryQueryService : ILibraryQueryService
    {
        public List<LibraryAssetDto> Result { get; } =
        [
            new LibraryAssetDto(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "photo.jpg",
                "杭州夜景",
                MediaKind.Image,
                DateTimeOffset.Parse("2026-08-22T10:00:00Z"),
                3),
            new LibraryAssetDto(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "video.mp4",
                "杭州延时",
                MediaKind.Video,
                DateTimeOffset.Parse("2026-08-23T10:00:00Z"),
                4)
        ];

        public LibraryQuery? LastQuery { get; private set; }

        public int SearchCallCount { get; private set; }

        public Task<IReadOnlyList<LibraryAssetDto>> SearchAsync(
            LibraryQuery query,
            CancellationToken cancellationToken)
        {
            SearchCallCount++;
            LastQuery = query;
            return Task.FromResult<IReadOnlyList<LibraryAssetDto>>(
                Result.ToArray());
        }

        public Task<LibraryAssetDetailsDto?> GetDetailsAsync(
            Guid assetId,
            CancellationToken cancellationToken)
        {
            LibraryAssetDto card = Result.Single(item => item.Id == assetId);
            var owner = new MetadataOwner(MetadataOwnerKind.MediaAsset, assetId);
            return Task.FromResult<LibraryAssetDetailsDto?>(new LibraryAssetDetailsDto(
                card,
                new MetadataDocument(owner, card.DisplayName, [], null, [], 0),
                3,
                new string('A', 64)));
        }
    }

    private sealed class RecordingMetadataGateway : IMetadataEditorService
    {
        public MetadataOwner? LoadedOwner { get; private set; }

        public Task<MetadataDocument?> LoadAsync(
            MetadataOwner owner,
            CancellationToken cancellationToken)
        {
            LoadedOwner = owner;
            return Task.FromResult<MetadataDocument?>(new MetadataDocument(
                owner,
                "杭州夜景",
                [],
                null,
                [],
                0));
        }

        public Task<MetadataSaveResult> SaveAsync(
            MetadataOwner owner,
            MetadataUpdate update,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MetadataSaveResult(
                true,
                update.ExpectedVersion + 1,
                null));
    }

    private sealed class FixedPreviewResolver : IMediaPreviewResolver
    {
        public Task<PreviewDescriptor> ResolveAsync(
            MetadataOwner owner,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PreviewDescriptor(
                MediaKind.Image,
                "C:\\library\\photo.jpg",
                true));
    }

    private sealed class FakeRevealService : IAssetRevealService
    {
        public Guid? RevealedId { get; private set; }

        public Task<AssetRevealResult> RevealAsync(
            Guid assetId,
            CancellationToken cancellationToken)
        {
            RevealedId = assetId;
            return Task.FromResult(new AssetRevealResult(true, null));
        }
    }

    private sealed class RecordingSortPreferenceService(
        LibrarySortPreference loaded) : ILibrarySortPreferenceService
    {
        public int LoadCount { get; private set; }

        public Task<LibrarySortPreference> LoadAsync(
            CancellationToken cancellationToken)
        {
            LoadCount++;
            return Task.FromResult(loaded);
        }

        public Task<string?> SaveAsync(
            LibrarySortPreference preference,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    private sealed class EmptyTaxonomySearchService : ITaxonomySearchService
    {
        public Task<IReadOnlyList<TaxonomyTermDto>> SearchAsync(
            TraceFold.Media.Domain.Metadata.TaxonomyKind kind,
            string? query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaxonomyTermDto>>([]);
    }

    private sealed class BlockingDebounceScheduler : IDebounceScheduler
    {
        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class RecordingLibraryBatchEditDialogService(
        bool applied,
        Action? onShow = null) : ILibraryBatchEditDialogService
    {
        public IReadOnlyList<LibrarySelectionEntry> Selection { get; private set; } = [];

        public Task<LibraryBatchEditDialogResult> ShowAsync(
            IReadOnlyList<LibrarySelectionEntry> selection,
            CancellationToken cancellationToken)
        {
            Selection = selection.ToArray();
            onShow?.Invoke();
            LibraryBatchApplyResult? result = applied
                ? new(true, selection.Count, 0, 0, true, null)
                : null;
            return Task.FromResult(new LibraryBatchEditDialogResult(
                applied,
                result));
        }
    }

    private sealed class RecordingLibraryBatchEditorService : ILibraryBatchEditorService
    {
        private LibraryBatchUndoState _undoState;
        private readonly LibraryBatchUndoResult _undoResult;
        private readonly Guid? _protectedAssetId;

        public RecordingLibraryBatchEditorService(
            LibraryBatchUndoState undoState,
            LibraryBatchUndoResult? undoResult = null,
            Guid? protectedAssetId = null)
        {
            _undoState = undoState;
            _undoResult = undoResult
                ?? new LibraryBatchUndoResult(false, 0, "library.batch.undo_unavailable");
            _protectedAssetId = protectedAssetId;
        }

        public LibraryBatchUndoState UndoState => _undoState;

        public List<Guid> InvalidatedAssetIds { get; } = [];

        public event EventHandler? UndoStateChanged;

        public void PublishUndoState(LibraryBatchUndoState state)
        {
            _undoState = state;
            UndoStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task<LibraryBatchPrepareResult> PrepareAsync(
            IReadOnlyList<LibrarySelectionEntry> selection,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public LibraryBatchPreviewDto Preview(
            LibraryBatchEditorDto editor,
            LibraryBatchEditDraft draft) => throw new NotSupportedException();

        public Task<LibraryBatchApplyResult> ApplyAsync(
            IReadOnlyList<LibrarySelectionEntry> selection,
            LibraryBatchEditDraft draft,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LibraryBatchUndoResult> UndoLastAsync(
            CancellationToken cancellationToken)
        {
            PublishUndoState(LibraryBatchUndoState.Unavailable);
            return Task.FromResult(_undoResult);
        }

        public void InvalidateUndoFor(Guid assetId)
        {
            InvalidatedAssetIds.Add(assetId);
            if (_protectedAssetId == assetId)
            {
                PublishUndoState(LibraryBatchUndoState.Unavailable);
            }
        }
    }
}
