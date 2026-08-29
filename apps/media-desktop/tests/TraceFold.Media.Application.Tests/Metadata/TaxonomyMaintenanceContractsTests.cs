using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Application.Tests.Metadata;

public sealed class TaxonomyMaintenanceContractsTests
{
    [Fact]
    public void Merge_selection_rejects_target_in_sources_and_empty_sources()
    {
        Guid target = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => TaxonomyMergeSelection.Create(
            TaxonomyKind.Author,
            [target],
            target));
        Assert.Throws<ArgumentException>(() => TaxonomyMergeSelection.Create(
            TaxonomyKind.Author,
            [],
            target));
    }

    [Fact]
    public void Merge_selection_rejects_duplicate_empty_and_unknown_values()
    {
        Guid source = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => TaxonomyMergeSelection.Create(
            TaxonomyKind.Tag,
            [source, source],
            Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => TaxonomyMergeSelection.Create(
            TaxonomyKind.Tag,
            [Guid.Empty],
            Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => TaxonomyMergeSelection.Create(
            (TaxonomyKind)99,
            [source],
            Guid.NewGuid()));
    }

    [Fact]
    public void Merge_selection_copies_source_ids()
    {
        Guid original = Guid.NewGuid();
        Guid replacement = Guid.NewGuid();
        Guid[] sourceIds = [original];

        TaxonomyMergeSelection selection = TaxonomyMergeSelection.Create(
            TaxonomyKind.Series,
            sourceIds,
            Guid.NewGuid());
        sourceIds[0] = replacement;

        Assert.Equal([original], selection.SourceIds);
    }

    [Fact]
    public void Delete_selection_requires_distinct_non_empty_ids()
    {
        Guid termId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => TaxonomyDeleteSelection.Create(
            TaxonomyKind.Author,
            []));
        Assert.Throws<ArgumentException>(() => TaxonomyDeleteSelection.Create(
            TaxonomyKind.Author,
            [termId, termId]));
        Assert.Throws<ArgumentException>(() => TaxonomyDeleteSelection.Create(
            TaxonomyKind.Author,
            [Guid.Empty]));
    }

    [Fact]
    public void Delete_preparation_requires_all_terms_and_no_blockers()
    {
        TaxonomyTermSnapshot term = Term(TaxonomyKind.Tag, "未使用");
        TaxonomyDeleteSelection selection = TaxonomyDeleteSelection.Create(
            TaxonomyKind.Tag,
            [term.Id]);

        var safe = new TaxonomyDeletePreparation(
            Guid.NewGuid(),
            selection,
            [term],
            []);
        var blocked = new TaxonomyDeletePreparation(
            Guid.NewGuid(),
            selection,
            [term],
            [new TaxonomyDeleteBlocker(term, 1, 0)]);

        Assert.True(safe.CanDelete);
        Assert.False(blocked.CanDelete);
    }

    private static TaxonomyTermSnapshot Term(TaxonomyKind kind, string name)
    {
        TaxonomyName taxonomyName = TaxonomyName.Create(name);
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-29T00:00:00Z");
        return new TaxonomyTermSnapshot(
            Guid.NewGuid(),
            kind,
            taxonomyName.Display,
            taxonomyName.Normalized,
            now,
            now);
    }
}
