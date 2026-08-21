namespace TraceFold.Media.Domain.Metadata;

public sealed record MetadataSelection
{
    private MetadataSelection(
        string? displayName,
        IReadOnlyList<Guid> authorIds,
        Guid? seriesId,
        IReadOnlyList<Guid> tagIds)
    {
        DisplayName = displayName;
        AuthorIds = authorIds;
        SeriesId = seriesId;
        TagIds = tagIds;
    }

    public string? DisplayName { get; }

    public IReadOnlyList<Guid> AuthorIds { get; }

    public Guid? SeriesId { get; }

    public IReadOnlyList<Guid> TagIds { get; }

    public static MetadataSelection Create(
        string? displayName,
        IEnumerable<Guid> authorIds,
        Guid? seriesId,
        IEnumerable<Guid> tagIds)
    {
        ArgumentNullException.ThrowIfNull(authorIds);
        ArgumentNullException.ThrowIfNull(tagIds);

        string? trimmedDisplayName = displayName?.Trim();
        if (string.IsNullOrEmpty(trimmedDisplayName))
        {
            trimmedDisplayName = null;
        }

        return new MetadataSelection(
            trimmedDisplayName,
            authorIds.Distinct().ToArray(),
            seriesId,
            tagIds.Distinct().ToArray());
    }
}
