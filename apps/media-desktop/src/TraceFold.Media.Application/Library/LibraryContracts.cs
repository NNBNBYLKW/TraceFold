using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;

namespace TraceFold.Media.Application.Library;

public sealed record LibraryAssetDto(
    Guid Id,
    string ActualFilename,
    string DisplayName,
    MediaKind MediaKind,
    DateTimeOffset CreatedAtUtc);

public sealed record LibraryAssetDetailsDto(
    LibraryAssetDto Card,
    MetadataDocument Metadata,
    long Size,
    string Sha256);

public interface ILibraryQueryStore
{
    Task<IReadOnlyList<LibraryAssetDto>> SearchAsync(
        string? query,
        CancellationToken cancellationToken);

    Task<LibraryAssetDetailsDto?> GetDetailsAsync(
        Guid assetId,
        CancellationToken cancellationToken);
}
