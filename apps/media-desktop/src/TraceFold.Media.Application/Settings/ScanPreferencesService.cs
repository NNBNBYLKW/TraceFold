namespace TraceFold.Media.Application.Settings;

public sealed class ScanPreferencesService(
    IScanPreferencesStore store) : IScanPreferencesService
{
    public Task<ScanPreferences> LoadAsync(CancellationToken cancellationToken) =>
        store.LoadAsync(cancellationToken);

    public async Task<ScanPreferenceResult> SetAutoScanEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        ScanPreferenceStoreResult result =
            await store.SaveAutoScanEnabledAsync(enabled, cancellationToken);
        return new ScanPreferenceResult(
            result.Succeeded,
            result.Preferences,
            result.ErrorCode);
    }
}
