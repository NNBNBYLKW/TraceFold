namespace TraceFold.Media.Application.Metadata;

public sealed class TaxonomyManagementQueryService : ITaxonomyManagementQueryService
{
    private readonly ITaxonomyManagementQueryStore _store;

    public TaxonomyManagementQueryService(ITaxonomyManagementQueryStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public Task<IReadOnlyList<TaxonomyManagementTermDto>> QueryAsync(
        TaxonomyManagementQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _store.QueryAsync(query, cancellationToken);
    }
}
