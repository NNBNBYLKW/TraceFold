using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Application.Tests.Metadata;

public sealed class BatchMetadataMutationTests
{
    [Fact]
    public void Add_preserves_existing_order_and_appends_only_new_ids()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        MetadataSelection current = MetadataSelection.Create(null, [a], null, []);
        var edits = new ResolvedBatchMetadataEdits(
            new(BatchMetadataFieldOperation.Add, [a, b]),
            ResolvedBatchSeriesEdit.Unchanged,
            ResolvedBatchSetEdit.Unchanged);

        MetadataSelection changed = BatchMetadataMutation.Apply(current, edits);

        Assert.Equal([a, b], changed.AuthorIds);
    }

    [Fact]
    public void Remove_absent_ids_is_a_no_op_and_does_not_reorder()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        MetadataSelection current = MetadataSelection.Create(null, [a, b], null, []);
        var edits = new ResolvedBatchMetadataEdits(
            new(BatchMetadataFieldOperation.Remove, [Guid.NewGuid()]),
            ResolvedBatchSeriesEdit.Unchanged,
            ResolvedBatchSetEdit.Unchanged);

        Assert.Same(current, BatchMetadataMutation.Apply(current, edits));
    }

    [Fact]
    public void Replace_deduplicates_in_first_occurrence_order()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        MetadataSelection current = MetadataSelection.Create(null, [], null, []);
        var edits = new ResolvedBatchMetadataEdits(
            ResolvedBatchSetEdit.Unchanged,
            ResolvedBatchSeriesEdit.Unchanged,
            new(BatchMetadataFieldOperation.Replace, [second, first, second]));

        MetadataSelection changed = BatchMetadataMutation.Apply(current, edits);

        Assert.Equal([second, first], changed.TagIds);
    }

    [Fact]
    public void Replace_can_explicitly_clear_series_without_touching_other_fields()
    {
        Guid author = Guid.NewGuid();
        MetadataSelection current = MetadataSelection.Create(
            null, [author], Guid.NewGuid(), [Guid.NewGuid()]);
        var edits = new ResolvedBatchMetadataEdits(
            ResolvedBatchSetEdit.Unchanged,
            new(BatchMetadataFieldOperation.Clear, null),
            ResolvedBatchSetEdit.Unchanged);

        MetadataSelection changed = BatchMetadataMutation.Apply(current, edits);

        Assert.Null(changed.SeriesId);
        Assert.Equal(current.AuthorIds, changed.AuthorIds);
        Assert.Equal(current.TagIds, changed.TagIds);
    }

    [Theory]
    [InlineData(BatchMetadataMode.Add, BatchMetadataFieldOperation.Replace)]
    [InlineData(BatchMetadataMode.Remove, BatchMetadataFieldOperation.Clear)]
    public void Command_rejects_operations_outside_the_selected_action_tab(
        BatchMetadataMode mode,
        BatchMetadataFieldOperation operation)
    {
        Assert.Throws<ArgumentException>(() => BatchMetadataCommand.Create(
            [Selection()],
            mode,
            new BatchMetadataSetEdit(operation, []),
            BatchMetadataSeriesEdit.Unchanged,
            BatchMetadataSetEdit.Unchanged));
    }

    [Fact]
    public void Command_rejects_duplicate_selection_identity_or_ordinal()
    {
        Guid id = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => BatchMetadataCommand.Create(
            [new(id, 0, 0), new(id, 0, 1)],
            BatchMetadataMode.Add,
            AddAuthors(),
            BatchMetadataSeriesEdit.Unchanged,
            BatchMetadataSetEdit.Unchanged));
        Assert.Throws<ArgumentException>(() => BatchMetadataCommand.Create(
            [new(Guid.NewGuid(), 0, 0), new(Guid.NewGuid(), 0, 0)],
            BatchMetadataMode.Add,
            AddAuthors(),
            BatchMetadataSeriesEdit.Unchanged,
            BatchMetadataSetEdit.Unchanged));
    }

    [Fact]
    public void Command_rejects_invalid_selection_and_taxonomy_kinds()
    {
        Assert.Throws<ArgumentException>(() => BatchMetadataCommand.Create(
            [],
            BatchMetadataMode.Add,
            AddAuthors(),
            BatchMetadataSeriesEdit.Unchanged,
            BatchMetadataSetEdit.Unchanged));
        Assert.Throws<ArgumentException>(() => BatchMetadataCommand.Create(
            [new(Guid.NewGuid(), -1, 0)],
            BatchMetadataMode.Add,
            AddAuthors(),
            BatchMetadataSeriesEdit.Unchanged,
            BatchMetadataSetEdit.Unchanged));
        Assert.Throws<ArgumentException>(() => BatchMetadataCommand.Create(
            [Selection()],
            BatchMetadataMode.Add,
            new(
                BatchMetadataFieldOperation.Add,
                [BatchTaxonomyReference.Existing(TaxonomyKind.Tag, Guid.NewGuid())]),
            BatchMetadataSeriesEdit.Unchanged,
            BatchMetadataSetEdit.Unchanged));
    }

    [Fact]
    public void Command_rejects_series_changes_outside_replace_and_new_remove_values()
    {
        Assert.Throws<ArgumentException>(() => BatchMetadataCommand.Create(
            [Selection()],
            BatchMetadataMode.Add,
            AddAuthors(),
            new(
                BatchMetadataFieldOperation.Replace,
                BatchTaxonomyReference.Existing(TaxonomyKind.Series, Guid.NewGuid())),
            BatchMetadataSetEdit.Unchanged));
        Assert.Throws<ArgumentException>(() => BatchMetadataCommand.Create(
            [Selection()],
            BatchMetadataMode.Remove,
            new(
                BatchMetadataFieldOperation.Remove,
                [BatchTaxonomyReference.New(TaxonomyKind.Author, "作者甲")]),
            BatchMetadataSeriesEdit.Unchanged,
            BatchMetadataSetEdit.Unchanged));
    }

    [Fact]
    public void Replace_requires_at_least_one_changed_field()
    {
        Assert.Throws<ArgumentException>(() => BatchMetadataCommand.Create(
            [Selection()],
            BatchMetadataMode.Replace,
            BatchMetadataSetEdit.Unchanged,
            BatchMetadataSeriesEdit.Unchanged,
            BatchMetadataSetEdit.Unchanged));
    }

    private static InboxSelectionEntry Selection() =>
        new(Guid.NewGuid(), 0, 0);

    private static BatchMetadataSetEdit AddAuthors() =>
        new(
            BatchMetadataFieldOperation.Add,
            [BatchTaxonomyReference.Existing(TaxonomyKind.Author, Guid.NewGuid())]);
}
