using CommunityToolkit.Mvvm.ComponentModel;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Application.Preview;

namespace TraceFold.Media.Desktop.Pages;

public sealed partial class LibraryInspectorViewModel(
    MetadataEditorViewModel metadata,
    IMediaPreviewResolver previewResolver) : ObservableObject
{
    [ObservableProperty]
    public partial Guid? AssetId { get; private set; }

    [ObservableProperty]
    public partial string ActualFilename { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial PreviewDescriptor? Preview { get; private set; }

    [ObservableProperty]
    public partial string TechnicalSummary { get; private set; } = string.Empty;

    public MetadataEditorViewModel Metadata { get; } = metadata;

    public async Task LoadAsync(
        LibraryAssetDetailsDto details,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        var owner = new MetadataOwner(MetadataOwnerKind.MediaAsset, details.Card.Id);
        await Metadata.LoadAsync(owner, cancellationToken);
        Preview = await previewResolver.ResolveAsync(owner, cancellationToken);
        AssetId = details.Card.Id;
        ActualFilename = details.Card.ActualFilename;
        TechnicalSummary = $"{FormatSize(details.Size)} · SHA-256 {details.Sha256[..8]}…";
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await Metadata.FlushAsync(cancellationToken);
        AssetId = null;
        ActualFilename = string.Empty;
        Preview = null;
        TechnicalSummary = string.Empty;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.##} GB",
        >= 1024 * 1024 => $"{bytes / (1024d * 1024):0.##} MB",
        >= 1024 => $"{bytes / 1024d:0.##} KB",
        _ => $"{bytes} B"
    };
}
