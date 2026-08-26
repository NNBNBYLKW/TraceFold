using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Application.Library;

public sealed class NamingPresetService(
    INamingPresetStore store) : INamingPresetService
{
    private static readonly IReadOnlySet<string> DefinitionErrorCodes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "naming.rule_invalid",
            "naming.rule_unknown",
            "naming.sequence_invalid",
            "naming.replace_search_empty",
            "naming.remove_text_empty",
            "naming.placeholder_unknown"
        };

    private readonly NamingRuleEngine _engine = new();

    public Task<IReadOnlyList<NamingPresetDto>> ListAsync(
        CancellationToken cancellationToken) =>
        store.ListAsync(cancellationToken);

    public Task<NamingPresetDto?> LoadAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        store.LoadAsync(id, cancellationToken);

    public async Task<NamingPresetSaveResult> SaveAsync(
        NamingPresetSaveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Definition is null)
        {
            return Failure("naming.preset.definition_invalid");
        }

        TaxonomyName name;
        try
        {
            name = TaxonomyName.Create(request.Name);
        }
        catch (ArgumentException)
        {
            return Failure("naming.preset.name_required");
        }

        if (name.Display.Length > 512)
        {
            return Failure("naming.preset.name_too_long");
        }

        if (!IsDefinitionValid(request.Definition))
        {
            return Failure("naming.preset.definition_invalid");
        }

        IReadOnlyList<NamingPresetDto> presets =
            await store.ListAsync(cancellationToken);
        NamingPresetDto? sameName = presets.FirstOrDefault(preset =>
            NormalizeExistingName(preset.Name) == name.Normalized);
        Guid? targetId = request.Id;
        if (sameName is not null && sameName.Id != request.Id)
        {
            if (!request.OverwriteExisting)
            {
                return Failure("naming.preset.name_conflict");
            }

            targetId = sameName.Id;
        }

        return await store.SaveAsync(
            request with { Id = targetId, Name = name.Display },
            cancellationToken);
    }

    public Task<NamingPresetDeleteResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        store.DeleteAsync(id, cancellationToken);

    private bool IsDefinitionValid(NamingDefinition definition)
    {
        foreach (INamingRule rule in definition.Rules)
        {
            NamingPreviewResult preview;
            try
            {
                preview = _engine.Preview(
                    NamingDefinition.Create(
                        definition.Source,
                        [EnableForValidation(rule)]),
                    [
                        new NamingAssetInput(
                            Guid.Parse(
                                "00000000-0000-0000-0000-000000000001"),
                            "__tracefold_validation_source__.media",
                            "TraceFold validation source",
                            ["Author"],
                            "Series",
                            ["Tag"],
                            0)
                    ],
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (preview.Rows
                .SelectMany(row => row.Issues)
                .Any(issue => DefinitionErrorCodes.Contains(issue.Code)))
            {
                return false;
            }
        }

        return true;
    }

    private static INamingRule EnableForValidation(INamingRule rule) =>
        rule switch
        {
            PrefixNamingRule value => value with { Enabled = true },
            SuffixNamingRule value => value with { Enabled = true },
            ReplaceNamingRule value => value with { Enabled = true },
            RemoveTextNamingRule value => value with { Enabled = true },
            RemoveBracketContentNamingRule value =>
                value with { Enabled = true },
            CaseNamingRule value => value with { Enabled = true },
            SequenceNamingRule value => value with { Enabled = true },
            MetadataTemplateNamingRule value => value with { Enabled = true },
            _ => throw new ArgumentException("The naming rule is unsupported.")
        };

    private static string? NormalizeExistingName(string name)
    {
        try
        {
            return TaxonomyName.Create(name).Normalized;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static NamingPresetSaveResult Failure(string errorCode) =>
        new(false, null, errorCode);
}
