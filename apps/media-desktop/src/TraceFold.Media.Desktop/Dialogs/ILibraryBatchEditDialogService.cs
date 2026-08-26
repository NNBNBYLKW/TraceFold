using TraceFold.Media.Application.Library;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed record LibraryBatchEditDialogResult(
    bool Applied,
    LibraryBatchApplyResult? Result);

public interface ILibraryBatchEditDialogService
{
    Task<LibraryBatchEditDialogResult> ShowAsync(
        IReadOnlyList<LibrarySelectionEntry> selection,
        CancellationToken cancellationToken);
}
