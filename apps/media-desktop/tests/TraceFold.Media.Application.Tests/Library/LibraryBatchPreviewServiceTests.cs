using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Application.Tests.Library;

public sealed class LibraryBatchPreviewServiceTests
{
    [Fact]
    public void Proposed_metadata_is_visible_to_naming_placeholders()
    {
        TaxonomyTermDto oldAuthor = Term(TaxonomyKind.Author, "Old");
        LibraryBatchEditorDto editor = Editor(
            Asset(authors: [oldAuthor], version: 3));
        LibraryBatchEditDraft draft = LibraryBatchEditDraft.Create(
            BatchMetadataDraft.Create(
                BatchMetadataMode.Replace,
                new(
                    BatchMetadataFieldOperation.Replace,
                    [BatchTaxonomyReference.New(TaxonomyKind.Author, "New")]),
                BatchMetadataSeriesEdit.Unchanged,
                BatchMetadataSetEdit.Unchanged),
            Definition(new MetadataTemplateNamingRule("{authors}-{number}")));

        LibraryBatchPreviewDto preview = CreateService().Preview(editor, draft);

        LibraryBatchPreviewRowDto row = Assert.Single(preview.Rows);
        Assert.Equal("New-1", row.GeneratedDisplayName);
        Assert.True(row.MetadataChanged);
        Assert.True(row.DisplayNameChanged);
        Assert.True(preview.CanApply);
    }

    [Fact]
    public void Existing_term_snapshot_outside_selection_preserves_identity_in_preview()
    {
        TaxonomyTermDto author = Term(TaxonomyKind.Author, "Outside");
        LibraryBatchEditorDto editor = Editor(Asset());
        LibraryBatchEditDraft draft = LibraryBatchEditDraft.Create(
            BatchMetadataDraft.Create(
                BatchMetadataMode.Add,
                new(
                    BatchMetadataFieldOperation.Add,
                    [BatchTaxonomyReference.Existing(
                        TaxonomyKind.Author,
                        author.Id)]),
                BatchMetadataSeriesEdit.Unchanged,
                BatchMetadataSetEdit.Unchanged),
            Definition(new MetadataTemplateNamingRule("{authors}-{name}")),
            [author]);

        LibraryBatchPreviewDto preview = CreateService().Preview(editor, draft);

        Assert.True(preview.CanApply);
        Assert.Equal("Outside-asset", preview.Rows[0].GeneratedDisplayName);
    }

    [Fact]
    public void Any_naming_error_blocks_metadata_only_changes_too()
    {
        TaxonomyTermDto tag = Term(TaxonomyKind.Tag, "Keep");
        LibraryBatchEditorDto editor = Editor(
            Asset(series: null),
            extraTags: [tag]);
        LibraryBatchEditDraft draft = LibraryBatchEditDraft.Create(
            BatchMetadataDraft.Create(
                BatchMetadataMode.Add,
                BatchMetadataSetEdit.Unchanged,
                BatchMetadataSeriesEdit.Unchanged,
                new(
                    BatchMetadataFieldOperation.Add,
                    [BatchTaxonomyReference.Existing(
                        TaxonomyKind.Tag,
                        tag.Id)])),
            Definition(new MetadataTemplateNamingRule("{series}")));

        LibraryBatchPreviewDto preview = CreateService().Preview(editor, draft);

        Assert.False(preview.CanApply);
        Assert.Equal("library.batch.preview_invalid", preview.ErrorCode);
        Assert.True(preview.Rows[0].MetadataChanged);
        Assert.Contains(
            preview.Rows[0].Issues,
            issue => issue.Code == "naming.placeholder_missing");
    }

    [Fact]
    public void Metadata_only_and_naming_only_drafts_are_supported()
    {
        TaxonomyTermDto tag = Term(TaxonomyKind.Tag, "New tag");
        LibraryBatchEditorDto editor = Editor(
            Asset(actualFilename: "photo.jpg"),
            extraTags: [tag]);
        LibraryBatchEditDraft metadataOnly = LibraryBatchEditDraft.Create(
            BatchMetadataDraft.Create(
                BatchMetadataMode.Add,
                BatchMetadataSetEdit.Unchanged,
                BatchMetadataSeriesEdit.Unchanged,
                new(
                    BatchMetadataFieldOperation.Add,
                    [BatchTaxonomyReference.Existing(
                        TaxonomyKind.Tag,
                        tag.Id)])),
            null);
        LibraryBatchEditDraft namingOnly = LibraryBatchEditDraft.Create(
            null,
            Definition(new PrefixNamingRule("sorted-")));

        LibraryBatchPreviewDto metadataPreview =
            CreateService().Preview(editor, metadataOnly);
        LibraryBatchPreviewDto namingPreview =
            CreateService().Preview(editor, namingOnly);

        Assert.True(metadataPreview.CanApply);
        Assert.True(metadataPreview.Rows[0].MetadataChanged);
        Assert.Null(metadataPreview.Rows[0].GeneratedDisplayName);
        Assert.True(namingPreview.CanApply);
        Assert.False(namingPreview.Rows[0].MetadataChanged);
        Assert.Equal(
            "sorted-photo",
            namingPreview.Rows[0].GeneratedDisplayName);
    }

