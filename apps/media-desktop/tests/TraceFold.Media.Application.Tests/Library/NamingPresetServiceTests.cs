using System.Text;
using TraceFold.Media.Application.Library;
using Xunit;

namespace TraceFold.Media.Application.Tests.Library;

public sealed class NamingPresetServiceTests
{
    [Fact]
    public async Task Save_as_rejects_duplicate_name_without_explicit_overwrite()
    {
        var store = new InMemoryNamingPresetStore(Preset("Cleanup"));
        var service = new NamingPresetService(store);

        NamingPresetSaveResult result = await service.SaveAsync(
            new NamingPresetSaveRequest(
                Id: null,
                Name: " cleanup ",
                Definition: Definition(new PrefixNamingRule("x")),
                OverwriteExisting: false),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("naming.preset.name_conflict", result.ErrorCode);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task Explicit_overwrite_preserves_existing_identity_and_created_time()
    {
        DateTimeOffset createdAt = DateTimeOffset.Parse("2026-08-20T01:02:03Z");
        NamingPresetDto existing = Preset("Ｃleanup", createdAt: createdAt);
        var store = new InMemoryNamingPresetStore(existing);
        var service = new NamingPresetService(store);

        NamingPresetSaveResult result = await service.SaveAsync(
            new NamingPresetSaveRequest(
                Id: null,
                Name: " cleanup ",
                Definition: Definition(new SuffixNamingRule("-done")),
                OverwriteExisting: true),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Preset);
        Assert.Equal(existing.Id, result.Preset.Id);
        Assert.Equal(createdAt, result.Preset.CreatedAtUtc);
        Assert.Equal("cleanup", result.Preset.Name);
    }

    [Fact]
    public async Task Unsupported_schema_is_visible_but_not_executable()
    {
        var store = new InMemoryNamingPresetStore(Preset(
            "Future",
            definition: null,
            canExecute: false,
            errorCode: "naming.preset.version_unsupported"));
        var service = new NamingPresetService(store);

        NamingPresetDto preset = Assert.Single(await service.ListAsync(
            TestContext.Current.CancellationToken));

        Assert.False(preset.CanExecute);
        Assert.Null(preset.Definition);
        Assert.Equal("naming.preset.version_unsupported", preset.ErrorCode);
    }

    [Fact]
    public async Task Save_trims_name_and_rejects_invalid_definition()
    {
        var store = new InMemoryNamingPresetStore();
        var service = new NamingPresetService(store);

        NamingPresetSaveResult invalidName = await service.SaveAsync(
            new NamingPresetSaveRequest(
                null,
                "   ",
                Definition(new PrefixNamingRule("x")),
                false),
            TestContext.Current.CancellationToken);
        NamingPresetSaveResult invalidDefinition = await service.SaveAsync(
            new NamingPresetSaveRequest(
                null,
                "Broken",
                Definition(new SequenceNamingRule(
                    1, 0, 1, NamingPlacement.Suffix, "-")),
                false),
            TestContext.Current.CancellationToken);

        Assert.Equal("naming.preset.name_required", invalidName.ErrorCode);
        Assert.Equal(
            "naming.preset.definition_invalid",
            invalidDefinition.ErrorCode);
        Assert.Equal(0, store.SaveCount);
    }

    private static NamingDefinition Definition(params INamingRule[] rules) =>
        NamingDefinition.Create(NamingSource.ActualFilename, rules);

    private static NamingPresetDto Preset(
        string name,
        NamingDefinition? definition = null,
        bool canExecute = true,
        string? errorCode = null,
        DateTimeOffset? createdAt = null)
    {
        DateTimeOffset created = createdAt
            ?? DateTimeOffset.Parse("2026-08-20T00:00:00Z");
        return new NamingPresetDto(
            Guid.NewGuid(),
            name,
            definition ?? (canExecute ? Definition() : null),
            canExecute,
            errorCode,
            created,
            created);
    }

    private sealed class InMemoryNamingPresetStore(
        params NamingPresetDto[] initial) : INamingPresetStore
    {
        private readonly List<NamingPresetDto> _presets = [.. initial];

        public int SaveCount { get; private set; }

        public Task<IReadOnlyList<NamingPresetDto>> ListAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NamingPresetDto>>(_presets.ToArray());

        public Task<NamingPresetDto?> LoadAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(_presets.SingleOrDefault(preset => preset.Id == id));

        public Task<NamingPresetSaveResult> SaveAsync(
            NamingPresetSaveRequest request,
            CancellationToken cancellationToken)
        {
            SaveCount++;
            DateTimeOffset now = DateTimeOffset.Parse("2026-08-21T00:00:00Z");
            int existingIndex = request.Id.HasValue
                ? _presets.FindIndex(preset => preset.Id == request.Id.Value)
                : -1;
            NamingPresetDto saved;
            if (existingIndex >= 0)
            {
                NamingPresetDto existing = _presets[existingIndex];
                saved = existing with
                {
                    Name = request.Name,
                    Definition = request.Definition,
                    CanExecute = true,
                    ErrorCode = null,
                    UpdatedAtUtc = now
                };
                _presets[existingIndex] = saved;
            }
            else
            {
                saved = new NamingPresetDto(
                    request.Id ?? Guid.NewGuid(),
                    request.Name,
                    request.Definition,
                    true,
                    null,
                    now,
                    now);
                _presets.Add(saved);
            }

            return Task.FromResult(new NamingPresetSaveResult(true, saved, null));
        }

        public Task<NamingPresetDeleteResult> DeleteAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            int removed = _presets.RemoveAll(preset => preset.Id == id);
            return Task.FromResult(new NamingPresetDeleteResult(
                removed == 1,
                removed == 1 ? null : "naming.preset.not_found"));
        }
    }
}
