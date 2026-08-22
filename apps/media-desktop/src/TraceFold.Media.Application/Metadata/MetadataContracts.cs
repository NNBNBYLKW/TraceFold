using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Application.Metadata;

public sealed record TaxonomyTermDto(
    Guid Id,
    TaxonomyKind Kind,
    string DisplayName,
    string NormalizedName,
    int ReferenceCount);

public sealed record MetadataUpdate(
    string? DisplayName,
    IReadOnlyList<Guid> AuthorIds,
    Guid? SeriesId,
    IReadOnlyList<Guid> TagIds,
    long ExpectedVersion);

public sealed record MetadataSaveResult(
    bool Succeeded,
    long CurrentVersion,
    string? ErrorCode);

public sealed record TaxonomyValidationResult(
    bool Succeeded,
    string? ErrorCode)
{
    public static TaxonomyValidationResult Success { get; } = new(true, null);
}

public sealed record TaxonomyMutationResult(
    bool Succeeded,
    string? ErrorCode);

public interface IMetadataStore
{
    Task<MetadataDocument?> LoadAsync(
        MetadataOwner owner,
        CancellationToken cancellationToken);

    Task<MetadataSaveResult> TrySaveAsync(
        MetadataOwner owner,
        MetadataSelection selection,
        long expectedVersion,
        CancellationToken cancellationToken);
}

public interface IMetadataEditorService
{
    Task<MetadataDocument?> LoadAsync(
        MetadataOwner owner,
        CancellationToken cancellationToken);

    Task<MetadataSaveResult> SaveAsync(
        MetadataOwner owner,
        MetadataUpdate update,
        CancellationToken cancellationToken);
}

public interface ITaxonomyLookup
{
    Task<TaxonomyValidationResult> ValidateAsync(
        MetadataSelection selection,
        CancellationToken cancellationToken);
}

public interface ITaxonomyStore
{
    Task<IReadOnlyList<TaxonomyTermDto>> SearchAsync(
        TaxonomyKind kind,
        string? query,
        CancellationToken cancellationToken);

    Task<TaxonomyTermDto> CreateOrGetAsync(
        TaxonomyKind kind,
        TaxonomyName name,
        CancellationToken cancellationToken);

    Task<TaxonomyMutationResult> RenameAsync(
        Guid id,
        TaxonomyName name,
        CancellationToken cancellationToken);

    Task<TaxonomyMutationResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken);
}

public interface ITaxonomyService
{
    Task<IReadOnlyList<TaxonomyTermDto>> SearchAsync(
        TaxonomyKind kind,
        string? query,
        CancellationToken cancellationToken);

    Task<TaxonomyTermDto> CreateAsync(
        TaxonomyKind kind,
        string name,
        CancellationToken cancellationToken);

    Task<TaxonomyMutationResult> RenameAsync(
        Guid id,
        string name,
        CancellationToken cancellationToken);

    Task<TaxonomyMutationResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken);
}
