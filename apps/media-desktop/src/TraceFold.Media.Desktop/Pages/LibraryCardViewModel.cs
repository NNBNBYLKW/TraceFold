using Microsoft.UI.Xaml;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;

namespace TraceFold.Media.Desktop.Pages;

public sealed class LibraryCardViewModel(
    LibraryAssetDto card,
    string? thumbnailSource = null)
{
    public LibraryAssetDto Card { get; } = card;

    public Guid Id => Card.Id;

    public string AutomationId => $"LibraryCard_{Id:N}";

    public string DisplayName => Card.DisplayName;

    public string ActualFilename => Card.ActualFilename;

    public string? ThumbnailSource { get; } = thumbnailSource;

    public Visibility ThumbnailVisibility => ThumbnailSource is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility FallbackVisibility => ThumbnailSource is null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string FallbackGlyph => Card.MediaKind == MediaKind.Image
        ? "\uEB9F"
        : "\uE714";
}
