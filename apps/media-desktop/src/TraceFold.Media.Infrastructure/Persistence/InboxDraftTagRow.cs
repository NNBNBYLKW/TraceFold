namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class InboxDraftTagRow
{
    public Guid InboxItemId { get; set; }

    public InboxItemRow InboxItem { get; set; } = null!;

    public Guid TagId { get; set; }

    public TaxonomyTermRow Tag { get; set; } = null!;

    public int Ordinal { get; set; }
}
