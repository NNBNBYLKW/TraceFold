namespace TraceFold.Media.Application.Settings;

public sealed record ScanPreferenceStoreResult(
    bool Succeeded,
    ScanPreferences Preferences,
    string? ErrorCode);

public sealed record ScanPreferenceResult(
    bool Succeeded,
    ScanPreferences Preferences,
    string? ErrorCode);

public interface IScanPreferencesStore
{
    Task<ScanPreferences> LoadAsync(CancellationToken cancellationToken);

    Task<ScanPreferenceStoreResult> SaveAutoScanEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken);

    Task SaveLastScanAsync(
        DateTimeOffset completedAtUtc,
        string summary,
        CancellationToken cancellationToken);
}

public interface IScanPreferencesService
{
    Task<ScanPreferences> LoadAsync(CancellationToken cancellationToken);

    Task<ScanPreferenceResult> SetAutoScanEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken);
}
