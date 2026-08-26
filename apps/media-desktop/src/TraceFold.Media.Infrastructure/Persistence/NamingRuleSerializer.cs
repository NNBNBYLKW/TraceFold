using System.Text;
using System.Text.Json;
using TraceFold.Media.Application.Library;

namespace TraceFold.Media.Infrastructure.Persistence;

internal static class NamingRuleSerializer
{
    private const string UnsupportedVersion =
        "naming.preset.version_unsupported";
    private const string UnsupportedRule =
        "naming.preset.rule_unsupported";

    public static string SerializeSource(NamingSource source) =>
        source switch
        {
            NamingSource.ActualFilename => "actual_filename",
            NamingSource.CurrentDisplayName => "current_display_name",
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };

    public static string SerializeRules(NamingDefinition definition)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (INamingRule rule in definition.Rules)
            {
                WriteRule(writer, rule);
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static NamingRuleDeserializationResult Deserialize(
        int schemaVersion,
        string source,
        string rulesJson)
    {
        if (schemaVersion != NamingDefinition.CurrentSchemaVersion)
        {
            return NamingRuleDeserializationResult.Failure(UnsupportedVersion);
        }

        if (!TryParseSource(source, out NamingSource parsedSource))
        {
            return NamingRuleDeserializationResult.Failure(UnsupportedRule);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(rulesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return NamingRuleDeserializationResult.Failure(UnsupportedRule);
            }

            var rules = new List<INamingRule>();
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                if (!TryReadRule(element, out INamingRule? rule))
                {
                    return NamingRuleDeserializationResult.Failure(
                        UnsupportedRule);
                }

                rules.Add(rule!);
            }

            return NamingRuleDeserializationResult.Success(
                NamingDefinition.Create(parsedSource, rules, schemaVersion));
        }
        catch (Exception error) when (error is JsonException
            or ArgumentException
            or InvalidOperationException
            or OverflowException)
        {
            return NamingRuleDeserializationResult.Failure(UnsupportedRule);
        }
    }

    private static void WriteRule(Utf8JsonWriter writer, INamingRule rule)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", rule.Kind);
        writer.WriteBoolean("enabled", rule.Enabled);
        switch (rule)
        {
            case PrefixNamingRule prefix:
                writer.WriteString("text", prefix.Text);
                break;
            case SuffixNamingRule suffix:
                writer.WriteString("text", suffix.Text);
                break;
            case ReplaceNamingRule replace:
                writer.WriteString("search", replace.Search);
                writer.WriteString("replacement", replace.Replacement);
                writer.WriteBoolean("caseSensitive", replace.CaseSensitive);
                break;
            case RemoveTextNamingRule remove:
                writer.WriteString("text", remove.Text);
                writer.WriteBoolean("caseSensitive", remove.CaseSensitive);
                break;
            case RemoveBracketContentNamingRule bracket:
                writer.WriteString("pair", BracketKey(bracket.Pair));
                break;
            case CaseNamingRule casing:
                writer.WriteString("mode", CaseKey(casing.Mode));
                break;
            case SequenceNamingRule sequence:
                writer.WriteNumber("start", sequence.Start);
                writer.WriteNumber("step", sequence.Step);
                writer.WriteNumber("minimumDigits", sequence.MinimumDigits);
                writer.WriteString(
                    "placement",
                    PlacementKey(sequence.Placement));
                writer.WriteString("separator", sequence.Separator);
                break;
            case MetadataTemplateNamingRule template:
                writer.WriteString("template", template.Template);
                writer.WriteString(
                    "multiValueSeparator",
                    template.MultiValueSeparator);
                writer.WriteNumber("sequenceStart", template.SequenceStart);
                writer.WriteNumber("sequenceStep", template.SequenceStep);
                writer.WriteNumber(
                    "sequenceMinimumDigits",
                    template.SequenceMinimumDigits);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported naming rule kind '{rule.Kind}'.");
        }

