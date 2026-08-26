using TraceFold.Media.Application.Library;
using TraceFold.Media.Desktop.Dialogs;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Dialogs;

public sealed class LibraryBatchEditDialogViewModelTests
{
    [Fact]
    public async Task Switching_tabs_preserves_both_drafts()
    {
        LibraryBatchEditDialogViewModel viewModel = CreateDialog();
        viewModel.Metadata.AddAuthorPicker.InputText = "Alice";
        await viewModel.Metadata.AddAuthorPicker.CommitInputAsync(
            TestContext.Current.CancellationToken);
        viewModel.SelectedTab = LibraryBatchEditTab.Naming;
        viewModel.Naming.AddRule(new PrefixNamingRule("A-"));
        viewModel.SelectedTab = LibraryBatchEditTab.Metadata;

        Assert.Equal(
            "Alice",
            viewModel.Metadata.AddAuthorPicker.SelectedChoices[0].DisplayText);
        Assert.Single(viewModel.Naming.Rules);
    }

    [Fact]
    public async Task Apply_failure_keeps_drafts_and_selects_first_error()
    {
        LibraryBatchEditDialogViewModel viewModel = CreateDialog(
            new(false, 0, 0, 0, false, "library.batch.version_conflict"));
        viewModel.Naming.AddRule(new PrefixNamingRule("keep-"));

        await viewModel.ApplyCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Naming.Rules);
        Assert.Equal("选择中的资料已变化，请刷新后重试。", viewModel.StatusMessage);
        Assert.True(viewModel.HasFocusedProblem);
    }

    [Theory]
    [InlineData("library.batch.selection_missing", "选择中的资料已不存在，请刷新后重试。")]
    [InlineData("library.batch.preview_invalid", "当前预览存在错误，请先修正后再应用。")]
    [InlineData("library.batch.persistence_failed", "保存失败，当前输入已保留，可直接重试。")]
    public async Task Stable_apply_errors_are_mapped(
        string errorCode,
        string expected)
    {
        LibraryBatchEditDialogViewModel viewModel = CreateDialog(
            new(false, 0, 0, 0, false, errorCode));
        viewModel.Naming.AddRule(new PrefixNamingRule("keep-"));

        await viewModel.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(expected, viewModel.StatusMessage);
    }

    [Theory]
    [InlineData("naming.empty", "生成的显示名称不能为空。")]
    [InlineData("naming.too_long", "生成的显示名称过长。")]
    [InlineData("naming.placeholder_missing", "部分文件缺少模板所需资料。")]
    public void Stable_naming_errors_are_mapped(
        string errorCode,
        string expected)
    {
        Assert.Equal(expected, LibraryBatchEditDialogViewModel.ErrorMessage(errorCode));
    }

    private static LibraryBatchEditDialogViewModel CreateDialog(
        LibraryBatchApplyResult? applyResult = null)
    {
        LibraryBatchEditorDto editor = LibraryDialogTestData.Editor();
        var service = new FakeLibraryBatchEditorService(
            LibraryDialogTestData.PreviewService(),
            applyResult ?? new(true, 1, 0, 0, true, null));
        return new(
            service,
            LibraryDialogTestData.PreviewService(),
            new LibraryDialogTestData.FakeNamingPresetService(),
            new LibraryDialogTestData.EmptyTaxonomySearchService(),
            editor);
    }

    private sealed class FakeLibraryBatchEditorService(
        ILibraryBatchPreviewService preview,
        LibraryBatchApplyResult applyResult) : ILibraryBatchEditorService
    {
        public LibraryBatchUndoState UndoState =>
            LibraryBatchUndoState.Unavailable;

        public event EventHandler? UndoStateChanged
        {
            add { }
            remove { }
        }

        public Task<LibraryBatchPrepareResult> PrepareAsync(
            IReadOnlyList<LibrarySelectionEntry> selection,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public LibraryBatchPreviewDto Preview(
            LibraryBatchEditorDto editor,
            LibraryBatchEditDraft draft) => preview.Preview(editor, draft);

        public Task<LibraryBatchApplyResult> ApplyAsync(
            IReadOnlyList<LibrarySelectionEntry> selection,
            LibraryBatchEditDraft draft,
            CancellationToken cancellationToken) =>
            Task.FromResult(applyResult);

        public Task<LibraryBatchUndoResult> UndoLastAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void InvalidateUndoFor(Guid assetId)
        {
        }
    }
}
