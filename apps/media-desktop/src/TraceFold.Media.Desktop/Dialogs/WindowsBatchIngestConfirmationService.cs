using Microsoft.UI.Xaml;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Desktop.Services;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed class WindowsBatchIngestConfirmationService(
    WindowContext windowContext) : IBatchIngestConfirmationService
{
    public async Task<bool> ConfirmAsync(
        BatchIngestPreviewDto preview,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        MainWindow window = windowContext.MainWindow
            ?? throw new InvalidOperationException("Main window is not available.");
        FrameworkElement content = window.Content as FrameworkElement
            ?? throw new InvalidOperationException(
                "Main window content is not available.");
        XamlRoot root = content.XamlRoot
            ?? throw new InvalidOperationException(
                "Main window XAML root is not available.");
        var dialog = new BatchIngestConfirmationDialog(preview)
        {
            XamlRoot = root
        };
        return await dialog.ShowConfirmationAsync(cancellationToken);
    }
}
