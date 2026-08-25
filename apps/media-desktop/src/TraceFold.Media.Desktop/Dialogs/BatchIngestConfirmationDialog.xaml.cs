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
    }

    public BatchIngestConfirmationViewModel ViewModel { get; }

    public bool Confirmed { get; private set; }

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
