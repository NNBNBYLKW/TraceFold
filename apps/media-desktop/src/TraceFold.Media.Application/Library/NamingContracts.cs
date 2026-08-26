namespace TraceFold.Media.Application.Library;

public enum NamingSource
{
    ActualFilename,
    CurrentDisplayName
}

public enum NamingPlacement
{
    Prefix,
    Suffix
}

public enum NamingCaseMode
{
    Upper,
    Lower
}

public enum NamingBracketPair
{
    Round,
    Square,
    Curly,
    FullWidthRound,
    CjkSquare
}

public enum NamingIssueSeverity
{
    Warning,
    Error
}

public interface INamingRule
{
    bool Enabled { get; }

    string Kind { get; }
}

public sealed record PrefixNamingRule(string Text, bool Enabled = true)
    : INamingRule
{
    public string Kind => "prefix";
}

public sealed record SuffixNamingRule(string Text, bool Enabled = true)
    : INamingRule
{
    public string Kind => "suffix";
}

public sealed record ReplaceNamingRule(
    string Search,
    string Replacement,
    bool CaseSensitive,
    bool Enabled = true) : INamingRule
{
    public string Kind => "replace";
}

public sealed record RemoveTextNamingRule(
    string Text,
    bool CaseSensitive,
    bool Enabled = true) : INamingRule
{
    public string Kind => "remove_text";
}

public sealed record RemoveBracketContentNamingRule(
    NamingBracketPair Pair,
    bool Enabled = true) : INamingRule
{
    public string Kind => "remove_brackets";
}

public sealed record CaseNamingRule(
    NamingCaseMode Mode,
    bool Enabled = true) : INamingRule
{
    public string Kind => "case";
}

public sealed record SequenceNamingRule(
    int Start,
    int Step,
    int MinimumDigits,
    NamingPlacement Placement,
    string Separator,
    bool Enabled = true) : INamingRule
{
    public string Kind => "sequence";
}

public sealed record MetadataTemplateNamingRule(
    string Template,
    string MultiValueSeparator = "、",
    int SequenceStart = 1,
    int SequenceStep = 1,
    int SequenceMinimumDigits = 1,
    bool Enabled = true) : INamingRule
{
    public string Kind => "metadata_template";
}

public sealed record NamingDefinition
{
    private NamingDefinition(
        int schemaVersion,
        NamingSource source,
        IReadOnlyList<INamingRule> rules)
    {
        SchemaVersion = schemaVersion;
        Source = source;
        Rules = rules;
    }

    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; }

    public NamingSource Source { get; }

    public IReadOnlyList<INamingRule> Rules { get; }

    public static NamingDefinition Create(
        NamingSource source,
        IReadOnlyList<INamingRule> rules,
        int schemaVersion = CurrentSchemaVersion)
    {
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                "The naming schema version is unsupported.");
        }

        ValidateEnum(source, nameof(source));
        ArgumentNullException.ThrowIfNull(rules);
        var copied = new INamingRule[rules.Count];
        for (int index = 0; index < rules.Count; index++)
        {
            INamingRule? rule = rules[index];
            if (rule is null)
            {
                throw new ArgumentException(
                    "A naming rule cannot be null.",
                    nameof(rules));
            }

            ValidateRuleEnums(rule, index);
            copied[index] = rule;
        }

        return new NamingDefinition(
            schemaVersion,
            source,
            Array.AsReadOnly(copied));
    }

    private static void ValidateRuleEnums(INamingRule rule, int index)
    {
        switch (rule)
        {
            case PrefixNamingRule:
            case SuffixNamingRule:
            case ReplaceNamingRule:
            case RemoveTextNamingRule:
            case MetadataTemplateNamingRule:
                return;
            case RemoveBracketContentNamingRule bracket:
                ValidateEnum(bracket.Pair, $"rules[{index}].Pair");
                return;
            case CaseNamingRule casing:
                ValidateEnum(casing.Mode, $"rules[{index}].Mode");
                return;
            case SequenceNamingRule sequence:
                ValidateEnum(sequence.Placement, $"rules[{index}].Placement");
                return;
            default:
                throw new ArgumentException(
                    $"Unsupported naming rule kind at index {index}.",
                    nameof(rule));
        }
    }

    private static void ValidateEnum<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, null);
        }
    }
}

public sealed record NamingAssetInput(
    Guid AssetId,
    string ActualFilename,
    string? ExplicitDisplayName,
    IReadOnlyList<string> Authors,
    string? Series,
    IReadOnlyList<string> Tags,
    int Ordinal);

public sealed record NamingIssue(
    NamingIssueSeverity Severity,
    string Code,
    int? RuleIndex);

public sealed record NamingPreviewRow(
    Guid AssetId,
    int Ordinal,
    string SourceName,
    string? GeneratedName,
    bool IsChanged,
    IReadOnlyList<NamingIssue> Issues);

public sealed record NamingPreviewResult(
    IReadOnlyList<NamingPreviewRow> Rows,
    bool CanApply,
    int WarningCount,
    int ErrorCount);

public interface INamingRuleEngine
{
    NamingPreviewResult Preview(
        NamingDefinition definition,
        IReadOnlyList<NamingAssetInput> assets,
        IReadOnlySet<string> existingDisplayNames);
}
