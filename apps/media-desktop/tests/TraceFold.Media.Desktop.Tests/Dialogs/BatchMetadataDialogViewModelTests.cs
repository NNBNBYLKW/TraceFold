using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Desktop.Dialogs;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Dialogs;

public sealed class BatchMetadataDialogViewModelTests
{
    [Fact]
    public void Replace_fields_start_unchanged_and_blank_input_does_not_clear()
    {
        BatchMetadataDialogViewModel viewModel = CreateEditor();
        viewModel.Load(EditorForThreeItems(), Selection(3));
        viewModel.Mode = BatchMetadataMode.Replace;

        bool built = viewModel.TryBuildCommand(out _, out string? error);

        Assert.False(viewModel.ReplaceAuthorsEnabled);
        Assert.False(viewModel.ReplaceSeriesEnabled);
        Assert.False(viewModel.ReplaceTagsEnabled);
        Assert.False(built);
        Assert.Equal("请先启用至少一个要替换的字段。", error);
    }

    [Fact]
    public void Explicit_clear_builds_clear_while_disabled_field_stays_unchanged()
    {
        BatchMetadataDialogViewModel viewModel = CreateEditor();
        viewModel.Load(EditorForThreeItems(), Selection(3));
        viewModel.Mode = BatchMetadataMode.Replace;
        viewModel.ReplaceSeriesEnabled = true;
        viewModel.ClearSeries = true;

        Assert.True(viewModel.TryBuildCommand(
            out BatchMetadataCommand? command,
            out _));
        Assert.Equal(
            BatchMetadataFieldOperation.Unchanged,
            command!.Authors.Operation);
        Assert.Equal(
            BatchMetadataFieldOperation.Clear,
            command.Series.Operation);
    }

    [Fact]
    public void Remove_page_exposes_only_terms_present_in_the_editor_coverage()
    {
        BatchMetadataDialogViewModel viewModel = CreateEditor();
        TaxonomyTermDto author = Term(TaxonomyKind.Author, "作者甲");
        var editor = new BatchMetadataEditorDto(
            3,
            [new BatchTermCoverageDto(author, 2)],
            []);
        viewModel.Load(editor, Selection(3));
        viewModel.Mode = BatchMetadataMode.Remove;

        BatchTermCoverageOptionViewModel option =
            Assert.Single(viewModel.RemoveAuthors);
        Assert.Equal("作者甲 · 2/3", option.DisplayText);
        Assert.False(viewModel.RemoveAuthorPicker.AllowsFreeText);
    }

    [Fact]
    public async Task Add_builds_deferred_author_and_tag_edits()
    {
        BatchMetadataDialogViewModel viewModel = CreateEditor();
        viewModel.Load(EditorForThreeItems(), Selection(3));
        viewModel.AddAuthorPicker.InputText = "作者甲";
        viewModel.AddTagPicker.InputText = "风景";
        await viewModel.AddAuthorPicker.CommitInputAsync(
            TestContext.Current.CancellationToken);
        await viewModel.AddTagPicker.CommitInputAsync(
            TestContext.Current.CancellationToken);

        Assert.True(viewModel.TryBuildCommand(
            out BatchMetadataCommand? command,
            out _));
        Assert.Equal(BatchMetadataFieldOperation.Add, command!.Authors.Operation);
        Assert.Equal(BatchMetadataFieldOperation.Add, command.Tags.Operation);
        Assert.Equal(3, command.Selection.Count);
    }

    [Fact]
    public async Task Failed_result_keeps_all_editor_input()
    {
        BatchMetadataDialogViewModel viewModel = CreateEditor();
        viewModel.Load(EditorForThreeItems(), Selection(3));
        viewModel.Mode = BatchMetadataMode.Replace;
        viewModel.ReplaceAuthorsEnabled = true;
        viewModel.ReplaceAuthorPicker.InputText = "作者甲";
        await viewModel.ReplaceAuthorPicker.CommitInputAsync(
            TestContext.Current.CancellationToken);

        viewModel.ApplyResult(new BatchMetadataResult(
            false,
            0,
            0,
            3,
            "batch.metadata.version_conflict"));

        Assert.True(viewModel.ReplaceAuthorsEnabled);
        Assert.Single(viewModel.ReplaceAuthorPicker.SelectedValues);
        Assert.NotEmpty(viewModel.StatusMessage);
    }

    private static BatchMetadataDialogViewModel CreateEditor() =>
        new(new EmptyTaxonomySearchService());

    private static BatchMetadataEditorDto EditorForThreeItems() =>
        new(3, [], []);

    private static IReadOnlyList<InboxSelectionEntry> Selection(int count) =>
        Enumerable.Range(0, count)
            .Select(ordinal => new InboxSelectionEntry(
                Guid.NewGuid(),
                0,
                ordinal))
            .ToArray();

    private static TaxonomyTermDto Term(TaxonomyKind kind, string name) =>
        new(
            Guid.NewGuid(),
            kind,
            name,
            TaxonomyName.Create(name).Normalized,
            0);

    private sealed class EmptyTaxonomySearchService : ITaxonomySearchService
    {
        public Task<IReadOnlyList<TaxonomyTermDto>> SearchAsync(
            TaxonomyKind kind,
            string? query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaxonomyTermDto>>([]);
    }
}
