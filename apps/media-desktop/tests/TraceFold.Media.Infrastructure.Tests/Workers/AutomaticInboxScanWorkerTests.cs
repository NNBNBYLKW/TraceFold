using Microsoft.Extensions.Logging.Abstractions;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Infrastructure.Workers;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Workers;

public sealed class AutomaticInboxScanWorkerTests
{
    [Fact]
    public async Task RunCycleAsync_scans_only_when_enabled()
    {
        var scanner = new RecordingScanner();
        var preferences = new MutablePreferences(autoScanEnabled: false);
        var worker = new AutomaticInboxScanWorker(
            scanner,
            preferences,
            TimeProvider.System,
            NullLogger<AutomaticInboxScanWorker>.Instance);

        await worker.RunCycleAsync(CancellationToken.None);
        preferences.AutoScanEnabled = true;
        await worker.RunCycleAsync(CancellationToken.None);

        Assert.Equal(1, scanner.CallCount);
        Assert.Equal(ScanTrigger.Automatic, scanner.LastTrigger);
    }

    private sealed class RecordingScanner : IInboxScanner
    {
        public int CallCount { get; private set; }

        public ScanTrigger? LastTrigger { get; private set; }

        public Task<ScanResult> ScanAsync(
            ScanTrigger trigger,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastTrigger = trigger;
            return Task.FromResult(new ScanResult(
                ScanOutcome.Completed,
                0,
                0,
                0,
                DateTimeOffset.UtcNow,
                string.Empty));
        }
    }

    private sealed class MutablePreferences(bool autoScanEnabled)
        : IScanPreferencesService
    {
        public bool AutoScanEnabled { get; set; } = autoScanEnabled;

        public Task<ScanPreferences> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ScanPreferences(AutoScanEnabled, null, null));

        public Task<ScanPreferenceResult> SetAutoScanEnabledAsync(
            bool enabled,
            CancellationToken cancellationToken)
        {
            AutoScanEnabled = enabled;
            return Task.FromResult(new ScanPreferenceResult(
                true,
                new ScanPreferences(enabled, null, null),
                null));
        }
    }
}
