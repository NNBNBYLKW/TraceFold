namespace TraceFold.Media.Application.Library;

public sealed class LibraryQueryService(ILibraryQueryStore store)
{
    public Task<IReadOnlyList<LibraryAssetDto>> SearchAsync(
        string? query,
        CancellationToken cancellationToken) =>
        store.SearchAsync(query?.Trim(), cancellationToken);

    public Task<LibraryAssetDetailsDto?> GetDetailsAsync(
        Guid assetId,
        CancellationToken cancellationToken) =>
        store.GetDetailsAsync(assetId, cancellationToken);
}
