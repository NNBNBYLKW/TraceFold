using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Desktop.Pages;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

public sealed class TaxonomyViewModelTests
{
    [Fact]
    public void Kind_options_cover_the_three_supported_taxonomies()
    {
        var viewModel = new TaxonomyViewModel(new FakeTaxonomyService());

        Assert.Collection(
            viewModel.KindOptions,
            item =>
            {
                Assert.Equal(TaxonomyKind.Author, item.Kind);
                Assert.Equal("作者", item.DisplayName);
            },
            item =>
            {
                Assert.Equal(TaxonomyKind.Series, item.Kind);
                Assert.Equal("系列", item.DisplayName);
            },
            item =>
            {
                Assert.Equal(TaxonomyKind.Tag, item.Kind);
                Assert.Equal("标签", item.DisplayName);
            });
    }

    [Fact]
    public async Task Delete_is_disabled_for_referenced_term()
    {
        TaxonomyTermDto referenced = Term(referenceCount: 2);
        var viewModel = new TaxonomyViewModel(
            new FakeTaxonomyService(referenced));

        await viewModel.SelectAsync(referenced, CancellationToken.None);

        Assert.False(viewModel.DeleteCommand.CanExecute(null));
        Assert.False(viewModel.CanDelete);
        Assert.True(viewModel.CanRename);
        Assert.Equal("该词条已被 2 项资料引用，不能删除。", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Rename_collision_keeps_original_display_name()
    {
        TaxonomyTermDto original = Term(displayName: "Original");
        var service = new FakeTaxonomyService(original)
        {
            RenameResult = new TaxonomyMutationResult(
                false,
                "taxonomy.name_conflict")
        };
        var viewModel = new TaxonomyViewModel(service);
        await viewModel.SelectAsync(original, CancellationToken.None);

        await viewModel.RenameAsync("Existing", CancellationToken.None);

        Assert.Equal("词条名称已存在。", viewModel.StatusMessage);
        Assert.Equal("Original", viewModel.SelectedTerm!.DisplayName);
    }

    [Fact]
    public async Task Delete_removes_an_unreferenced_term_after_confirmation()
    {
        TaxonomyTermDto unused = Term();
        var service = new FakeTaxonomyService(unused);
        var viewModel = new TaxonomyViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectAsync(unused, CancellationToken.None);

        await viewModel.DeleteAsync(CancellationToken.None);

        Assert.Empty(viewModel.Terms);
        Assert.Null(viewModel.SelectedTerm);
        Assert.Equal("词条已删除。", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Delete_failure_message_survives_term_reload()
    {
        TaxonomyTermDto unused = Term();
        var service = new FakeTaxonomyService(unused)
        {
            DeleteResult = new TaxonomyMutationResult(false, "taxonomy.in_use")
        };
        var viewModel = new TaxonomyViewModel(service);
        await viewModel.SelectAsync(unused, CancellationToken.None);

        await viewModel.DeleteAsync(CancellationToken.None);

        Assert.Equal("该词条已被资料引用，不能删除。", viewModel.StatusMessage);
    }

    private static TaxonomyTermDto Term(
        string displayName = "Original",
        int referenceCount = 0) =>
        new(
            Guid.NewGuid(),
            TaxonomyKind.Author,
            displayName,
            displayName.ToUpperInvariant(),
            referenceCount);

    private sealed class FakeTaxonomyService(params TaxonomyTermDto[] terms)
        : ITaxonomyService
    {
        private readonly List<TaxonomyTermDto> _terms = [.. terms];

        public TaxonomyMutationResult RenameResult { get; set; } =
            new(true, null);

        public TaxonomyMutationResult DeleteResult { get; set; } =
            new(true, null);

        public Task<IReadOnlyList<TaxonomyTermDto>> SearchAsync(
            TaxonomyKind kind,
            string? query,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            IReadOnlyList<TaxonomyTermDto> result = _terms
                .Where(term => term.Kind == kind)
                .Where(term => string.IsNullOrWhiteSpace(query)
                    || term.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return Task.FromResult(result);
        }

        public Task<TaxonomyTermDto> CreateAsync(
            TaxonomyKind kind,
            string name,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            var created = new TaxonomyTermDto(
                Guid.NewGuid(),
                kind,
                name,
                name.ToUpperInvariant(),
                0);
            _terms.Add(created);
            return Task.FromResult(created);
        }

        public Task<TaxonomyMutationResult> RenameAsync(
            Guid id,
            string name,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (RenameResult.Succeeded)
            {
                int index = _terms.FindIndex(term => term.Id == id);
                if (index >= 0)
                {
                    _terms[index] = _terms[index] with
                    {
                        DisplayName = name,
                        NormalizedName = name.ToUpperInvariant()
                    };
                }
            }

            return Task.FromResult(RenameResult);
        }

        public Task<TaxonomyMutationResult> DeleteAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (DeleteResult.Succeeded)
            {
                _terms.RemoveAll(term => term.Id == id);
            }

            return Task.FromResult(DeleteResult);
        }
    }
}
