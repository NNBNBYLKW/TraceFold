using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;

namespace TraceFold.Media.Desktop.Pages;

public sealed class LibraryCardViewModel : INotifyPropertyChanged
{
    private readonly Uri? _thumbnailUri;
    private ImageSource? _thumbnailSource;

    public LibraryCardViewModel(
        LibraryAssetDto card,
        string? thumbnailSource = null)
    {
        Card = card;
        _thumbnailUri = thumbnailSource is null
            ? null
            : new Uri(thumbnailSource);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LibraryAssetDto Card { get; private set; }

    public Guid Id => Card.Id;

    public string AutomationId => $"LibraryCard_{Id:N}";

    public string DisplayName => Card.DisplayName;

    public string ActualFilename => Card.ActualFilename;

    public long MetadataVersion => Card.MetadataVersion;

    public MediaKind MediaKind => Card.MediaKind;

    public string MediaKindText => Card.MediaKind == MediaKind.Image
        ? "图片"
        : "视频";

    /// <summary>
    /// Deterministic accessible name for the row. It carries the localized display name,
    /// the actual file name and the media kind only: never a database identifier or an
    /// absolute path.
    /// </summary>
    public string AccessibleName =>
        $"正式文件 {DisplayName}，实际文件 {ActualFilename}，{MediaKindText}";

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

    /// <summary>
    /// Applies refreshed asset data and notifies exactly the projections that changed, so a
    /// row keeps its AutomationId while its accessible name follows the new state.
    /// </summary>
    public void Update(LibraryAssetDto card)
    {
        ArgumentNullException.ThrowIfNull(card);
        string displayName = DisplayName;
        string actualFilename = ActualFilename;
        string mediaKindText = MediaKindText;
        string accessibleName = AccessibleName;

        Card = card;

        RaiseIfChanged(nameof(DisplayName), displayName, DisplayName);
        RaiseIfChanged(nameof(ActualFilename), actualFilename, ActualFilename);
        RaiseIfChanged(nameof(MediaKindText), mediaKindText, MediaKindText);
        RaiseIfChanged(nameof(AccessibleName), accessibleName, AccessibleName);
    }

    private void RaiseIfChanged(string propertyName, string before, string after)
    {
        if (!StringComparer.Ordinal.Equals(before, after))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
