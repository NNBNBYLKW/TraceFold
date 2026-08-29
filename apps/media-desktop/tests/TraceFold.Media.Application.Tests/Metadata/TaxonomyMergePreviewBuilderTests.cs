using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Application.Tests.Metadata;

public sealed class TaxonomyMergePreviewBuilderTests
{
    [Fact]
    public void Preview_preserves_rows_and_splits_owner_counts()
    {
        TaxonomyAssignmentSnapshot inboxBefore = Assignment(Guid.NewGuid());
        TaxonomyAssignmentSnapshot libraryBefore = Assignment(Guid.NewGuid());
        TaxonomyAssignmentSnapshot after = Assignment(Guid.NewGuid());
        var owners = new TaxonomyMergePlanOwner[]
        {
            new(
                new MetadataOwner(MetadataOwnerKind.InboxItem, Guid.NewGuid()),
                "待入库文件",
                1,
                inboxBefore,
                after),
            new(
                new MetadataOwner(MetadataOwnerKind.MediaAsset, Guid.NewGuid()),
                "正式文件",
                2,
                libraryBefore,
                after),
        };
        TaxonomyMergePlan plan = Plan(owners);

        TaxonomyMergePreviewDto preview = TaxonomyMergePreviewBuilder.Build(plan);

        Assert.True(preview.CanApply);
        Assert.Null(preview.ErrorCode);
        Assert.Equal(1, preview.InboxAffectedCount);
        Assert.Equal(1, preview.LibraryAffectedCount);
        Assert.Equal(2, preview.Rows.Count);
        Assert.Same(inboxBefore, preview.Rows[0].Before);
        Assert.Same(after, preview.Rows[0].After);
    }

    [Fact]
    public void Preview_allows_a_valid_term_only_merge_with_no_owners()
    {
        TaxonomyMergePreviewDto preview = TaxonomyMergePreviewBuilder.Build(
            Plan([]));

        Assert.True(preview.CanApply);
        Assert.Empty(preview.Rows);
        Assert.Equal(0, preview.InboxAffectedCount);
        Assert.Equal(0, preview.LibraryAffectedCount);
    }

    private static TaxonomyMergePlan Plan(
        IReadOnlyList<TaxonomyMergePlanOwner> owners)
    {
        TaxonomyTermSnapshot target = Term("Target");
        return new TaxonomyMergePlan(
            Guid.NewGuid(),
            TaxonomyKind.Author,
            target,
            TaxonomyName.Create(target.DisplayName),
            [Term("Source")],
            owners);
    }

    private static TaxonomyTermSnapshot Term(string displayName)
    {
        TaxonomyName name = TaxonomyName.Create(displayName);
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-29T00:00:00Z");
        return new TaxonomyTermSnapshot(
            Guid.NewGuid(),
            TaxonomyKind.Author,
            name.Display,
            name.Normalized,
            now,
            now);
    }

    private static TaxonomyAssignmentSnapshot Assignment(Guid authorId) =>
        new([authorId], null, []);
}
