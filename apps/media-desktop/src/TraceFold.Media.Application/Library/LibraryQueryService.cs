namespace TraceFold.Media.Application.Library;

public sealed class LibraryQueryService(ILibraryQueryStore store) : ILibraryQueryService
{
    public Task<IReadOnlyList<LibraryAssetDto>> SearchAsync(
        LibraryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return store.SearchAsync(query, cancellationToken);
    }

    public Task<LibraryAssetDetailsDto?> GetDetailsAsync(
        Guid assetId,
        CancellationToken cancellationToken) =>
        store.GetDetailsAsync(assetId, cancellationToken);
}
