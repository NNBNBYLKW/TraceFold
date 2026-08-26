using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;

namespace TraceFold.Media.Desktop.Pages;

public sealed class LibraryCardViewModel(
    LibraryAssetDto card,
    string? thumbnailSource = null)
{
    private readonly Uri? _thumbnailUri = thumbnailSource is null
        ? null
        : new Uri(thumbnailSource);
    private ImageSource? _thumbnailSource;

    public LibraryAssetDto Card { get; } = card;

    public Guid Id => Card.Id;

    public string AutomationId => $"LibraryCard_{Id:N}";

    public string DisplayName => Card.DisplayName;

    public string ActualFilename => Card.ActualFilename;

    public long MetadataVersion => Card.MetadataVersion;

    public MediaKind MediaKind => Card.MediaKind;

    public LibrarySelectionEntry SelectionEntry(int ordinal) =>
        new(Id, MetadataVersion, ordinal);

    public ImageSource? ThumbnailSource => _thumbnailUri is null
        ? null
        : _thumbnailSource ??= new BitmapImage(_thumbnailUri);

    public Visibility ThumbnailVisibility => _thumbnailUri is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility FallbackVisibility => _thumbnailUri is null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string FallbackGlyph => Card.MediaKind == MediaKind.Image
        ? "\uEB9F"
        : "\uE714";
}
