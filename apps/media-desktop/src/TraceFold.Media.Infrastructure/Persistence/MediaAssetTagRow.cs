namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class MediaAssetTagRow
{
    public Guid MediaAssetId { get; set; }

    public MediaAssetRow MediaAsset { get; set; } = null!;

    public Guid TagId { get; set; }

    public TaxonomyTermRow Tag { get; set; } = null!;

    public int Ordinal { get; set; }
}
