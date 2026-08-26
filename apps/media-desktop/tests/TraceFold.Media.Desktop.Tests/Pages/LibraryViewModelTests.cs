using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;
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

    private static LibraryViewModel CreateViewModel(
        ILibraryQueryService library,
        IAssetRevealService? reveal = null,
        ILibrarySortPreferenceService? sortPreferences = null,
        IMetadataEditorService? metadataService = null)
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
            preview);
    }

    private static MetadataEditorViewModel CreateEditor(
        IMetadataEditorService service) =>
        new(service, new BlockingDebounceScheduler());

    private sealed class RecordingLibraryQueryService : ILibraryQueryService
    {
        public IReadOnlyList<LibraryAssetDto> Result { get; } =
        [
            new LibraryAssetDto(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "photo.jpg",
                "杭州夜景",
                MediaKind.Image,
                DateTimeOffset.Parse("2026-08-22T10:00:00Z"))
        ];

        public LibraryQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<LibraryAssetDto>> SearchAsync(
            LibraryQuery query,
            CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(Result);
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
}
