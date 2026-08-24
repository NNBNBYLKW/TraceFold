using TraceFold.Media.Application.Ingest;

namespace TraceFold.Media.Desktop.Dialogs;

public interface IConflictDialogService
{
    Task<ConflictDialogResult?> ShowAsync(
        ConflictCaseDto conflict,
        CancellationToken cancellationToken);
}
