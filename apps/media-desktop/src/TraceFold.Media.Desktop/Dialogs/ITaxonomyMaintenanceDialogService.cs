using TraceFold.Media.Application.Metadata;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed record TaxonomyMergeDialogResult(
    Guid TargetId,
    TaxonomyMaintenanceResult MaintenanceResult);

public interface ITaxonomyMaintenanceDialogService
{
    Task<TaxonomyMergeDialogResult?> ShowMergeAsync(
        IReadOnlyList<TaxonomyManagementTermDto> selectedTerms,
        Guid? preselectedTargetId,
        CancellationToken cancellationToken);

    Task<bool> ConfirmDeleteAsync(
        TaxonomyDeletePreparation preparation,
        CancellationToken cancellationToken);
}
