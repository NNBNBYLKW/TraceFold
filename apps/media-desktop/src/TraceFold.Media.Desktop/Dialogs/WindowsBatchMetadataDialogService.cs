using Microsoft.UI.Xaml;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Desktop.Services;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed class WindowsBatchMetadataDialogService(
    ITaxonomySearchService taxonomy,
    WindowContext windowContext) : IBatchMetadataDialogService
{
    public async Task<BatchMetadataDialogResult> ShowAsync(
        BatchMetadataEditorDto editor,
        IReadOnlyList<InboxSelectionEntry> selection,
        Func<BatchMetadataCommand, CancellationToken,
            Task<BatchMetadataResult>> submit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(submit);
        MainWindow window = windowContext.MainWindow
            ?? throw new InvalidOperationException("Main window is not available.");
        FrameworkElement content = window.Content as FrameworkElement
            ?? throw new InvalidOperationException(
                "Main window content is not available.");
        XamlRoot root = content.XamlRoot
            ?? throw new InvalidOperationException(
                "Main window XAML root is not available.");
        var viewModel = new BatchMetadataDialogViewModel(taxonomy);
        var dialog = new BatchMetadataDialog(
            viewModel,
            editor,
            selection,
            submit)
        {
            XamlRoot = root
        };
        return await dialog.ShowEditorAsync(cancellationToken);
    }
}
