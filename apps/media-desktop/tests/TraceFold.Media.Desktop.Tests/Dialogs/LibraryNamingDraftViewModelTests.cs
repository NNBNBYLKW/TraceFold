using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Desktop.Dialogs;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Dialogs;

public sealed class LibraryNamingDraftViewModelTests
{
    [Fact]
    public void Moving_a_rule_recomputes_preview_in_the_new_order()
    {
        LibraryNamingDraftViewModel viewModel = CreateNamingDraft();
        viewModel.AddRule(new PrefixNamingRule("A"));
        viewModel.AddRule(new ReplaceNamingRule("Aphoto", "done", true));

        viewModel.MoveRuleUp(viewModel.Rules[1]);

        Assert.Equal("Aphoto", viewModel.PreviewRows[0].GeneratedDisplayName);
    }

    [Fact]
    public void Rule_identity_stays_stable_while_display_indexes_follow_order()
    {
        var dialogId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        LibraryNamingDraftViewModel viewModel = CreateNamingDraft(
            dialogId: dialogId);
        NamingRuleRowViewModel first =
            viewModel.AddRule(new PrefixNamingRule("A"));
        NamingRuleRowViewModel second =
            viewModel.AddRule(new SuffixNamingRule("B"));
        string firstAutomationId = first.AutomationId;

        viewModel.MoveRuleDown(first);

        Assert.Equal(firstAutomationId, first.AutomationId);
        Assert.Equal($"LibraryNamingRule-{dialogId:N}-1", first.AutomationId);
        Assert.Equal(2, first.DisplayIndex);
        Assert.Equal(1, second.DisplayIndex);
    }

    [Fact]
    public void Editing_selected_rule_parameters_recomputes_preview()
    {
        LibraryNamingDraftViewModel viewModel = CreateNamingDraft();
        NamingRuleRowViewModel prefix =
            viewModel.AddRule(new PrefixNamingRule("A"));

        prefix.TextParameter = "B";

        Assert.Equal("Bphoto", viewModel.PreviewRows[0].GeneratedDisplayName);
    }

    [Fact]
    public void Enabling_disabling_and_deleting_rules_recomputes_preview()
    {
        LibraryNamingDraftViewModel viewModel = CreateNamingDraft();
        NamingRuleRowViewModel prefix =
            viewModel.AddRule(new PrefixNamingRule("A"));

        prefix.IsEnabled = false;
        Assert.Equal("photo", viewModel.PreviewRows[0].GeneratedDisplayName);
        Assert.False(viewModel.CanApply);

        prefix.IsEnabled = true;
        Assert.Equal("Aphoto", viewModel.PreviewRows[0].GeneratedDisplayName);
        Assert.True(viewModel.CanApply);

        viewModel.DeleteRule(prefix);
        Assert.Empty(viewModel.Rules);
        Assert.Empty(viewModel.PreviewRows);
    }

