using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed record ConflictDialogResult(
    ConflictResolution Resolution,
    Guid? SelectedMatchId);
