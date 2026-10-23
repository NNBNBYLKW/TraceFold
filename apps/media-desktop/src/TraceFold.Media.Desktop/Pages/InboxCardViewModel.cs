using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Desktop.Pages;

public sealed class InboxCardViewModel : INotifyPropertyChanged
{
    private readonly Uri? _thumbnailUri;
    private ImageSource? _thumbnailSource;
    private long _metadataVersion;

    public InboxCardViewModel(
        InboxCardDto card,
        string? thumbnailSource = null)
    {
        Card = card;
        _metadataVersion = card.MetadataVersion;
        _thumbnailUri = thumbnailSource is null
            ? null
            : new Uri(thumbnailSource);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public InboxCardDto Card { get; private set; }

    public Guid Id => Card.Id;

    public long MetadataVersion => _metadataVersion;

    public InboxAction AllowedActions => Card.AllowedActions;

    public string AutomationId => $"InboxCard_{Id:N}";

    public string ActualFilename => Card.ActualFilename;

    public string DisplayName => Card.DisplayName;

    public string StatusText => Card.StatusText;

    /// <summary>
    /// Deterministic accessible name for the row. It carries the localized display name
    /// and status only: never a database identifier or an absolute path.
    /// </summary>
    public string AccessibleName => $"待入库文件 {DisplayName}，状态 {StatusText}";

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

    public InboxSelectionEntry SelectionEntry(int ordinal) =>
        new(Id, MetadataVersion, ordinal);

    public void UpdateMetadataVersion(long version)
    {
        if (version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        _metadataVersion = Math.Max(_metadataVersion, version);
    }

    /// <summary>
    /// Applies refreshed card data and notifies exactly the projections that changed, so a
    /// row keeps its AutomationId while its accessible name follows the new state.
    /// </summary>
    public void Update(InboxCardDto card)
    {
        ArgumentNullException.ThrowIfNull(card);
        string displayName = DisplayName;
        string status = StatusText;
        string actualFilename = ActualFilename;
        string accessibleName = AccessibleName;

        Card = card;
        _metadataVersion = Math.Max(_metadataVersion, card.MetadataVersion);

        RaiseIfChanged(nameof(DisplayName), displayName, DisplayName);
        RaiseIfChanged(nameof(StatusText), status, StatusText);
        RaiseIfChanged(nameof(ActualFilename), actualFilename, ActualFilename);
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
