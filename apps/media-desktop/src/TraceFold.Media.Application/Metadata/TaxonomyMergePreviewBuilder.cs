namespace TraceFold.Media.Application.Metadata;

public static class TaxonomyMergePreviewBuilder
{
    public static TaxonomyMergePreviewDto Build(TaxonomyMergePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        TaxonomyMergePreviewRowDto[] rows = plan.Owners
            .Select(owner => new TaxonomyMergePreviewRowDto(
                owner.Owner,
                owner.VisibleName,
                owner.Before,
                owner.After))
            .ToArray();
        int inboxCount = rows.Count(row =>
            row.Owner.Kind == MetadataOwnerKind.InboxItem);
        int libraryCount = rows.Count(row =>
            row.Owner.Kind == MetadataOwnerKind.MediaAsset);
        return new TaxonomyMergePreviewDto(
            rows,
            inboxCount,
            libraryCount,
            CanApply: true,
            ErrorCode: null);
    }
}
