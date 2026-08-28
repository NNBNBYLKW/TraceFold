using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Desktop.Dialogs;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Dialogs;

public sealed class BatchTaxonomyPickerViewModelTests
{
    [Fact]
    public async Task Free_text_becomes_a_deferred_reference_without_create_call()
    {
        var taxonomy = new RecordingTaxonomySearchService();
        var picker = new BatchTaxonomyPickerViewModel(
            taxonomy,
            TaxonomyKind.Author);
        picker.InputText = " 作者甲 ";

        await picker.CommitInputAsync(TestContext.Current.CancellationToken);

        BatchTaxonomyReference value = Assert.Single(picker.SelectedValues);
        Assert.Null(value.ExistingId);
        Assert.Equal("作者甲", value.NewName);
        Assert.Equal(0, taxonomy.CreateCallCount);
    }

    [Fact]
    public async Task Existing_normalized_match_is_referenced_by_id()
    {
        TaxonomyName input = TaxonomyName.Create("ａlice");
        TaxonomyTermDto existing = new(
            Guid.NewGuid(),
            TaxonomyKind.Tag,
            "Alice",
            input.Normalized,
            0);
        var picker = new BatchTaxonomyPickerViewModel(
            new RecordingTaxonomySearchService(existing),
            TaxonomyKind.Tag)
        {
            InputText = "ａlice"
        };

        await picker.CommitInputAsync(TestContext.Current.CancellationToken);

        Assert.Equal(existing.Id, Assert.Single(picker.SelectedValues).ExistingId);
    }

    [Fact]
    public async Task Duplicate_new_values_are_ignored_and_series_keeps_one_value()
    {
        var taxonomy = new RecordingTaxonomySearchService();
        var picker = new BatchTaxonomyPickerViewModel(
            taxonomy,
            TaxonomyKind.Series);
        picker.InputText = "第一季";
        await picker.CommitInputAsync(TestContext.Current.CancellationToken);
        picker.InputText = " 第一季 ";
        await picker.CommitInputAsync(TestContext.Current.CancellationToken);
        picker.InputText = "第二季";

        await picker.CommitInputAsync(TestContext.Current.CancellationToken);

        Assert.Equal("第二季", Assert.Single(picker.SelectedValues).NewName);
    }

    [Fact]
    public async Task Dynamic_choice_automation_ids_are_scoped_unique_and_private()
    {
        var picker = new BatchTaxonomyPickerViewModel(
            new RecordingTaxonomySearchService(),
            TaxonomyKind.Author,
            "LibraryBatchAddAuthor");
        picker.InputText = "Private Alpha";
        await picker.CommitInputAsync(TestContext.Current.CancellationToken);
        picker.InputText = "Private Beta";

        await picker.CommitInputAsync(TestContext.Current.CancellationToken);

        string[] ids = picker.SelectedChoices
            .Select(choice => choice.RemoveAutomationId)
            .ToArray();
        Assert.Equal(2, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.StartsWith(
            "LibraryBatchAddAuthor-Remove-",
            id,
            StringComparison.Ordinal));
        Assert.DoesNotContain(ids, id => id.Contains(
            "Private",
            StringComparison.OrdinalIgnoreCase));
    }

    private sealed class RecordingTaxonomySearchService(
        params TaxonomyTermDto[] terms) : ITaxonomySearchService
    {
        public int CreateCallCount => 0;

        public Task<IReadOnlyList<TaxonomyTermDto>> SearchAsync(
            TaxonomyKind kind,
            string? query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaxonomyTermDto>>(terms
                .Where(term => term.Kind == kind)
                .ToArray());
    }
}