    [Fact]
    public void No_op_draft_is_rejected_after_preview()
    {
        TaxonomyTermDto absent = Term(TaxonomyKind.Author, "Absent");
        LibraryBatchEditorDto editor = Editor(
            Asset(),
            extraAuthors: [absent]);
        LibraryBatchEditDraft draft = LibraryBatchEditDraft.Create(
            BatchMetadataDraft.Create(
                BatchMetadataMode.Remove,
                new(
                    BatchMetadataFieldOperation.Remove,
                    [BatchTaxonomyReference.Existing(
                        TaxonomyKind.Author,
                        absent.Id)]),
                BatchMetadataSeriesEdit.Unchanged,
                BatchMetadataSetEdit.Unchanged),
            null);

        LibraryBatchPreviewDto preview = CreateService().Preview(editor, draft);

        Assert.False(preview.CanApply);
        Assert.Equal(0, preview.ChangedCount);
        Assert.Equal("library.batch.no_changes", preview.ErrorCode);
    }

    [Fact]
    public void Remove_and_explicit_clear_change_only_covered_assets()
    {
        TaxonomyTermDto author = Term(TaxonomyKind.Author, "Author");
        TaxonomyTermDto series = Term(TaxonomyKind.Series, "Series");
        LibraryBatchAssetDto covered = Asset(
            authors: [author],
            series: series,
            ordinal: 1);
        LibraryBatchAssetDto uncovered = Asset(ordinal: 0);
        LibraryBatchEditorDto removeEditor = Editor(covered, uncovered);
        LibraryBatchEditDraft remove = LibraryBatchEditDraft.Create(
            BatchMetadataDraft.Create(
                BatchMetadataMode.Remove,
                new(
                    BatchMetadataFieldOperation.Remove,
                    [BatchTaxonomyReference.Existing(
                        TaxonomyKind.Author,
                        author.Id)]),
                BatchMetadataSeriesEdit.Unchanged,
                BatchMetadataSetEdit.Unchanged),
            null);
        LibraryBatchEditDraft clear = LibraryBatchEditDraft.Create(
            BatchMetadataDraft.Create(
                BatchMetadataMode.Replace,
                BatchMetadataSetEdit.Unchanged,
                new(BatchMetadataFieldOperation.Clear, null),
                BatchMetadataSetEdit.Unchanged),
            null);

        LibraryBatchPreviewDto removed =
            CreateService().Preview(removeEditor, remove);
        LibraryBatchPreviewDto cleared =
            CreateService().Preview(Editor(covered), clear);

        Assert.Equal(1, removed.ChangedCount);
        Assert.False(removed.Rows[0].MetadataChanged);
        Assert.True(removed.Rows[1].MetadataChanged);
        Assert.True(cleared.CanApply);
        Assert.True(cleared.Rows[0].MetadataChanged);
    }

    [Fact]
    public void Deferred_new_terms_are_normalized_deduplicated_and_ordered()
    {
        LibraryBatchEditorDto editor = Editor(Asset());
        LibraryBatchEditDraft draft = LibraryBatchEditDraft.Create(
            BatchMetadataDraft.Create(
                BatchMetadataMode.Add,
                new(
                    BatchMetadataFieldOperation.Add,
                    [
                        BatchTaxonomyReference.New(
                            TaxonomyKind.Author,
                            " 作者甲 "),
                        BatchTaxonomyReference.New(
                            TaxonomyKind.Author,
                            "作者甲"),
                        BatchTaxonomyReference.New(
                            TaxonomyKind.Author,
                            "作者乙")
                    ]),
                BatchMetadataSeriesEdit.Unchanged,
                BatchMetadataSetEdit.Unchanged),
            Definition(new MetadataTemplateNamingRule("{authors}")));

        LibraryBatchPreviewDto preview = CreateService().Preview(editor, draft);

        Assert.True(preview.CanApply);
        Assert.Equal("作者甲、作者乙", preview.Rows[0].GeneratedDisplayName);
    }

