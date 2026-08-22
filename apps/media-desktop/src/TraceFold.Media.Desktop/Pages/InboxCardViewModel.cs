using Microsoft.UI.Xaml;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Desktop.Pages;

public sealed class InboxCardViewModel
{
    public InboxCardViewModel(
        InboxCardDto card,
        string? thumbnailSource = null)
    {
        Card = card;
        ThumbnailSource = thumbnailSource;
    }

    public InboxCardDto Card { get; }

    public Guid Id => Card.Id;

    public string AutomationId => $"InboxCard_{Id:N}";

    public string ActualFilename => Card.ActualFilename;

    public string DisplayName => Card.DisplayName;

    public string StatusText => Card.StatusText;

    public InboxItemState State => Card.State;

    public string? ThumbnailSource { get; }

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
