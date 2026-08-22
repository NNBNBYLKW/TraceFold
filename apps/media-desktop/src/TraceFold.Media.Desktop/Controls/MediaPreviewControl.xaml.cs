using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Preview;
using Windows.Media.Core;

namespace TraceFold.Media.Desktop.Controls;

public sealed partial class MediaPreviewControl : UserControl
{
    public static readonly DependencyProperty DescriptorProperty =
        DependencyProperty.Register(
            nameof(Descriptor),
            typeof(PreviewDescriptor),
            typeof(MediaPreviewControl),
            new PropertyMetadata(null, OnDescriptorChanged));

    public MediaPreviewControl()
    {
        InitializeComponent();
        VideoPreview.MediaPlayer.MediaFailed += OnMediaFailed;
        Unloaded += OnUnloaded;
    }

    public PreviewDescriptor? Descriptor
    {
        get => (PreviewDescriptor?)GetValue(DescriptorProperty);
        set => SetValue(DescriptorProperty, value);
    }

    public void Stop()
    {
        VideoPreview.MediaPlayer.Pause();
        VideoPreview.Source = null;
    }

    private static void OnDescriptorChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args)
    {
        _ = args;
        ((MediaPreviewControl)sender).ApplyDescriptor();
    }

    private void ApplyDescriptor()
    {
        Stop();
        ImagePreview.Source = null;
        ImagePreview.Visibility = Visibility.Collapsed;
        VideoPreview.Visibility = Visibility.Collapsed;
        FailureMessage.Visibility = Visibility.Collapsed;
        FallbackPanel.Visibility = Visibility.Visible;
        PreviewDescriptor? descriptor = Descriptor;
        FallbackIcon.Glyph = descriptor?.MediaKind == MediaKind.Video
            ? "\uE714"
            : "\uEB9F";
        if (descriptor is null)
        {
            return;
        }

        if (!descriptor.Exists)
        {
            ShowFailure();
            return;
        }

        try
        {
            var uri = new Uri(descriptor.TrustedFullPath);
            FallbackPanel.Visibility = Visibility.Collapsed;
            if (descriptor.MediaKind == MediaKind.Image)
            {
                ImagePreview.Source = new BitmapImage(uri);
                ImagePreview.Visibility = Visibility.Visible;
            }
            else
            {
                VideoPreview.Source = MediaSource.CreateFromUri(uri);
                VideoPreview.Visibility = Visibility.Visible;
            }
        }
        catch (Exception)
        {
            ShowFailure();
        }
    }

    private void OnImageFailed(object sender, ExceptionRoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        ShowFailure();
    }

    private void OnMediaFailed(
        Windows.Media.Playback.MediaPlayer sender,
        Windows.Media.Playback.MediaPlayerFailedEventArgs args)
    {
        _ = sender;
        _ = args;
        DispatcherQueue.TryEnqueue(ShowFailure);
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        Stop();
    }

    private void ShowFailure()
    {
        Stop();
        ImagePreview.Source = null;
        ImagePreview.Visibility = Visibility.Collapsed;
        VideoPreview.Visibility = Visibility.Collapsed;
        FallbackPanel.Visibility = Visibility.Visible;
        FailureMessage.Visibility = Visibility.Visible;
    }
}
