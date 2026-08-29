using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Application.Metadata;

public sealed class TaxonomyService(
    ITaxonomyStore store,
    ITaxonomyMaintenanceService maintenance) : ITaxonomyService
{
    public Task<IReadOnlyList<TaxonomyTermDto>> SearchAsync(
        TaxonomyKind kind,
        string? query,
        CancellationToken cancellationToken) =>
        store.SearchAsync(kind, query?.Trim(), cancellationToken);

    public async Task<TaxonomyTermDto> CreateAsync(
        TaxonomyKind kind,
        string name,
        CancellationToken cancellationToken)
    {
        TaxonomyTermDto result = await store.CreateOrGetAsync(
            kind,
            TaxonomyName.Create(name),
            cancellationToken);
        maintenance.InvalidateUndo();
        return result;
    }

    public async Task<TaxonomyMutationResult> RenameAsync(
        Guid id,
        string name,
        CancellationToken cancellationToken)
    {
        TaxonomyMutationResult result = await store.RenameAsync(
            id,
            TaxonomyName.Create(name),
            cancellationToken);
        if (result.Succeeded)
        {
            maintenance.InvalidateUndo();
        }

        return result;
    }

    public async Task<TaxonomyMutationResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        TaxonomyMutationResult result = await store.DeleteAsync(
            id,
            cancellationToken);
        if (result.Succeeded)
        {
            maintenance.InvalidateUndo();
        }

        return result;
    }
}
