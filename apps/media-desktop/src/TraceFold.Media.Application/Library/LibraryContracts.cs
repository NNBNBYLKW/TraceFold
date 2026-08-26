using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;

namespace TraceFold.Media.Application.Library;

public enum LibraryMediaFilter
{
    All,
    Image,
    Video
}

public enum LibraryDisplayNameFilter
{
    All,
    Customized,
    ActualFilename
}

public enum LibrarySortField
{
    CreatedAt,
    DisplayName,
    ActualFilename,
    MediaKind
}

public enum LibrarySortDirection
{
    Ascending,
    Descending
}

public sealed record LibraryQuery(
    string? SearchText,
    LibraryMediaFilter Media,
    IReadOnlyList<Guid> AuthorIds,
    Guid? SeriesId,
    IReadOnlyList<Guid> TagIds,
    bool MissingAuthors,
    bool MissingSeries,
    bool MissingTags,
    LibraryDisplayNameFilter DisplayName,
    LibrarySortField SortField,
    LibrarySortDirection SortDirection)
{
    public static LibraryQuery Default { get; } = Create(
        null,
        LibraryMediaFilter.All,
        [],
        null,
        [],
        false,
        false,
        false,
        LibraryDisplayNameFilter.All,
        LibrarySortField.CreatedAt,
        LibrarySortDirection.Descending);

    public static LibraryQuery Create(
        string? searchText,
        LibraryMediaFilter media,
        IReadOnlyList<Guid> authorIds,
        Guid? seriesId,
        IReadOnlyList<Guid> tagIds,
        bool missingAuthors,
        bool missingSeries,
        bool missingTags,
        LibraryDisplayNameFilter displayName,
        LibrarySortField sortField,
        LibrarySortDirection sortDirection)
    {
        ValidateEnum(media, nameof(media));
        ValidateEnum(displayName, nameof(displayName));
        ValidateEnum(sortField, nameof(sortField));
        ValidateEnum(sortDirection, nameof(sortDirection));

        IReadOnlyList<Guid> authors = CopyValidDistinct(authorIds, nameof(authorIds));
        IReadOnlyList<Guid> tags = CopyValidDistinct(tagIds, nameof(tagIds));

        if ((missingAuthors && authors.Count > 0)
            || (missingTags && tags.Count > 0)
            || (missingSeries && seriesId.HasValue))
        {
            throw new ArgumentException(
                "A missing filter cannot carry selected terms.");
        }

        if (seriesId == Guid.Empty)
        {
            throw new ArgumentException(
                "A persisted series id is required.",
                nameof(seriesId));
        }

        return new LibraryQuery(
            string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim(),
            media,
            authors,
            seriesId,
            tags,
            missingAuthors,
            missingSeries,
            missingTags,
            displayName,
            sortField,
            sortDirection);
    }

    private static IReadOnlyList<Guid> CopyValidDistinct(
        IReadOnlyList<Guid> ids,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(ids, parameterName);

        var seen = new HashSet<Guid>();
        var result = new List<Guid>(ids.Count);
        foreach (Guid id in ids)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException(
                    "A persisted taxonomy id is required.",
                    parameterName);
            }

            if (seen.Add(id))
            {
                result.Add(id);
            }
        }

        return result.AsReadOnly();
    }

    private static void ValidateEnum<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, null);
        }
    }
}

public sealed record LibraryAssetDto(
    Guid Id,
    string ActualFilename,
    string DisplayName,
    MediaKind MediaKind,
    DateTimeOffset CreatedAtUtc,
    long MetadataVersion = 0,
    bool HasCustomDisplayName = false);

public sealed record LibraryAssetDetailsDto(
    LibraryAssetDto Card,
    MetadataDocument Metadata,
    long Size,
    string Sha256);

public interface ILibraryQueryStore
{
    Task<IReadOnlyList<LibraryAssetDto>> SearchAsync(
        LibraryQuery query,
        CancellationToken cancellationToken);

    Task<LibraryAssetDetailsDto?> GetDetailsAsync(
        Guid assetId,
        CancellationToken cancellationToken);
}

public interface ILibraryQueryService
{
    Task<IReadOnlyList<LibraryAssetDto>> SearchAsync(
        LibraryQuery query,
        CancellationToken cancellationToken);

    Task<LibraryAssetDetailsDto?> GetDetailsAsync(
        Guid assetId,
        CancellationToken cancellationToken);
}