    [Fact]
    public void Invalid_sequence_is_an_error_that_blocks_apply()
    {
        LibraryNamingDraftViewModel viewModel = CreateNamingDraft();
        viewModel.AddRule(new SequenceNamingRule(
            -1,
            1,
            1,
            NamingPlacement.Prefix,
            "-"));

        Assert.True(viewModel.ErrorCount > 0);
        Assert.Equal("naming.sequence_invalid", viewModel.FirstErrorCode);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public void Switching_source_recomputes_from_actual_or_display_name()
    {
        LibraryNamingDraftViewModel viewModel = CreateNamingDraft(
            LibraryDialogTestData.Editor(explicitDisplayName: "alias"));
        viewModel.AddRule(new PrefixNamingRule("A"));

        Assert.Equal("Aphoto", viewModel.PreviewRows[0].GeneratedDisplayName);

        viewModel.Source = NamingSource.CurrentDisplayName;

        Assert.Equal("Aalias", viewModel.PreviewRows[0].GeneratedDisplayName);
    }

    [Fact]
    public void Duplicate_warning_does_not_block_apply_but_empty_error_does()
    {
        LibraryNamingDraftViewModel warning = CreateNamingDraft(
            LibraryDialogTestData.Editor(
                unselectedDisplayNames: new HashSet<string>(
                    ["Aphoto"],
                    StringComparer.OrdinalIgnoreCase)));
        warning.AddRule(new PrefixNamingRule("A"));

        Assert.True(warning.WarningCount > 0);
        Assert.Equal(0, warning.ErrorCount);
        Assert.True(warning.CanApply);

        LibraryNamingDraftViewModel error = CreateNamingDraft();
        error.AddRule(new RemoveTextNamingRule("photo", true));

        Assert.Equal("naming.empty", error.FirstErrorCode);
        Assert.False(error.CanApply);
    }

    [Fact]
    public async Task Save_as_name_conflict_requires_an_explicit_overwrite()
    {
        NamingPresetDto existing = Preset("常用", new PrefixNamingRule("old-"));
        var presets = new LibraryDialogTestData.FakeNamingPresetService(existing);
        LibraryNamingDraftViewModel viewModel = CreateNamingDraft(presets: presets);
        viewModel.AddRule(new PrefixNamingRule("new-"));

        NamingPresetSaveResult conflict = await viewModel.SavePresetAsync(
            "常用",
            overwriteExisting: false,
            TestContext.Current.CancellationToken);

        Assert.False(conflict.Succeeded);
        Assert.True(viewModel.RequiresPresetOverwrite);
        Assert.Single(viewModel.Rules);

        NamingPresetSaveResult saved = await viewModel.SavePresetAsync(
            "常用",
            overwriteExisting: true,
            TestContext.Current.CancellationToken);

        Assert.True(saved.Succeeded);
        Assert.True(presets.LastSaveRequestedOverwrite);
        Assert.False(viewModel.RequiresPresetOverwrite);
    }

    [Fact]
    public void Unsupported_preset_is_disabled_and_does_not_replace_rules()
    {
        NamingPresetDto unsupported = new(
            Guid.NewGuid(),
            "旧预设",
            null,
            false,
            "naming.preset.unsupported_version",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        LibraryNamingDraftViewModel viewModel = CreateNamingDraft();
        viewModel.AddRule(new PrefixNamingRule("keep-"));

        Assert.False(viewModel.LoadPreset(unsupported));
        Assert.Single(viewModel.Rules);
        Assert.Equal("naming.preset.unsupported_version", viewModel.PresetErrorCode);
    }

    [Fact]
    public async Task Proposed_metadata_is_used_by_the_naming_preview()
    {
        LibraryBatchEditorDto editor = LibraryDialogTestData.Editor();
        LibraryMetadataDraftViewModel metadata =
            LibraryDialogTestData.CreateMetadataDraft(editor);
        LibraryNamingDraftViewModel naming = CreateNamingDraft(
            editor,
            metadataProvider: () =>
            {
                metadata.TryBuildDraft(out BatchMetadataDraft? draft, out _);
                return draft;
            },
            existingTermsProvider: () => metadata.ExistingTermSnapshots);
        metadata.DraftChanged += (_, _) => naming.RefreshPreview();
        naming.AddRule(new MetadataTemplateNamingRule("{authors}-{name}"));

        Assert.Equal("naming.placeholder_missing", naming.FirstErrorCode);

        metadata.AddAuthorPicker.InputText = "Alice";
        await metadata.AddAuthorPicker.CommitInputAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal("Alice-photo", naming.PreviewRows[0].GeneratedDisplayName);
        Assert.True(naming.CanApply);
    }

    [Fact]
    public async Task Existing_term_outside_selection_still_feeds_preview_by_name()
    {
        LibraryBatchEditorDto editor = LibraryDialogTestData.Editor();
        var term = new TaxonomyTermDto(
            Guid.NewGuid(),
            TaxonomyKind.Author,
            "Alice",
            TaxonomyName.Create("Alice").Normalized,
            0);
        var metadata = new LibraryMetadataDraftViewModel(
            new FixedTaxonomySearchService(term));
        metadata.Load(editor);
        LibraryNamingDraftViewModel naming = CreateNamingDraft(
            editor,
            metadataProvider: () =>
            {
                metadata.TryBuildDraft(out BatchMetadataDraft? draft, out _);
                return draft;
            },
            existingTermsProvider: () => metadata.ExistingTermSnapshots);
        metadata.DraftChanged += (_, _) => naming.RefreshPreview();
        naming.AddRule(new MetadataTemplateNamingRule("{authors}-{name}"));

        metadata.AddAuthorPicker.InputText = "Alice";
        await metadata.AddAuthorPicker.CommitInputAsync(
            TestContext.Current.CancellationToken);

        Assert.True(metadata.TryBuildDraft(
            out BatchMetadataDraft? draft,
            out _));
        BatchTaxonomyReference reference = Assert.Single(draft!.Authors.Values);
        Assert.Equal(term.Id, reference.ExistingId);
        Assert.Null(reference.NewName);
        Assert.Equal("Alice-photo", naming.PreviewRows[0].GeneratedDisplayName);
        Assert.True(naming.CanApply);
    }

    private static LibraryNamingDraftViewModel CreateNamingDraft(
        LibraryBatchEditorDto? editor = null,
        LibraryDialogTestData.FakeNamingPresetService? presets = null,
        Func<BatchMetadataDraft?>? metadataProvider = null,
        Guid? dialogId = null,
        Func<IReadOnlyList<TaxonomyTermDto>>? existingTermsProvider = null) =>
        new(
            presets ?? new LibraryDialogTestData.FakeNamingPresetService(),
            LibraryDialogTestData.PreviewService(),
            editor ?? LibraryDialogTestData.Editor(),
            metadataProvider,
            dialogId,
            existingTermsProvider);

    private static NamingPresetDto Preset(string name, INamingRule rule) =>
        new(
            Guid.NewGuid(),
            name,
            NamingDefinition.Create(NamingSource.ActualFilename, [rule]),
            true,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private sealed class FixedTaxonomySearchService(TaxonomyTermDto term)
        : ITaxonomySearchService
    {
        public Task<IReadOnlyList<TaxonomyTermDto>> SearchAsync(
            TaxonomyKind kind,
            string? query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaxonomyTermDto>>(
                kind == term.Kind ? [term] : []);
    }
}
