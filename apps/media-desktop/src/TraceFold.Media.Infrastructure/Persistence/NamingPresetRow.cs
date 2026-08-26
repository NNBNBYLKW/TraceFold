namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class NamingPresetRow
{
    public Guid Id { get; set; }

    public required string DisplayName { get; set; }

    public required string NormalizedName { get; set; }

    public int SchemaVersion { get; set; }

    public required string Source { get; set; }

    public required string RulesJson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
