using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Application.Metadata;

public static class BatchMetadataMutation
{
    public static MetadataSelection Apply(
        MetadataSelection current,
        ResolvedBatchMetadataEdits edits)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(edits);
        IReadOnlyList<Guid> authors = ApplySet(current.AuthorIds, edits.Authors);
        Guid? series = edits.Series.Operation switch
        {
            BatchMetadataFieldOperation.Unchanged => current.SeriesId,
            BatchMetadataFieldOperation.Replace => edits.Series.Value,
            BatchMetadataFieldOperation.Clear => null,
            _ => throw new ArgumentException("Invalid series operation.")
        };
        IReadOnlyList<Guid> tags = ApplySet(current.TagIds, edits.Tags);
        if (authors.SequenceEqual(current.AuthorIds)
            && series == current.SeriesId
            && tags.SequenceEqual(current.TagIds))
        {
            return current;
        }

        return MetadataSelection.Create(current.DisplayName, authors, series, tags);
    }

    private static IReadOnlyList<Guid> ApplySet(
        IReadOnlyList<Guid> current,
        ResolvedBatchSetEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(edit.Values);
        return edit.Operation switch
        {
            BatchMetadataFieldOperation.Unchanged => current,
            BatchMetadataFieldOperation.Add => DistinctInOrder(
                current.Concat(edit.Values)),
            BatchMetadataFieldOperation.Remove => Remove(current, edit.Values),
            BatchMetadataFieldOperation.Replace => DistinctInOrder(edit.Values),
            BatchMetadataFieldOperation.Clear => [],
            _ => throw new ArgumentException("Invalid set operation.")
        };
    }

    private static IReadOnlyList<Guid> Remove(
        IReadOnlyList<Guid> current,
        IReadOnlyList<Guid> values)
    {
        var removed = new HashSet<Guid>(values);
        return current.Where(value => !removed.Contains(value)).ToArray();
    }

    private static IReadOnlyList<Guid> DistinctInOrder(IEnumerable<Guid> values)
    {
        var seen = new HashSet<Guid>();
        return values.Where(seen.Add).ToArray();
    }
}
