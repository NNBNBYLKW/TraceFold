using System.Globalization;
using System.Text;

namespace TraceFold.Media.Application.Library;

public sealed class NamingRuleEngine : INamingRuleEngine
{
    private const int MaximumDisplayNameLength = 1024;

    public NamingPreviewResult Preview(
        NamingDefinition definition,
        IReadOnlyList<NamingAssetInput> assets,
        IReadOnlySet<string> existingDisplayNames)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(existingDisplayNames);
        ValidateInputs(assets);

        NamingAssetInput[] ordered = assets
            .OrderBy(asset => asset.Ordinal)
            .ToArray();
        NamingPreviewRow[] rows = ordered
            .Select(asset => Evaluate(definition, asset))
            .ToArray();
        AddDuplicateWarnings(rows, existingDisplayNames);

        int warningCount = rows.Sum(row => row.Issues.Count(issue =>
            issue.Severity == NamingIssueSeverity.Warning));
        int errorCount = rows.Sum(row => row.Issues.Count(issue =>
            issue.Severity == NamingIssueSeverity.Error));
        return new NamingPreviewResult(
            Array.AsReadOnly(rows),
            errorCount == 0,
            warningCount,
            errorCount);
    }

    private static NamingPreviewRow Evaluate(
        NamingDefinition definition,
        NamingAssetInput asset)
    {
        string sourceName = GetSourceName(definition.Source, asset);
        string current = sourceName;
        var issues = new List<NamingIssue>();
        int? lastRuleIndex = null;
        for (int index = 0; index < definition.Rules.Count; index++)
        {
            INamingRule rule = definition.Rules[index];
            if (!rule.Enabled)
            {
                continue;
            }

            RuleResult result = ApplyRule(rule, current, asset);
            if (result.ErrorCode is not null)
            {
                issues.Add(Error(result.ErrorCode, index));
                return Row(asset, sourceName, null, false, issues);
            }

            current = result.Value!;
            lastRuleIndex = index;
        }

        string generated = current.Trim();
        if (generated.Length == 0)
        {
            issues.Add(Error("naming.empty", lastRuleIndex));
            return Row(asset, sourceName, null, false, issues);
        }

        if (generated.Length > MaximumDisplayNameLength)
        {
            issues.Add(Error("naming.too_long", lastRuleIndex));
            return Row(asset, sourceName, null, false, issues);
        }

        string currentDisplayName = asset.ExplicitDisplayName
            ?? StripFinalExtension(asset.ActualFilename);
        bool changed = !StringComparer.Ordinal.Equals(
            generated,
            currentDisplayName);
        return Row(asset, sourceName, generated, changed, issues);
    }

    private static RuleResult ApplyRule(
        INamingRule rule,
        string current,
        NamingAssetInput asset) =>
        rule switch
        {
            PrefixNamingRule prefix when prefix.Text is not null =>
                RuleResult.Success(prefix.Text + current),
            PrefixNamingRule => RuleResult.Failure("naming.rule_invalid"),
            SuffixNamingRule suffix when suffix.Text is not null =>
                RuleResult.Success(current + suffix.Text),
            SuffixNamingRule => RuleResult.Failure("naming.rule_invalid"),
            ReplaceNamingRule replace => ApplyReplace(replace, current),
            RemoveTextNamingRule remove => ApplyRemoveText(remove, current),
            RemoveBracketContentNamingRule bracket => RuleResult.Success(
                RemoveBracketContent(current, bracket.Pair)),
            CaseNamingRule { Mode: NamingCaseMode.Upper } =>
                RuleResult.Success(ConvertEnglishAsciiCase(current, upper: true)),
            CaseNamingRule { Mode: NamingCaseMode.Lower } =>
                RuleResult.Success(ConvertEnglishAsciiCase(current, upper: false)),
            CaseNamingRule => RuleResult.Failure("naming.rule_invalid"),
            SequenceNamingRule sequence => ApplySequence(
                sequence,
                current,
                asset.Ordinal),
            MetadataTemplateNamingRule template => ApplyTemplate(
                template,
                current,
                asset),
            _ => RuleResult.Failure("naming.rule_unknown")
        };

    private static string ConvertEnglishAsciiCase(string value, bool upper)
    {
        char[] characters = value.ToCharArray();
        for (int index = 0; index < characters.Length; index++)
        {
            char character = characters[index];
            if (upper && character is >= 'a' and <= 'z')
            {
                characters[index] = (char)(character - ('a' - 'A'));
            }
            else if (!upper && character is >= 'A' and <= 'Z')
            {
                characters[index] = (char)(character + ('a' - 'A'));
            }
        }

        return new string(characters);
    }

    private static RuleResult ApplyReplace(
        ReplaceNamingRule rule,
        string current)
    {
        if (string.IsNullOrEmpty(rule.Search))
        {
            return RuleResult.Failure("naming.replace_search_empty");
        }

        if (rule.Replacement is null)
        {
            return RuleResult.Failure("naming.rule_invalid");
        }

        return RuleResult.Success(current.Replace(
            rule.Search,
            rule.Replacement,
            rule.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase));
    }

    private static RuleResult ApplyRemoveText(
        RemoveTextNamingRule rule,
        string current)
    {
        if (string.IsNullOrEmpty(rule.Text))
        {
            return RuleResult.Failure("naming.remove_text_empty");
        }

        return RuleResult.Success(current.Replace(
            rule.Text,
            string.Empty,
            rule.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase));
    }

    private static RuleResult ApplySequence(
        SequenceNamingRule rule,
        string current,
        int ordinal)
    {
        if (!IsValidSequence(
                rule.Start,
                rule.Step,
                rule.MinimumDigits,
                rule.Separator))
        {
            return RuleResult.Failure("naming.sequence_invalid");
        }

        if (!TryFormatSequence(
                rule.Start,
                rule.Step,
                rule.MinimumDigits,
                ordinal,
                out string? number))
        {
            return RuleResult.Failure("naming.sequence_invalid");
        }

        return rule.Placement switch
        {
            NamingPlacement.Prefix =>
                RuleResult.Success(number + rule.Separator + current),
            NamingPlacement.Suffix =>
                RuleResult.Success(current + rule.Separator + number),
            _ => RuleResult.Failure("naming.sequence_invalid")
        };
    }

    private static RuleResult ApplyTemplate(
        MetadataTemplateNamingRule rule,
        string current,
        NamingAssetInput asset)
    {
        if (rule.Template is null || rule.MultiValueSeparator is null)
        {
            return RuleResult.Failure("naming.rule_invalid");
        }

        var result = new StringBuilder(rule.Template.Length);
        for (int index = 0; index < rule.Template.Length; index++)
        {
            char value = rule.Template[index];
            if (value != '{')
            {
                result.Append(value);
                continue;
            }

            int close = rule.Template.IndexOf('}', index + 1);
            if (close < 0)
            {
                return RuleResult.Failure("naming.placeholder_unknown");
            }

            string token = rule.Template[index..(close + 1)];
            string? replacement;
            switch (token)
            {
                case "{name}":
                    replacement = current;
                    break;
                case "{authors}":
                    if (asset.Authors.Count == 0)
                    {
                        return RuleResult.Failure("naming.placeholder_missing");
                    }

                    replacement = string.Join(rule.MultiValueSeparator, asset.Authors);
                    break;
                case "{series}":
                    if (string.IsNullOrWhiteSpace(asset.Series))
                    {
                        return RuleResult.Failure("naming.placeholder_missing");
                    }

                    replacement = asset.Series;
                    break;
                case "{tags}":
                    if (asset.Tags.Count == 0)
                    {
                        return RuleResult.Failure("naming.placeholder_missing");
                    }

                    replacement = string.Join(rule.MultiValueSeparator, asset.Tags);
                    break;
                case "{number}":
                    if (!IsValidSequence(
                            rule.SequenceStart,
                            rule.SequenceStep,
                            rule.SequenceMinimumDigits,
                            string.Empty)
                        || !TryFormatSequence(
                            rule.SequenceStart,
                            rule.SequenceStep,
                            rule.SequenceMinimumDigits,
                            asset.Ordinal,
                            out replacement))
                    {
                        return RuleResult.Failure("naming.sequence_invalid");
                    }

                    break;
                default:
                    return RuleResult.Failure("naming.placeholder_unknown");
            }

            result.Append(replacement);
            index = close;
        }

        return RuleResult.Success(result.ToString());
    }

    private static string RemoveBracketContent(
        string current,
        NamingBracketPair pair)
    {
        (char open, char close) = pair switch
        {
            NamingBracketPair.Round => ('(', ')'),
            NamingBracketPair.Square => ('[', ']'),
            NamingBracketPair.Curly => ('{', '}'),
            NamingBracketPair.FullWidthRound => ('（', '）'),
            NamingBracketPair.CjkSquare => ('【', '】'),
            _ => throw new ArgumentOutOfRangeException(nameof(pair))
        };

        var result = new StringBuilder(current.Length);
        int searchFrom = 0;
        while (searchFrom < current.Length)
        {
            int opening = current.IndexOf(open, searchFrom);
            if (opening < 0)
            {
                result.Append(current, searchFrom, current.Length - searchFrom);
                break;
            }

            int closing = current.IndexOf(close, opening + 1);
            if (closing < 0)
            {
                result.Append(current, searchFrom, current.Length - searchFrom);
                break;
            }

            result.Append(current, searchFrom, opening - searchFrom);
            searchFrom = closing + 1;
        }

        return result.ToString();
    }

    private static bool IsValidSequence(
        int start,
        int step,
        int minimumDigits,
        string? separator) =>
        start >= 0
        && step > 0
        && minimumDigits is >= 1 and <= 10
        && separator is not null;

    private static bool TryFormatSequence(
        int start,
        int step,
        int minimumDigits,
        int ordinal,
        out string? formatted)
    {
        try
        {
            int number = checked(start + checked(ordinal * step));
            formatted = number.ToString(
                $"D{minimumDigits}",
                CultureInfo.InvariantCulture);
            return true;
        }
        catch (OverflowException)
        {
            formatted = null;
            return false;
        }
    }

    private static string GetSourceName(
        NamingSource source,
        NamingAssetInput asset) =>
        source switch
        {
            NamingSource.ActualFilename =>
                StripFinalExtension(asset.ActualFilename),
            NamingSource.CurrentDisplayName =>
                asset.ExplicitDisplayName
                ?? StripFinalExtension(asset.ActualFilename),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };

    private static string StripFinalExtension(string actualFilename) =>
        Path.GetFileNameWithoutExtension(actualFilename);

    private static void ValidateInputs(IReadOnlyList<NamingAssetInput> assets)
    {
        var ids = new HashSet<Guid>();
        var ordinals = new HashSet<int>();
        foreach (NamingAssetInput? asset in assets)
        {
            if (asset is null
                || asset.AssetId == Guid.Empty
                || asset.ActualFilename is null
                || asset.Authors is null
                || asset.Tags is null
                || asset.Authors.Any(value => value is null)
                || asset.Tags.Any(value => value is null)
                || asset.Ordinal < 0)
            {
                throw new ArgumentException(
                    "A naming asset input is invalid.",
                    nameof(assets));
            }

            if (!ids.Add(asset.AssetId) || !ordinals.Add(asset.Ordinal))
            {
                throw new ArgumentException(
                    "Naming asset ids and ordinals must be unique.",
                    nameof(assets));
            }
        }
    }

    private static void AddDuplicateWarnings(
        NamingPreviewRow[] rows,
        IReadOnlySet<string> existingDisplayNames)
    {
        HashSet<string> existing = existingDisplayNames
            .Where(name => name is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> duplicateBatchNames = rows
            .Where(row => row.GeneratedName is not null)
            .GroupBy(row => row.GeneratedName!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < rows.Length; index++)
        {
            NamingPreviewRow row = rows[index];
            if (row.GeneratedName is null)
            {
                continue;
            }

            var issues = row.Issues.ToList();
            if (duplicateBatchNames.Contains(row.GeneratedName))
            {
                issues.Add(Warning("naming.duplicate_batch"));
            }

            if (existing.Contains(row.GeneratedName))
            {
                issues.Add(Warning("naming.duplicate_existing"));
            }

            rows[index] = row with { Issues = issues.AsReadOnly() };
        }
    }

    private static NamingPreviewRow Row(
        NamingAssetInput asset,
        string sourceName,
        string? generatedName,
        bool changed,
        List<NamingIssue> issues) =>
        new(
            asset.AssetId,
            asset.Ordinal,
            sourceName,
            generatedName,
            changed,
            issues.AsReadOnly());

    private static NamingIssue Error(string code, int? ruleIndex) =>
        new(NamingIssueSeverity.Error, code, ruleIndex);

    private static NamingIssue Warning(string code) =>
        new(NamingIssueSeverity.Warning, code, null);

    private sealed record RuleResult(string? Value, string? ErrorCode)
    {
        public static RuleResult Success(string value) => new(value, null);

        public static RuleResult Failure(string errorCode) => new(null, errorCode);
    }
}
