using TraceFold.Media.Application.Library;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class Stage5BMigrationTests
{
    [Fact]
    public async Task Stage5B_preset_migration_preserves_stage4_assets_and_settings()
    {
        await using TestMediaDatabase database =
            await TestMediaDatabase.CreateAtMigrationAsync(
                "20260823230000_Stage4ConflictRecovery");
        await database.InsertStage1SettingsAsync(
            "C:\\inbox",
            "C:\\library",
            "C:\\work");
        await database.SeedFormalAssetAsync("preserved.jpg", null, 7);

        await database.MigrateAsync();

        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM media_assets"));
        Assert.Equal("C:\\library", await database.ScalarAsync<string>(
            "SELECT LibraryDirectory FROM app_settings WHERE Id = 1"));
        Assert.Contains("naming_presets", await database.ReadTableNamesAsync());
    }

    [Fact]
    public async Task Stage5B_query_migration_preserves_stage4_settings()
    {
        await using TestMediaDatabase database =
            await TestMediaDatabase.CreateAtMigrationAsync(
                "20260823230000_Stage4ConflictRecovery");
        await database.InsertStage1SettingsAsync(
            "C:\\inbox",
            "C:\\library",
            "C:\\work");

        await database.MigrateAsync();

        Assert.Equal("created_at", await database.ScalarAsync<string>(
            "SELECT library_sort_field FROM app_settings WHERE id = 1"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT library_sort_descending FROM app_settings WHERE id = 1"));
        IReadOnlySet<string> columns =
            await database.ReadColumnNamesAsync("app_settings");
        Assert.Contains("library_sort_field", columns);
        Assert.Contains("library_sort_descending", columns);
    }

    [Theory]
    [InlineData(LibrarySortField.CreatedAt, LibrarySortDirection.Descending,
        "created_at", true)]
    [InlineData(LibrarySortField.DisplayName, LibrarySortDirection.Ascending,
        "display_name", false)]
    [InlineData(LibrarySortField.ActualFilename, LibrarySortDirection.Descending,
        "actual_filename", true)]
    [InlineData(LibrarySortField.MediaKind, LibrarySortDirection.Ascending,
        "media_kind", false)]
    public async Task Sort_preference_round_trips_through_existing_settings(
        LibrarySortField field,
        LibrarySortDirection direction,
        string persistedField,
        bool persistedDescending)
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        await database.InsertStage1SettingsAsync(
            "C:\\inbox",
            "C:\\library",
            "C:\\work");
        var store = new EfLibrarySortPreferenceStore(
            database.CreateContextFactory());

        string? error = await store.SaveAsync(
            new LibrarySortPreference(field, direction),
            TestContext.Current.CancellationToken);
        PersistedLibrarySortPreference result = await store.LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.Null(error);
        Assert.Equal(
            new PersistedLibrarySortPreference(
                persistedField,
                persistedDescending),
            result);
    }

    [Fact]
    public async Task Missing_settings_return_default_and_cannot_be_partially_created()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        var store = new EfLibrarySortPreferenceStore(
            database.CreateContextFactory());

        PersistedLibrarySortPreference loaded = await store.LoadAsync(
            TestContext.Current.CancellationToken);
        string? error = await store.SaveAsync(
            LibrarySortPreference.Default,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new PersistedLibrarySortPreference("created_at", true),
            loaded);
        Assert.Equal("settings.directories_required", error);
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM app_settings"));
    }
}
