namespace TraceFold.Media.Infrastructure.Persistence;

internal sealed class AppSettingsRow
{
    public int Id { get; set; } = 1;

    public required string InboxDirectory { get; set; }

    public required string LibraryDirectory { get; set; }

    public required string WorkDirectory { get; set; }

    public bool AutoScanEnabled { get; set; }

    public DateTimeOffset? LastScanAtUtc { get; set; }

    public string? LastScanSummary { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
