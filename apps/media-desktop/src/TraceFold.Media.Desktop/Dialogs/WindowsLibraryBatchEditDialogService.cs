using Microsoft.UI.Xaml;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Desktop.Services;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed class WindowsLibraryBatchEditDialogService(
    ILibraryBatchEditorService editorService,
    ILibraryBatchPreviewService previewService,
    INamingPresetService presetService,
    ITaxonomySearchService taxonomy,
    WindowContext windowContext) : ILibraryBatchEditDialogService
{
    public async Task<LibraryBatchEditDialogResult> ShowAsync(
        IReadOnlyList<LibrarySelectionEntry> selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Count < 2)
        {
            return Failure(
                selection.Count,
                "library.batch.selection_invalid",
                editorService.UndoState.IsAvailable);
        }

        LibraryBatchPrepareResult prepared = await editorService.PrepareAsync(
            selection,
            cancellationToken);
        if (!prepared.Succeeded || prepared.Editor is null)
        {
            return Failure(
                selection.Count,
                prepared.ErrorCode ?? "library.batch.prepare_failed",
                editorService.UndoState.IsAvailable);
        }

        MainWindow window = windowContext.MainWindow
            ?? throw new InvalidOperationException("Main window is not available.");
        FrameworkElement content = window.Content as FrameworkElement
            ?? throw new InvalidOperationException(
                "Main window content is not available.");
        XamlRoot root = content.XamlRoot
            ?? throw new InvalidOperationException(
                "Main window XAML root is not available.");
        var viewModel = new LibraryBatchEditDialogViewModel(
            editorService,
            previewService,
            presetService,
            taxonomy,
            prepared.Editor);
        var dialog = new LibraryBatchEditDialog(viewModel)
        {
            XamlRoot = root
        };
        return await dialog.ShowEditorAsync(cancellationToken);
    }

    private static LibraryBatchEditDialogResult Failure(
        int selectionCount,
        string errorCode,
        bool canUndo) =>
        new(
            false,
            new LibraryBatchApplyResult(
                false,
                0,
                selectionCount,
                0,
                canUndo,
                errorCode));
}
