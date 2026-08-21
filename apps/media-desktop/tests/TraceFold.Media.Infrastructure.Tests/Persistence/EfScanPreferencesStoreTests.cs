using TraceFold.Media.Application.Settings;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class EfScanPreferencesStoreTests
{
    [Fact]
    public async Task Save_auto_scan_requires_an_existing_directory_row()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        var store = new EfScanPreferencesStore(database.CreateContextFactory());

        ScanPreferenceStoreResult result = await store.SaveAutoScanEnabledAsync(
            true,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("settings.directories_required", result.ErrorCode);
        Assert.Equal(ScanPreferences.Default, await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Save_auto_scan_preserves_existing_directories()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        await database.InsertStage1SettingsAsync("C:/inbox", "C:/library", "C:/work");
        var store = new EfScanPreferencesStore(database.CreateContextFactory());

        ScanPreferenceStoreResult result = await store.SaveAutoScanEnabledAsync(
            true,
            CancellationToken.None);

        SavedSettings row = await database.ReadSettingsAsync();
        Assert.True(result.Succeeded);
        Assert.True(row.AutoScanEnabled);
        Assert.Equal("C:/inbox", row.InboxDirectory);
        Assert.Equal("C:/library", row.LibraryDirectory);
        Assert.Equal("C:/work", row.WorkDirectory);
    }

    [Fact]
    public async Task Save_last_scan_round_trips_exact_result_and_preserves_directories()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        await database.InsertStage1SettingsAsync("C:/inbox", "C:/library", "C:/work");
        var store = new EfScanPreferencesStore(database.CreateContextFactory());
        DateTimeOffset completedAtUtc =
            DateTimeOffset.Parse("2026-08-22T10:00:00Z");

        await store.SaveLastScanAsync(
            completedAtUtc,
            "扫描完成：发现 2 个文件，新增 1 项。",
            CancellationToken.None);

        SavedSettings row = await database.ReadSettingsAsync();
        Assert.Equal(completedAtUtc, row.LastScanAtUtc);
        Assert.Equal("扫描完成：发现 2 个文件，新增 1 项。", row.LastScanSummary);
        Assert.Equal("C:/inbox", row.InboxDirectory);
        Assert.Equal("C:/library", row.LibraryDirectory);
        Assert.Equal("C:/work", row.WorkDirectory);
    }
}
