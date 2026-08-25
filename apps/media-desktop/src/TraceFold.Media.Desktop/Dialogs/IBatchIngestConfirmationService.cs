using TraceFold.Media.Application.Ingest;

namespace TraceFold.Media.Desktop.Dialogs;

public interface IBatchIngestConfirmationService
{
    Task<bool> ConfirmAsync(
        BatchIngestPreviewDto preview,
        CancellationToken cancellationToken);
}
