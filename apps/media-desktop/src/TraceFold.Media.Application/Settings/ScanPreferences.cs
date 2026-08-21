namespace TraceFold.Media.Application.Settings;

public sealed record ScanPreferences(
    bool AutoScanEnabled,
    DateTimeOffset? LastScanAtUtc,
    string? LastScanSummary)
{
    public static ScanPreferences Default { get; } = new(false, null, null);
}
