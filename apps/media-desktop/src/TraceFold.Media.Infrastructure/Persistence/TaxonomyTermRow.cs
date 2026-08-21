namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class TaxonomyTermRow
{
    public Guid Id { get; set; }

    public required string Kind { get; set; }

    public required string DisplayName { get; set; }

    public required string NormalizedName { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
