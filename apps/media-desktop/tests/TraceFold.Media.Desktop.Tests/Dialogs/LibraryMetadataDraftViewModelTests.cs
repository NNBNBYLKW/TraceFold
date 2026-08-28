using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Desktop.Dialogs;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Dialogs;

public sealed class LibraryMetadataDraftViewModelTests
{
    [Fact]
    public void Enabled_replace_field_requires_value_or_explicit_clear()
    {
        LibraryMetadataDraftViewModel viewModel =
            LibraryDialogTestData.CreateMetadataDraft();
        viewModel.Mode = BatchMetadataMode.Replace;
        viewModel.ReplaceAuthorsEnabled = true;

        Assert.False(viewModel.TryBuildDraft(out _, out string? error));
        Assert.Equal("已启用的替换字段必须选择资料或明确清空。", error);
    }

    [Fact]
    public void Empty_add_and_remove_pages_produce_no_optional_draft()
    {
        LibraryMetadataDraftViewModel viewModel =
            LibraryDialogTestData.CreateMetadataDraft();

        Assert.True(viewModel.TryBuildDraft(out BatchMetadataDraft? add, out _));
        Assert.Null(add);

        viewModel.Mode = BatchMetadataMode.Remove;

        Assert.True(viewModel.TryBuildDraft(out BatchMetadataDraft? remove, out _));
        Assert.Null(remove);
    }

    [Fact]
    public void Explicit_clear_is_distinct_from_an_unchanged_field()
    {
        LibraryMetadataDraftViewModel viewModel =
            LibraryDialogTestData.CreateMetadataDraft();
        viewModel.Mode = BatchMetadataMode.Replace;
        viewModel.ReplaceSeriesEnabled = true;
        viewModel.ClearSeries = true;

        Assert.True(viewModel.TryBuildDraft(
            out BatchMetadataDraft? draft,
            out _));
        Assert.Equal(
            BatchMetadataFieldOperation.Unchanged,
            draft!.Authors.Operation);
        Assert.Equal(
            BatchMetadataFieldOperation.Clear,
            draft.Series.Operation);
    }

