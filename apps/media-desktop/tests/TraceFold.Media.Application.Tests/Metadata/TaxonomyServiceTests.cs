using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Application.Tests.Metadata;

public sealed class TaxonomyServiceTests
{
    [Fact]
    public async Task CreateAsync_reuses_existing_normalized_term()
    {
        var store = new InMemoryTaxonomyStore();
        var service = new TaxonomyService(store);

        TaxonomyTermDto first = await service.CreateAsync(
            TaxonomyKind.Author,
            "Alice",
            CancellationToken.None);
        TaxonomyTermDto second = await service.CreateAsync(
            TaxonomyKind.Author,
            " ＡLICE ",
            CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Alice", second.DisplayName);
    }

    [Fact]
    public async Task DeleteAsync_refuses_referenced_term()
    {
        var store = new InMemoryTaxonomyStore(referenceCount: 2);
        var service = new TaxonomyService(store);

        TaxonomyMutationResult result = await service.DeleteAsync(
            store.TermId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("taxonomy.in_use", result.ErrorCode);
    }

    private sealed class InMemoryTaxonomyStore(int referenceCount = 0) : ITaxonomyStore
    {
        private readonly Dictionary<(TaxonomyKind Kind, string Normalized), TaxonomyTermDto>
            _terms = [];

        public Guid TermId { get; } = Guid.NewGuid();

        public Task<IReadOnlyList<TaxonomyTermDto>> SearchAsync(
            TaxonomyKind kind,
            string? query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaxonomyTermDto>>(_terms.Values.ToArray());

        public Task<TaxonomyTermDto> CreateOrGetAsync(
            TaxonomyKind kind,
            TaxonomyName name,
            CancellationToken cancellationToken)
        {
            if (_terms.TryGetValue((kind, name.Normalized), out TaxonomyTermDto? existing))
            {
                return Task.FromResult(existing);
            }

            var created = new TaxonomyTermDto(
                _terms.Count == 0 ? TermId : Guid.NewGuid(),
                kind,
                name.Display,
                name.Normalized,
                referenceCount);
            _terms.Add((kind, name.Normalized), created);
            return Task.FromResult(created);
        }

        public Task<TaxonomyMutationResult> RenameAsync(
            Guid id,
            TaxonomyName name,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TaxonomyMutationResult(true, null));

        public Task<TaxonomyMutationResult> DeleteAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(referenceCount > 0
                ? new TaxonomyMutationResult(false, "taxonomy.in_use")
                : new TaxonomyMutationResult(true, null));
    }
}
