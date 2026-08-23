using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Desktop.Pages;

public sealed class InboxCardViewModel
{
    private readonly Uri? _thumbnailUri;
    private ImageSource? _thumbnailSource;

    public InboxCardViewModel(
        InboxCardDto card,
        string? thumbnailSource = null)
    {
        Card = card;
        _thumbnailUri = thumbnailSource is null
            ? null
            : new Uri(thumbnailSource);
    }

    public InboxCardDto Card { get; }

    public Guid Id => Card.Id;

    public string AutomationId => $"InboxCard_{Id:N}";

    public string ActualFilename => Card.ActualFilename;

    public string DisplayName => Card.DisplayName;

    public string StatusText => Card.StatusText;

    public string ProgressText => Card.Processing is null
        ? string.Empty
        : State == InboxItemState.ProcessingQueued
            ? "排队中"
            : $"{Card.Processing.StepText} {Card.Processing.ProgressPercent}%";

    public double ProcessingProgress => Card.Processing?.ProgressPercent ?? 0;

    public Visibility ProcessingVisibility => State is
        InboxItemState.ProcessingQueued or InboxItemState.Processing
            ? Visibility.Visible
            : Visibility.Collapsed;

    public InboxItemState State => Card.State;

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
