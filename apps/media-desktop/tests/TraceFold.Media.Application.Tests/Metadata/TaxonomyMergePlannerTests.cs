using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Application.Tests.Metadata;

public sealed class TaxonomyMergePlannerTests
{
    private static readonly Guid Target = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SourceA = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid SourceB = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid Unrelated = Guid.Parse("30000000-0000-0000-0000-000000000001");

    [Fact]
    public void Author_plan_keeps_existing_target_position_and_dense_order()
    {
        TaxonomyMergePreparation preparation = Preparation(
            TaxonomyKind.Author,
            [Owner(
                MetadataOwnerKind.InboxItem,
                authors: [Unrelated, Target, SourceA, SourceB])]);

        TaxonomyMergePlan plan = TaxonomyMergePlanner.Build(preparation, null);

        Assert.Equal(
            [Unrelated, Target],
            Assert.Single(plan.Owners).After.AuthorIds);
    }

    [Fact]
    public void Author_plan_uses_earliest_source_position_when_target_is_absent()
    {
        TaxonomyMergePreparation preparation = Preparation(
            TaxonomyKind.Author,
            [Owner(
                MetadataOwnerKind.InboxItem,
                authors: [Unrelated, SourceB, SourceA])]);

        TaxonomyMergePlan plan = TaxonomyMergePlanner.Build(preparation, null);

        Assert.Equal(
            [Unrelated, Target],
            Assert.Single(plan.Owners).After.AuthorIds);
    }

    [Fact]
    public void Tag_plan_uses_the_same_ordering_rule_without_changing_authors()
    {
        TaxonomyMergePreparation preparation = Preparation(
            TaxonomyKind.Tag,
            [Owner(
                MetadataOwnerKind.MediaAsset,
                authors: [Unrelated],
                tags: [SourceA, Unrelated, SourceB])]);

        TaxonomyMergePlan plan = TaxonomyMergePlanner.Build(preparation, null);
        TaxonomyMergePlanOwner owner = Assert.Single(plan.Owners);

        Assert.Equal([Unrelated], owner.After.AuthorIds);
        Assert.Equal([Target, Unrelated], owner.After.TagIds);
    }

    [Fact]
    public void Series_plan_replaces_only_a_selected_source()
    {
        TaxonomyMergePreparation preparation = Preparation(
            TaxonomyKind.Series,
            [
                Owner(MetadataOwnerKind.InboxItem, seriesId: SourceA),
                Owner(MetadataOwnerKind.MediaAsset, seriesId: Unrelated),
            ]);

        TaxonomyMergePlan plan = TaxonomyMergePlanner.Build(preparation, null);

        TaxonomyMergePlanOwner changed = Assert.Single(plan.Owners);
        Assert.Equal(Target, changed.After.SeriesId);
        Assert.Equal(SourceA, changed.Before.SeriesId);
    }

    [Fact]
    public void Plan_deduplicates_the_same_affected_owner()
    {
        TaxonomyOwnerSnapshot owner = Owner(
            MetadataOwnerKind.InboxItem,
            authors: [SourceA, SourceB]);
        TaxonomyMergePreparation preparation = Preparation(
            TaxonomyKind.Author,
            [owner, owner]);

        TaxonomyMergePlan plan = TaxonomyMergePlanner.Build(preparation, null);

        Assert.Single(plan.Owners);
        Assert.Equal([Target], plan.Owners[0].After.AuthorIds);
    }

    [Fact]
    public void Final_name_may_reuse_a_source_normalized_name()
    {
        TaxonomyMergePreparation preparation = Preparation(
            TaxonomyKind.Author,
            [Owner(MetadataOwnerKind.InboxItem, authors: [SourceA])]);

        TaxonomyMergePlan plan = TaxonomyMergePlanner.Build(
            preparation,
            TaxonomyName.Create("Source A"));

        Assert.Equal("Source A", plan.FinalName.Display);
    }

    [Fact]
    public void Final_name_rejects_a_third_same_kind_owner()
    {
        TaxonomyMergePreparation preparation = Preparation(
            TaxonomyKind.Author,
            [Owner(MetadataOwnerKind.InboxItem, authors: [SourceA])],
            additionalNameOwners:
            [new TaxonomyNameOwnerSnapshot(
                TaxonomyName.Create("Occupied").Normalized,
                Unrelated)]);

        TaxonomyMaintenancePlanningException error = Assert.Throws<
            TaxonomyMaintenancePlanningException>(() =>
            TaxonomyMergePlanner.Build(
                preparation,
                TaxonomyName.Create("Occupied")));

        Assert.Equal(TaxonomyMaintenanceErrorCodes.NameConflict, error.ErrorCode);
    }

    [Fact]
    public void Plan_keeps_inbox_and_library_owners_distinct()
    {
        TaxonomyMergePreparation preparation = Preparation(
            TaxonomyKind.Tag,
            [
                Owner(MetadataOwnerKind.InboxItem, tags: [SourceA]),
                Owner(MetadataOwnerKind.MediaAsset, tags: [SourceB]),
            ]);

        TaxonomyMergePlan plan = TaxonomyMergePlanner.Build(preparation, null);

        Assert.Equal(2, plan.Owners.Count);
        Assert.Contains(plan.Owners, owner =>
            owner.Owner.Kind == MetadataOwnerKind.InboxItem);
        Assert.Contains(plan.Owners, owner =>
            owner.Owner.Kind == MetadataOwnerKind.MediaAsset);
    }

    private static TaxonomyMergePreparation Preparation(
        TaxonomyKind kind,
        IReadOnlyList<TaxonomyOwnerSnapshot> owners,
        IReadOnlyList<TaxonomyNameOwnerSnapshot>? additionalNameOwners = null)
    {
        TaxonomyTermSnapshot target = Term(Target, kind, "Target");
        TaxonomyTermSnapshot sourceA = Term(SourceA, kind, "Source A");
        TaxonomyTermSnapshot sourceB = Term(SourceB, kind, "Source B");
        TaxonomyNameOwnerSnapshot[] names =
        [
            new(target.NormalizedName, target.Id),
            new(sourceA.NormalizedName, sourceA.Id),
            new(sourceB.NormalizedName, sourceB.Id),
            .. additionalNameOwners ?? [],
        ];
        return new TaxonomyMergePreparation(
            Guid.NewGuid(),
            TaxonomyMergeSelection.Create(kind, [SourceA, SourceB], Target),
            target,
            [sourceA, sourceB],
            names,
            owners);
    }

    private static TaxonomyOwnerSnapshot Owner(
        MetadataOwnerKind kind,
        IReadOnlyList<Guid>? authors = null,
        Guid? seriesId = null,
        IReadOnlyList<Guid>? tags = null) =>
        new(
            new MetadataOwner(kind, Guid.NewGuid()),
            kind == MetadataOwnerKind.InboxItem ? "待入库文件" : "正式文件",
            Version: 5,
            new TaxonomyAssignmentSnapshot(
                authors ?? [],
                seriesId,
                tags ?? []));

    private static TaxonomyTermSnapshot Term(
        Guid id,
        TaxonomyKind kind,
        string displayName)
    {
        TaxonomyName name = TaxonomyName.Create(displayName);
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-29T00:00:00Z");
        return new TaxonomyTermSnapshot(
            id,
            kind,
            name.Display,
            name.Normalized,
            now,
            now);
    }
}
