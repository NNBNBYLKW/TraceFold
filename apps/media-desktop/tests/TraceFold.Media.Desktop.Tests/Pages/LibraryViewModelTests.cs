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
        MetadataEditorViewModel metadata = CreateEditor(metadataGateway);
        var viewModel = new LibraryViewModel(
            library,
            new LibraryInspectorViewModel(metadata, new FixedPreviewResolver()),
            new FakeRevealService(),
            new FixedPreviewResolver());

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
        var viewModel = new LibraryViewModel(
            library,
            new LibraryInspectorViewModel(
                CreateEditor(new RecordingMetadataGateway()),
                new FixedPreviewResolver()),
            reveal,
            new FixedPreviewResolver());
        await viewModel.ApplySearchAsync(TestContext.Current.CancellationToken);
        await viewModel.SelectAsync(
            viewModel.Items[0],
            TestContext.Current.CancellationToken);

        await viewModel.RevealSelectedCommand.ExecuteAsync(null);

        Assert.Equal(library.Result[0].Id, reveal.RevealedId);
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

    private sealed class BlockingDebounceScheduler : IDebounceScheduler
    {
        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
