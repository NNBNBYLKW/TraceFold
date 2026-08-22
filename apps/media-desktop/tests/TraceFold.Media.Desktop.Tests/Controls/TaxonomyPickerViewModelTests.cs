using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Desktop.Controls;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Controls;

public sealed class TaxonomyPickerViewModelTests
{
    [Fact]
    public async Task Exact_suggestion_is_reused_without_creation()
    {
        TaxonomyTermDto existing = Term(TaxonomyKind.Author, "Alice");
        var service = new RecordingTaxonomyService(existing);
        var viewModel = new TaxonomyPickerViewModel(service)
        {
            Kind = TaxonomyKind.Author
        };
        await viewModel.SearchAsync("ali", TestContext.Current.CancellationToken);

        await viewModel.CommitAsync(" ＡLICE ", TestContext.Current.CancellationToken);

        Assert.Equal(existing.Id, Assert.Single(viewModel.SelectedTerms).Id);
        Assert.Equal(0, service.CreateCount);
    }

    [Fact]
    public async Task Missing_term_is_created_once_and_duplicate_ids_are_ignored()
    {
        var service = new RecordingTaxonomyService();
        var viewModel = new TaxonomyPickerViewModel(service)
        {
            Kind = TaxonomyKind.Tag
        };

        await viewModel.CommitAsync("夜景", TestContext.Current.CancellationToken);
        await viewModel.CommitAsync("夜景", TestContext.Current.CancellationToken);

        Assert.Equal(1, service.CreateCount);
        Assert.Single(viewModel.SelectedTerms);
    }

    [Fact]
    public async Task Selecting_a_second_series_replaces_the_first()
    {
        var service = new RecordingTaxonomyService();
        var viewModel = new TaxonomyPickerViewModel(service)
        {
            Kind = TaxonomyKind.Series
        };

        await viewModel.CommitAsync("第一季", TestContext.Current.CancellationToken);
        await viewModel.CommitAsync("第二季", TestContext.Current.CancellationToken);

        Assert.Equal("第二季", Assert.Single(viewModel.SelectedTerms).DisplayName);
    }

    private static TaxonomyTermDto Term(TaxonomyKind kind, string name) =>
        new(
            Guid.NewGuid(),
            kind,
            name,
            TaxonomyName.Create(name).Normalized,
            0);

    private sealed class RecordingTaxonomyService(params TaxonomyTermDto[] terms)
        : ITaxonomyService
    {
        private readonly List<TaxonomyTermDto> _terms = [.. terms];

        public int CreateCount { get; private set; }

        public Task<IReadOnlyList<TaxonomyTermDto>> SearchAsync(
            TaxonomyKind kind,
            string? query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaxonomyTermDto>>(_terms
                .Where(term => term.Kind == kind)
                .ToArray());

        public Task<TaxonomyTermDto> CreateAsync(
            TaxonomyKind kind,
            string name,
            CancellationToken cancellationToken)
        {
            TaxonomyName normalized = TaxonomyName.Create(name);
            TaxonomyTermDto? existing = _terms.SingleOrDefault(term =>
                term.Kind == kind && term.NormalizedName == normalized.Normalized);
            if (existing is not null)
            {
                return Task.FromResult(existing);
            }

            CreateCount++;
            TaxonomyTermDto created = Term(kind, normalized.Display);
            _terms.Add(created);
            return Task.FromResult(created);
        }

        public Task<TaxonomyMutationResult> RenameAsync(
            Guid id,
            string name,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TaxonomyMutationResult(true, null));

        public Task<TaxonomyMutationResult> DeleteAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TaxonomyMutationResult(true, null));
    }
}
