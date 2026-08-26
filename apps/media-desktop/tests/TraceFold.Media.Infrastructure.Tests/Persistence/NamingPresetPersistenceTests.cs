using TraceFold.Media.Application.Library;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Persistence;

public sealed class NamingPresetPersistenceTests
{
    [Fact]
    public async Task Every_rule_round_trips_with_order_and_enabled_state()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        var store = new EfNamingPresetStore(database.CreateContextFactory());
        NamingDefinition definition = NamingDefinition.Create(
            NamingSource.ActualFilename,
            [
                new PrefixNamingRule("pre-"),
                new SuffixNamingRule("-post", Enabled: false),
                new ReplaceNamingRule("old", "new", CaseSensitive: false),
                new RemoveTextNamingRule("draft", CaseSensitive: true),
                new RemoveBracketContentNamingRule(NamingBracketPair.Round),
                new CaseNamingRule(NamingCaseMode.Lower),
                new SequenceNamingRule(
                    1, 1, 3, NamingPlacement.Suffix, "-"),
                new MetadataTemplateNamingRule(
                    "{series}-{authors}-{number}")
            ]);

        NamingPresetDto? saved = (await store.SaveAsync(
            new NamingPresetSaveRequest(
                null, "Every rule", definition, OverwriteExisting: false),
            TestContext.Current.CancellationToken)).Preset;
        Assert.NotNull(saved);
        NamingPresetDto? loaded = await store.LoadAsync(
            saved.Id,
            TestContext.Current.CancellationToken);
        Assert.NotNull(loaded);

        Assert.True(loaded.CanExecute);
        Assert.Null(loaded.ErrorCode);
        Assert.Equal(definition.SchemaVersion, loaded.Definition!.SchemaVersion);
        Assert.Equal(definition.Source, loaded.Definition.Source);
        Assert.Equal(definition.Rules, loaded.Definition.Rules);
    }

    [Fact]
    public async Task Normalized_name_is_unique_and_overwrite_preserves_identity()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        var store = new EfNamingPresetStore(database.CreateContextFactory());
        NamingDefinition first = Definition(new PrefixNamingRule("first-"));
        NamingDefinition second = Definition(new SuffixNamingRule("-second"));
        NamingPresetDto? saved = (await store.SaveAsync(
            new NamingPresetSaveRequest(null, "Ｃleanup", first, false),
            TestContext.Current.CancellationToken)).Preset;
        Assert.NotNull(saved);

        NamingPresetSaveResult conflict = await store.SaveAsync(
            new NamingPresetSaveRequest(null, " cleanup ", second, false),
            TestContext.Current.CancellationToken);
        NamingPresetSaveResult overwritten = await store.SaveAsync(
            new NamingPresetSaveRequest(null, " cleanup ", second, true),
            TestContext.Current.CancellationToken);

        Assert.False(conflict.Succeeded);
        Assert.Equal("naming.preset.name_conflict", conflict.ErrorCode);
        Assert.True(overwritten.Succeeded);
        Assert.Equal(saved.Id, overwritten.Preset!.Id);
        Assert.Equal(saved.CreatedAtUtc, overwritten.Preset.CreatedAtUtc);
        Assert.Equal("cleanup", overwritten.Preset.Name);
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM naming_presets"));
    }

    [Theory]
    [InlineData(2, "actual_filename", "[]", "naming.preset.version_unsupported")]
    [InlineData(1, "actual_filename", "[{\"kind\":\"future\",\"enabled\":true}]", "naming.preset.rule_unsupported")]
    [InlineData(1, "actual_filename", "[{\"kind\":\"sequence\",\"enabled\":false,\"start\":1,\"step\":0,\"minimumDigits\":1,\"placement\":\"suffix\",\"separator\":\"-\"}]", "naming.preset.rule_unsupported")]
    [InlineData(1, "actual_filename", "not json", "naming.preset.rule_unsupported")]
    public async Task Unsupported_or_malformed_content_is_visible_but_not_executable(
        int schemaVersion,
        string source,
        string rulesJson,
        string expectedError)
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        Guid id = Guid.NewGuid();
        string persistedId = id.ToString("D").ToUpperInvariant();
        string escapedRulesJson = rulesJson
            .Replace("'", "''", StringComparison.Ordinal)
            .Replace("{", "{{", StringComparison.Ordinal)
            .Replace("}", "}}", StringComparison.Ordinal);
        await database.ExecuteSqlAsync($"""
            INSERT INTO naming_presets
                (id, display_name, normalized_name, schema_version, source,
                 rules_json, created_at_utc, updated_at_utc)
            VALUES
                ('{persistedId}', 'Future', 'FUTURE', {schemaVersion}, '{source}',
                 '{escapedRulesJson}',
                 '2026-08-20T00:00:00+00:00',
                 '2026-08-20T00:00:00+00:00')
            """);
        var store = new EfNamingPresetStore(database.CreateContextFactory());

        NamingPresetDto? loaded = await store.LoadAsync(
            id,
            TestContext.Current.CancellationToken);
        Assert.NotNull(loaded);

        Assert.False(loaded.CanExecute);
        Assert.Null(loaded.Definition);
        Assert.Equal(expectedError, loaded.ErrorCode);
    }

    [Fact]
    public async Task Deleting_a_preset_does_not_change_formal_assets()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        await database.SeedFormalAssetAsync("keep.jpg", null, 0);
        var store = new EfNamingPresetStore(database.CreateContextFactory());
        NamingPresetDto? saved = (await store.SaveAsync(
            new NamingPresetSaveRequest(
                null, "Temporary", Definition(), false),
            TestContext.Current.CancellationToken)).Preset;
        Assert.NotNull(saved);

        NamingPresetDeleteResult result = await store.DeleteAsync(
            saved.Id,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM naming_presets"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM media_assets"));
    }

    private static NamingDefinition Definition(params INamingRule[] rules) =>
        NamingDefinition.Create(NamingSource.ActualFilename, rules);
}
