using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Application.Metadata;

public sealed class TaxonomyService(ITaxonomyStore store)
{
    public Task<IReadOnlyList<TaxonomyTermDto>> SearchAsync(
        TaxonomyKind kind,
        string? query,
        CancellationToken cancellationToken) =>
        store.SearchAsync(kind, query?.Trim(), cancellationToken);

    public Task<TaxonomyTermDto> CreateAsync(
        TaxonomyKind kind,
        string name,
        CancellationToken cancellationToken) =>
        store.CreateOrGetAsync(kind, TaxonomyName.Create(name), cancellationToken);

    public Task<TaxonomyMutationResult> RenameAsync(
        Guid id,
        string name,
        CancellationToken cancellationToken) =>
        store.RenameAsync(id, TaxonomyName.Create(name), cancellationToken);

    public Task<TaxonomyMutationResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        store.DeleteAsync(id, cancellationToken);
}
