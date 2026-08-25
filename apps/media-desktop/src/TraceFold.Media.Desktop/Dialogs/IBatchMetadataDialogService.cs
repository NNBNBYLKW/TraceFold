using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed record BatchMetadataDialogResult(
    bool Applied,
    BatchMetadataResult? Result);

public interface IBatchMetadataDialogService
{
    Task<BatchMetadataDialogResult> ShowAsync(
        BatchMetadataEditorDto editor,
        IReadOnlyList<InboxSelectionEntry> selection,
        Func<BatchMetadataCommand, CancellationToken,
            Task<BatchMetadataResult>> submit,
        CancellationToken cancellationToken);
}