    [Fact]
    public void Remove_term_automation_ids_remain_unique_across_fields()
    {
        Guid sharedId = Guid.NewGuid();
        TaxonomyTermDto author = new(
            sharedId,
            TaxonomyKind.Author,
            "Author",
            "AUTHOR",
            1);
        TaxonomyTermDto tag = new(
            sharedId,
            TaxonomyKind.Tag,
            "Tag",
            "TAG",
            1);
        LibraryBatchEditorDto editor = LibraryDialogTestData.Editor() with
        {
            Authors = [new BatchTermCoverageDto(author, 1)],
            Tags = [new BatchTermCoverageDto(tag, 1)]
        };
        LibraryMetadataDraftViewModel viewModel =
            LibraryDialogTestData.CreateMetadataDraft(editor);

        string authorId = Assert.Single(viewModel.RemoveAuthors)
            .SelectionAutomationId;
        string tagId = Assert.Single(viewModel.RemoveTags)
            .SelectionAutomationId;

        Assert.NotEqual(authorId, tagId);
        Assert.StartsWith(
            "LibraryBatchRemoveAuthor-Select-",
            authorId,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "LibraryBatchRemoveTag-Select-",
            tagId,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Switching_modes_excludes_hidden_existing_choices_from_snapshots()
    {
        TaxonomyTermDto author = Term(TaxonomyKind.Author, "Author");
        LibraryMetadataDraftViewModel viewModel =
            LibraryDialogTestData.CreateMetadataDraft(
                taxonomy: new ExistingTaxonomySearchService(author));
        viewModel.AddAuthorPicker.InputText = author.DisplayName;
        await viewModel.AddAuthorPicker.CommitInputAsync(
            TestContext.Current.CancellationToken);
        Assert.Single(viewModel.ExistingTermSnapshots);

        viewModel.Mode = BatchMetadataMode.Remove;

        Assert.Empty(viewModel.ExistingTermSnapshots);
    }

    [Fact]
    public async Task Disabling_replace_excludes_hidden_existing_choice_snapshot()
    {
        TaxonomyTermDto author = Term(TaxonomyKind.Author, "Author");
        LibraryMetadataDraftViewModel viewModel =
            LibraryDialogTestData.CreateMetadataDraft(
                taxonomy: new ExistingTaxonomySearchService(author));
        viewModel.Mode = BatchMetadataMode.Replace;
        viewModel.ReplaceAuthorsEnabled = true;
        viewModel.ReplaceAuthorPicker.InputText = author.DisplayName;
        await viewModel.ReplaceAuthorPicker.CommitInputAsync(
            TestContext.Current.CancellationToken);
        Assert.Single(viewModel.ExistingTermSnapshots);

        viewModel.ReplaceAuthorsEnabled = false;

        Assert.Empty(viewModel.ExistingTermSnapshots);
    }

    [Fact]
    public async Task Explicit_clear_excludes_hidden_existing_choice_snapshot()
    {
        TaxonomyTermDto author = Term(TaxonomyKind.Author, "Author");
        LibraryMetadataDraftViewModel viewModel =
            LibraryDialogTestData.CreateMetadataDraft(
                taxonomy: new ExistingTaxonomySearchService(author));
        viewModel.Mode = BatchMetadataMode.Replace;
        viewModel.ReplaceAuthorsEnabled = true;
        viewModel.ReplaceAuthorPicker.InputText = author.DisplayName;
        await viewModel.ReplaceAuthorPicker.CommitInputAsync(
            TestContext.Current.CancellationToken);

        viewModel.ClearAuthors = true;

        Assert.True(viewModel.TryBuildDraft(
            out BatchMetadataDraft? metadata,
            out _));
        Assert.Equal(
            BatchMetadataFieldOperation.Clear,
            metadata!.Authors.Operation);
        Assert.Empty(viewModel.ExistingTermSnapshots);
        _ = LibraryBatchEditDraft.Create(
            metadata,
            null,
            viewModel.ExistingTermSnapshots);
    }

    private static TaxonomyTermDto Term(
        TaxonomyKind kind,
        string name) =>
        new(
            Guid.NewGuid(),
            kind,
            name,
            TaxonomyName.Create(name).Normalized,
            0);

    private sealed class ExistingTaxonomySearchService(
        params TaxonomyTermDto[] terms) : ITaxonomySearchService
    {
        public Task<IReadOnlyList<TaxonomyTermDto>> SearchAsync(
            TaxonomyKind kind,
            string? query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaxonomyTermDto>>(terms
                .Where(term => term.Kind == kind)
                .ToArray());
    }
}

internal static class LibraryDialogTestData
{
    public static LibraryMetadataDraftViewModel CreateMetadataDraft(
        LibraryBatchEditorDto? editor = null,
        ITaxonomySearchService? taxonomy = null)
    {
        var viewModel = new LibraryMetadataDraftViewModel(
            taxonomy ?? new EmptyTaxonomySearchService());
        viewModel.Load(editor ?? Editor());
        return viewModel;
    }

    public static LibraryBatchEditorDto Editor(
        string actualFilename = "photo.jpg",
        string? explicitDisplayName = null,
        IReadOnlySet<string>? unselectedDisplayNames = null)
    {
        var selection = new LibrarySelectionEntry(Guid.NewGuid(), 0, 0);
        var asset = new LibraryBatchAssetDto(
            selection,
            actualFilename,
            explicitDisplayName,
            [],
            null,
            []);
        return new(
            [asset],
            [],
            [],
            unselectedDisplayNames ?? new HashSet<string>(
                StringComparer.OrdinalIgnoreCase));
    }

    public static ILibraryBatchPreviewService PreviewService() =>
        new LibraryBatchPreviewService(new NamingRuleEngine());

    public sealed class EmptyTaxonomySearchService : ITaxonomySearchService
    {
        public Task<IReadOnlyList<TaxonomyTermDto>> SearchAsync(
            TaxonomyKind kind,
            string? query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaxonomyTermDto>>([]);
    }

    public sealed class FakeNamingPresetService : INamingPresetService
    {
        private readonly List<NamingPresetDto> _presets;

        public FakeNamingPresetService(params NamingPresetDto[] presets)
        {
            _presets = [.. presets];
        }

        public bool LastSaveRequestedOverwrite { get; private set; }

        public Task<IReadOnlyList<NamingPresetDto>> ListAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NamingPresetDto>>(_presets.ToArray());

        public Task<NamingPresetDto?> LoadAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(_presets.SingleOrDefault(preset => preset.Id == id));

        public Task<NamingPresetSaveResult> SaveAsync(
            NamingPresetSaveRequest request,
            CancellationToken cancellationToken)
        {
            LastSaveRequestedOverwrite = request.OverwriteExisting;
            NamingPresetDto? conflict = _presets.FirstOrDefault(preset =>
                string.Equals(
                    preset.Name,
                    request.Name,
                    StringComparison.OrdinalIgnoreCase));
            if (conflict is not null && !request.OverwriteExisting)
            {
                return Task.FromResult(new NamingPresetSaveResult(
                    false,
                    null,
                    "naming.preset.name_conflict"));
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            NamingPresetDto saved = new(
                conflict?.Id ?? request.Id ?? Guid.NewGuid(),
                request.Name.Trim(),
                request.Definition,
                true,
                null,
                conflict?.CreatedAtUtc ?? now,
                now);
            if (conflict is not null)
            {
                _presets.Remove(conflict);
            }

            _presets.Add(saved);
            return Task.FromResult(new NamingPresetSaveResult(
                true,
                saved,
                null));
        }

        public Task<NamingPresetDeleteResult> DeleteAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            _presets.RemoveAll(preset => preset.Id == id);
            return Task.FromResult(new NamingPresetDeleteResult(true, null));
        }
    }
}
