using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Desktop.Pages;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

public sealed class LibraryFilterViewModelTests
{
    [Fact]
    public void Missing_author_clears_selected_author_terms_in_the_draft()
    {
        var filter = CreateFilter();
        filter.Authors.Select(Term(TaxonomyKind.Author, "Alice"));

        filter.MissingAuthors = true;

        Assert.Empty(filter.Authors.SelectedTerms);
        Assert.True(filter.MissingAuthors);
    }

    [Fact]
    public void Selecting_author_clears_the_missing_author_draft()
    {
        var filter = CreateFilter();
        filter.MissingAuthors = true;

        filter.Authors.Select(Term(TaxonomyKind.Author, "Alice"));

        Assert.False(filter.MissingAuthors);
        Assert.Single(filter.Authors.SelectedTerms);
    }

    [Fact]
    public void Cancel_discards_draft_without_changing_applied_chips()
    {
        var filter = CreateFilter();
        filter.BeginEdit();
        filter.MissingTags = true;
        filter.CancelDraft();

        Assert.False(filter.AppliedQuery.MissingTags);
        Assert.Empty(filter.ActiveChips);
    }

    [Fact]
    public void Apply_builds_chips_and_removing_one_updates_applied_query()
    {
        var filter = CreateFilter();
        TaxonomyTermDto author = Term(TaxonomyKind.Author, "Alice");
        filter.Authors.Select(author);
        filter.MissingTags = true;

        LibraryQuery applied = filter.ApplyDraft();
        LibraryFilterChipViewModel authorChip = Assert.Single(
            filter.ActiveChips,
            chip => chip.Key == $"author:{author.Id:N}");

        authorChip.Remove();

        Assert.Equal([author.Id], applied.AuthorIds);
        Assert.Empty(filter.AppliedQuery.AuthorIds);
        Assert.True(filter.AppliedQuery.MissingTags);
    }

    [Fact]
    public async Task Term_search_never_creates_free_text_taxonomy()
    {
        TaxonomyTermDto existing = Term(TaxonomyKind.Tag, "Nature");
        var taxonomy = new StubTaxonomySearchService(existing);
        var filter = new LibraryFilterViewModel(taxonomy);

        await filter.Tags.SearchAsync(
            "Nature",
            TestContext.Current.CancellationToken);
        filter.Tags.Select(Assert.Single(filter.Tags.Suggestions));

        Assert.Equal(existing.Id, Assert.Single(filter.Tags.SelectedTerms).Id);
    }

    private static LibraryFilterViewModel CreateFilter() =>
        new(new StubTaxonomySearchService());

    private static TaxonomyTermDto Term(TaxonomyKind kind, string name) =>
        new(Guid.NewGuid(), kind, name, name.ToUpperInvariant(), 0);

    private sealed class StubTaxonomySearchService(params TaxonomyTermDto[] terms)
        : ITaxonomySearchService
    {
        public Task<IReadOnlyList<TaxonomyTermDto>> SearchAsync(
            TaxonomyKind kind,
            string? query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaxonomyTermDto>>(
                terms.Where(term => term.Kind == kind).ToArray());
    }
}