    [Fact]
    public void Duplicate_names_are_warnings_and_do_not_block_apply()
    {
        LibraryBatchEditorDto editor = Editor(
            Asset(ordinal: 1),
            Asset(ordinal: 0),
            unselectedDisplayNames: new HashSet<string>(
                ["same"],
                StringComparer.OrdinalIgnoreCase));
        LibraryBatchEditDraft draft = LibraryBatchEditDraft.Create(
            null,
            Definition(new MetadataTemplateNamingRule("same")));

        LibraryBatchPreviewDto preview = CreateService().Preview(editor, draft);

        Assert.True(preview.CanApply);
        Assert.Equal(4, preview.WarningCount);
        Assert.All(preview.Rows, row => Assert.Equal(2, row.Issues.Count));
    }

    [Fact]
    public void Preview_order_and_sequence_follow_selection_ordinals()
    {
        LibraryBatchAssetDto later = Asset(
            actualFilename: "later.jpg",
            ordinal: 1);
        LibraryBatchAssetDto first = Asset(
            actualFilename: "first.jpg",
            ordinal: 0);
        LibraryBatchEditDraft draft = LibraryBatchEditDraft.Create(
            null,
            Definition(new SequenceNamingRule(
                1, 1, 2, NamingPlacement.Prefix, "-")));

        LibraryBatchPreviewDto preview =
            CreateService().Preview(Editor(later, first), draft);

        Assert.Equal(first.Selection.AssetId, preview.Rows[0].AssetId);
        Assert.Equal("01-first", preview.Rows[0].GeneratedDisplayName);
        Assert.Equal(later.Selection.AssetId, preview.Rows[1].AssetId);
        Assert.Equal("02-later", preview.Rows[1].GeneratedDisplayName);
    }

    [Fact]
    public void Empty_combined_draft_and_invalid_selection_are_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            LibraryBatchEditDraft.Create(null, null));
        LibraryBatchAssetDto asset = Asset();
        LibraryBatchEditorDto invalid = Editor(
            asset,
            asset with
            {
                ActualFilename = "duplicate.jpg",
                Selection = asset.Selection with { Ordinal = 1 }
            });
        LibraryBatchEditDraft draft = LibraryBatchEditDraft.Create(
            null,
            Definition(new PrefixNamingRule("x")));

        Assert.Throws<ArgumentException>(() =>
            CreateService().Preview(invalid, draft));
    }

    private static LibraryBatchPreviewService CreateService() =>
        new(new NamingRuleEngine());

    private static NamingDefinition Definition(params INamingRule[] rules) =>
        NamingDefinition.Create(NamingSource.ActualFilename, rules);

    private static LibraryBatchEditorDto Editor(
        LibraryBatchAssetDto first,
        LibraryBatchAssetDto? second = null,
        IReadOnlyList<TaxonomyTermDto>? extraAuthors = null,
        IReadOnlyList<TaxonomyTermDto>? extraTags = null,
        IReadOnlySet<string>? unselectedDisplayNames = null)
    {
        LibraryBatchAssetDto[] assets = second is null
            ? [first]
            : [first, second];
        TaxonomyTermDto[] authors = assets
            .SelectMany(asset => asset.Authors)
            .Concat(extraAuthors ?? [])
            .GroupBy(term => term.Id)
            .Select(group => group.First())
            .ToArray();
        TaxonomyTermDto[] tags = assets
            .SelectMany(asset => asset.Tags)
            .Concat(extraTags ?? [])
            .GroupBy(term => term.Id)
            .Select(group => group.First())
            .ToArray();
        return new LibraryBatchEditorDto(
            assets,
            authors.Select(term => new BatchTermCoverageDto(
                term,
                assets.Count(asset => asset.Authors.Any(
                    value => value.Id == term.Id)))).ToArray(),
            tags.Select(term => new BatchTermCoverageDto(
                term,
                assets.Count(asset => asset.Tags.Any(
                    value => value.Id == term.Id)))).ToArray(),
            unselectedDisplayNames
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static LibraryBatchAssetDto Asset(
        string actualFilename = "asset.jpg",
        string? displayName = null,
        IReadOnlyList<TaxonomyTermDto>? authors = null,
        TaxonomyTermDto? series = null,
        IReadOnlyList<TaxonomyTermDto>? tags = null,
        long version = 0,
        int ordinal = 0) =>
        new(
            new LibrarySelectionEntry(Guid.NewGuid(), version, ordinal),
            actualFilename,
            displayName,
            authors ?? [],
            series,
            tags ?? []);

    private static TaxonomyTermDto Term(
        TaxonomyKind kind,
        string name) =>
        new(Guid.NewGuid(), kind, name, name.ToUpperInvariant(), 0);
}
