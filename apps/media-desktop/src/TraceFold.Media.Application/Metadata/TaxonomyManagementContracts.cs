using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Application.Metadata;

public enum TaxonomyReferenceFilter
{
    All,
    Referenced,
    Unused,
}

public enum TaxonomySortField
{
    DisplayName,
    ReferenceCount,
}

public enum TaxonomySortDirection
{
    Ascending,
    Descending,
}

public sealed record TaxonomyManagementQuery(
    TaxonomyKind Kind,
    string? SearchText,
    TaxonomyReferenceFilter ReferenceFilter,
    TaxonomySortField SortField,
    TaxonomySortDirection SortDirection)
{
    public static TaxonomyManagementQuery Create(
        TaxonomyKind kind,
        string? searchText,
        TaxonomyReferenceFilter referenceFilter,
        TaxonomySortField sortField,
        TaxonomySortDirection sortDirection)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (!Enum.IsDefined(referenceFilter))
        {
            throw new ArgumentOutOfRangeException(nameof(referenceFilter));
        }

        if (!Enum.IsDefined(sortField))
        {
            throw new ArgumentOutOfRangeException(nameof(sortField));
        }

        if (!Enum.IsDefined(sortDirection))
        {
            throw new ArgumentOutOfRangeException(nameof(sortDirection));
        }

        return new TaxonomyManagementQuery(
            kind,
            string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim(),
            referenceFilter,
            sortField,
            sortDirection);
    }
}

public sealed record TaxonomyManagementTermDto(
    Guid Id,
    TaxonomyKind Kind,
    string DisplayName,
    string NormalizedName,
    DateTimeOffset UpdatedAtUtc,
    int InboxReferenceCount,
    int LibraryReferenceCount)
{
    public int ReferenceCount => checked(InboxReferenceCount + LibraryReferenceCount);
}

public interface ITaxonomyManagementQueryStore
{
    Task<IReadOnlyList<TaxonomyManagementTermDto>> QueryAsync(
        TaxonomyManagementQuery query,
        CancellationToken cancellationToken);
}

public interface ITaxonomyManagementQueryService
{
    Task<IReadOnlyList<TaxonomyManagementTermDto>> QueryAsync(
        TaxonomyManagementQuery query,
        CancellationToken cancellationToken);
}
