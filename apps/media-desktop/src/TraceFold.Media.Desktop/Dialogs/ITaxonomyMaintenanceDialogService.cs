using TraceFold.Media.Application.Metadata;

namespace TraceFold.Media.Desktop.Dialogs;

public interface ITaxonomyMaintenanceDialogService
{
    Task<TaxonomyMaintenanceResult?> ShowMergeAsync(
        IReadOnlyList<TaxonomyManagementTermDto> selectedTerms,
        Guid? preselectedTargetId,
        CancellationToken cancellationToken);

    Task<bool> ConfirmDeleteAsync(
        TaxonomyDeletePreparation preparation,
        CancellationToken cancellationToken);
}
