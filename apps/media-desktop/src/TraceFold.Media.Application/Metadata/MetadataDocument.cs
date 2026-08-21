namespace TraceFold.Media.Application.Metadata;

public sealed record MetadataDocument(
    MetadataOwner Owner,
    string? DisplayName,
    IReadOnlyList<TaxonomyTermDto> Authors,
    TaxonomyTermDto? Series,
    IReadOnlyList<TaxonomyTermDto> Tags,
    long Version);
