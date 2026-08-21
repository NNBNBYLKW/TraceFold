namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class MediaAssetAuthorRow
{
    public Guid MediaAssetId { get; set; }

    public MediaAssetRow MediaAsset { get; set; } = null!;

    public Guid AuthorId { get; set; }

    public TaxonomyTermRow Author { get; set; } = null!;

    public int Ordinal { get; set; }
}