        writer.WriteEndObject();
    }

    private static bool TryReadRule(
        JsonElement element,
        out INamingRule? rule)
    {
        rule = null;
        if (element.ValueKind != JsonValueKind.Object
            || !TryGetString(element, "kind", out string? kind)
            || !TryGetBoolean(element, "enabled", out bool enabled))
        {
            return false;
        }

        switch (kind)
        {
            case "prefix":
                if (!HasOnlyProperties(element, "kind", "enabled", "text")
                    || !TryGetString(element, "text", out string? prefixText))
                {
                    return false;
                }

                rule = new PrefixNamingRule(prefixText!, enabled);
                return true;
            case "suffix":
                if (!HasOnlyProperties(element, "kind", "enabled", "text")
                    || !TryGetString(element, "text", out string? suffixText))
                {
                    return false;
                }

                rule = new SuffixNamingRule(suffixText!, enabled);
                return true;
            case "replace":
                if (!HasOnlyProperties(
                        element,
                        "kind", "enabled", "search", "replacement",
                        "caseSensitive")
                    || !TryGetString(element, "search", out string? search)
                    || !TryGetString(
                        element,
                        "replacement",
                        out string? replacement)
                    || !TryGetBoolean(
                        element,
                        "caseSensitive",
                        out bool replaceCaseSensitive)
                    || search!.Length == 0)
                {
                    return false;
                }

                rule = new ReplaceNamingRule(
                    search!, replacement!, replaceCaseSensitive, enabled);
                return true;
            case "remove_text":
                if (!HasOnlyProperties(
                        element,
                        "kind", "enabled", "text", "caseSensitive")
                    || !TryGetString(element, "text", out string? removeText)
                    || !TryGetBoolean(
                        element,
                        "caseSensitive",
                        out bool removeCaseSensitive)
                    || removeText!.Length == 0)
                {
                    return false;
                }

                rule = new RemoveTextNamingRule(
                    removeText!, removeCaseSensitive, enabled);
                return true;
            case "remove_brackets":
                if (!HasOnlyProperties(element, "kind", "enabled", "pair")
                    || !TryGetString(element, "pair", out string? pair)
                    || !TryParseBracket(pair!, out NamingBracketPair bracket))
                {
                    return false;
                }

                rule = new RemoveBracketContentNamingRule(bracket, enabled);
                return true;
            case "case":
                if (!HasOnlyProperties(element, "kind", "enabled", "mode")
                    || !TryGetString(element, "mode", out string? mode)
                    || !TryParseCase(mode!, out NamingCaseMode casing))
                {
                    return false;
                }

                rule = new CaseNamingRule(casing, enabled);
                return true;
            case "sequence":
                if (!HasOnlyProperties(
                        element,
                        "kind", "enabled", "start", "step", "minimumDigits",
                        "placement", "separator")
                    || !TryGetInt32(element, "start", out int start)
                    || !TryGetInt32(element, "step", out int step)
                    || !TryGetInt32(
                        element,
                        "minimumDigits",
                        out int minimumDigits)
                    || !TryGetString(
                        element,
                        "placement",
                        out string? placement)
                    || !TryParsePlacement(
                        placement!,
                        out NamingPlacement parsedPlacement)
                    || !TryGetString(
                        element,
                        "separator",
                        out string? separator)
                    || start < 0
                    || step <= 0
                    || minimumDigits is < 1 or > 10)
                {
                    return false;
                }

                rule = new SequenceNamingRule(
                    start,
                    step,
                    minimumDigits,
                    parsedPlacement,
                    separator!,
                    enabled);
                return true;
            case "metadata_template":
                if (!HasOnlyProperties(
                        element,
                        "kind", "enabled", "template", "multiValueSeparator",
                        "sequenceStart", "sequenceStep",
                        "sequenceMinimumDigits")
                    || !TryGetString(
                        element,
                        "template",
                        out string? template)
                    || !TryGetString(
                        element,
                        "multiValueSeparator",
                        out string? multiValueSeparator)
                    || !TryGetInt32(
                        element,
                        "sequenceStart",
                        out int sequenceStart)
                    || !TryGetInt32(
                        element,
                        "sequenceStep",
                        out int sequenceStep)
                    || !TryGetInt32(
                        element,
                        "sequenceMinimumDigits",
                        out int sequenceMinimumDigits)
                    || sequenceStart < 0
                    || sequenceStep <= 0
                    || sequenceMinimumDigits is < 1 or > 10
                    || !IsValidTemplate(template!))
                {
                    return false;
                }

                rule = new MetadataTemplateNamingRule(
                    template!,
                    multiValueSeparator!,
                    sequenceStart,
                    sequenceStep,
                    sequenceMinimumDigits,
                    enabled);
                return true;
            default:
                return false;
        }
    }

    private static bool HasOnlyProperties(
        JsonElement element,
        params string[] allowed)
    {
        var names = new HashSet<string>(allowed, StringComparer.Ordinal);
        return element.EnumerateObject().All(property => names.Contains(property.Name));
    }

    private static bool TryGetString(
        JsonElement element,
        string name,
        out string? value)
    {
        value = null;
        return element.TryGetProperty(name, out JsonElement property)
            && property.ValueKind == JsonValueKind.String
            && (value = property.GetString()) is not null;
    }

    private static bool TryGetBoolean(
        JsonElement element,
        string name,
        out bool value)
    {
        value = false;
        if (!element.TryGetProperty(name, out JsonElement property)
            || property.ValueKind is not (
                JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool TryGetInt32(
        JsonElement element,
        string name,
        out int value)
    {
        value = 0;
        return element.TryGetProperty(name, out JsonElement property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }

    private static bool TryParseSource(string value, out NamingSource source)
    {
        source = value switch
        {
            "actual_filename" => NamingSource.ActualFilename,
            "current_display_name" => NamingSource.CurrentDisplayName,
            _ => (NamingSource)(-1)
        };
        return Enum.IsDefined(source);
    }

    private static bool IsValidTemplate(string template)
    {
        string[] supportedTokens =
            ["{name}", "{authors}", "{series}", "{tags}", "{number}"];
        for (int index = 0; index < template.Length; index++)
        {
            if (template[index] != '{')
            {
                continue;
            }

            int close = template.IndexOf('}', index + 1);
            if (close < 0
                || !supportedTokens.Contains(
                    template[index..(close + 1)],
                    StringComparer.Ordinal))
            {
                return false;
            }

            index = close;
        }

        return true;
    }

    private static string BracketKey(NamingBracketPair value) => value switch
    {
        NamingBracketPair.Round => "round",
        NamingBracketPair.Square => "square",
        NamingBracketPair.Curly => "curly",
        NamingBracketPair.FullWidthRound => "full_width_round",
        NamingBracketPair.CjkSquare => "cjk_square",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static bool TryParseBracket(
        string value,
        out NamingBracketPair pair)
    {
        pair = value switch
        {
            "round" => NamingBracketPair.Round,
            "square" => NamingBracketPair.Square,
            "curly" => NamingBracketPair.Curly,
            "full_width_round" => NamingBracketPair.FullWidthRound,
            "cjk_square" => NamingBracketPair.CjkSquare,
            _ => (NamingBracketPair)(-1)
        };
        return Enum.IsDefined(pair);
    }

    private static string CaseKey(NamingCaseMode value) => value switch
    {
        NamingCaseMode.Upper => "upper",
        NamingCaseMode.Lower => "lower",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static bool TryParseCase(string value, out NamingCaseMode mode)
    {
        mode = value switch
        {
            "upper" => NamingCaseMode.Upper,
            "lower" => NamingCaseMode.Lower,
            _ => (NamingCaseMode)(-1)
        };
        return Enum.IsDefined(mode);
    }

    private static string PlacementKey(NamingPlacement value) => value switch
    {
        NamingPlacement.Prefix => "prefix",
        NamingPlacement.Suffix => "suffix",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static bool TryParsePlacement(
        string value,
        out NamingPlacement placement)
    {
        placement = value switch
        {
            "prefix" => NamingPlacement.Prefix,
            "suffix" => NamingPlacement.Suffix,
            _ => (NamingPlacement)(-1)
        };
        return Enum.IsDefined(placement);
    }
}

internal sealed record NamingRuleDeserializationResult(
    NamingDefinition? Definition,
    string? ErrorCode)
{
    public static NamingRuleDeserializationResult Success(
        NamingDefinition definition) => new(definition, null);

    public static NamingRuleDeserializationResult Failure(
        string errorCode) => new(null, errorCode);
}
