using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Domain.Tests.Metadata;

public sealed class MetadataSelectionTests
{
    [Fact]
    public void Metadata_is_optional_and_deduplicates_relations()
    {
        Guid author = Guid.NewGuid();
        Guid tag = Guid.NewGuid();

        MetadataSelection selection = MetadataSelection.Create(
            "  ",
            [author, author],
            null,
            [tag, tag]);

        Assert.Null(selection.DisplayName);
        Assert.Equal([author], selection.AuthorIds);
        Assert.Null(selection.SeriesId);
        Assert.Equal([tag], selection.TagIds);
    }

    [Theory]
    [InlineData(" Alice ", "Alice", "ALICE")]
    [InlineData("Ａlice", "Ａlice", "ALICE")]
    public void Taxonomy_name_uses_trimmed_nfkc_casefolded_key(
        string input,
        string display,
        string normalized)
    {
        TaxonomyName value = TaxonomyName.Create(input);

        Assert.Equal(display, value.Display);
        Assert.Equal(normalized, value.Normalized);
    }

    [Fact]
    public void Taxonomy_name_rejects_whitespace_only_input()
    {
        Assert.Throws<ArgumentException>(() => TaxonomyName.Create(" \t "));
    }

    [Fact]
    public void Metadata_relation_lists_cannot_be_mutated_after_creation()
    {
        Guid author = Guid.NewGuid();
        Guid tag = Guid.NewGuid();
        MetadataSelection selection = MetadataSelection.Create(
            null,
            [author],
            null,
            [tag]);

        IList<Guid> authors = Assert.IsAssignableFrom<IList<Guid>>(selection.AuthorIds);
        IList<Guid> tags = Assert.IsAssignableFrom<IList<Guid>>(selection.TagIds);

        Assert.Throws<NotSupportedException>(() => authors[0] = Guid.NewGuid());
        Assert.Throws<NotSupportedException>(() => tags[0] = Guid.NewGuid());
        Assert.Equal(author, selection.AuthorIds[0]);
        Assert.Equal(tag, selection.TagIds[0]);
    }
}
