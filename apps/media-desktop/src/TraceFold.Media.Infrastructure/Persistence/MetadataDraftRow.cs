namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class MetadataDraftRow
{
    public Guid InboxItemId { get; set; }

    public InboxItemRow InboxItem { get; set; } = null!;

    public string? DisplayName { get; set; }

    public Guid? SeriesId { get; set; }

    public TaxonomyTermRow? Series { get; set; }

    public long Version { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
