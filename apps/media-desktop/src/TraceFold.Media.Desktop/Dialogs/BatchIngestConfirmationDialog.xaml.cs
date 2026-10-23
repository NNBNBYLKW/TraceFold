using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TraceFold.Media.Application.Ingest;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed partial class BatchIngestConfirmationDialog : ContentDialog
{
    public BatchIngestConfirmationDialog(BatchIngestPreviewDto preview)
    {
        ViewModel = new(preview);
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnViewportSizeChanged;
    }

    public BatchIngestConfirmationViewModel ViewModel { get; }

    public bool Confirmed { get; private set; }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        UpdateViewportBounds();
        (ViewModel.CanConfirm ? ConfirmButton : CancelButton).Focus(FocusState.Programmatic);
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        UpdateViewportBounds();
    }

    private void UpdateViewportBounds()
    {
        if (XamlRoot is null) return;
        double width = Math.Max(320, XamlRoot.Size.Width - 112);
        double height = Math.Max(240, XamlRoot.Size.Height - 160);
        if (DialogRoot.MaxWidth != width) DialogRoot.MaxWidth = width;
        if (DialogRoot.MaxHeight != height) DialogRoot.MaxHeight = height;
    }

    public async Task<bool> ShowConfirmationAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenRegistration registration =
            cancellationToken.Register(() => DispatcherQueue.TryEnqueue(Hide));
        await ShowAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return Confirmed;
    }

    private void OnConfirmClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (ViewModel.CanConfirm)
        {
            Confirmed = true;
            Hide();
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        Hide();
    }
}
