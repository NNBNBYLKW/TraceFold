using TraceFold.Media.Application.Settings;
using Xunit;

namespace TraceFold.Media.Application.Tests.Settings;

public sealed class ScanPreferencesServiceTests
{
    [Fact]
    public async Task Missing_settings_default_auto_scan_off()
    {
        var service = new ScanPreferencesService(new InMemoryScanPreferencesStore());

        ScanPreferences value = await service.LoadAsync(CancellationToken.None);

        Assert.False(value.AutoScanEnabled);
        Assert.Null(value.LastScanAtUtc);
        Assert.Null(value.LastScanSummary);
    }

    [Fact]
    public async Task Enable_is_persisted_without_changing_other_preferences()
    {
        var store = new InMemoryScanPreferencesStore(
            new ScanPreferences(false, Instant, "previous"));
        var service = new ScanPreferencesService(store);

        ScanPreferenceResult result = await service.SetAutoScanEnabledAsync(
            true,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        ScanPreferences saved = await store.LoadAsync(CancellationToken.None);
        Assert.True(saved.AutoScanEnabled);
        Assert.Equal(Instant, saved.LastScanAtUtc);
        Assert.Equal("previous", saved.LastScanSummary);
    }

    private static readonly DateTimeOffset Instant =
        DateTimeOffset.Parse("2026-08-22T10:00:00Z");

    private sealed class InMemoryScanPreferencesStore(ScanPreferences? value = null)
        : IScanPreferencesStore
    {
        private ScanPreferences _value = value ?? ScanPreferences.Default;

        public Task<ScanPreferences> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_value);

        public Task<ScanPreferenceStoreResult> SaveAutoScanEnabledAsync(
            bool enabled,
            CancellationToken cancellationToken)
        {
            _value = _value with { AutoScanEnabled = enabled };
            return Task.FromResult(new ScanPreferenceStoreResult(
                true,
                _value,
                null));
        }

        public Task SaveLastScanAsync(
            DateTimeOffset completedAtUtc,
            string summary,
            CancellationToken cancellationToken)
        {
            _value = _value with
            {
                LastScanAtUtc = completedAtUtc,
                LastScanSummary = summary
            };
            return Task.CompletedTask;
        }
    }
}
