namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class InboxDraftAuthorRow
{
    public Guid InboxItemId { get; set; }

    public InboxItemRow InboxItem { get; set; } = null!;

    public Guid AuthorId { get; set; }

    public TaxonomyTermRow Author { get; set; } = null!;

    public int Ordinal { get; set; }
}
