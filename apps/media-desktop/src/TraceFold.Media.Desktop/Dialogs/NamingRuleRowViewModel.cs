using CommunityToolkit.Mvvm.ComponentModel;
using TraceFold.Media.Application.Library;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed class NamingRuleRowViewModel : ObservableObject
{
    private INamingRule _rule;
    private int _displayIndex;

    internal NamingRuleRowViewModel(
        Guid dialogId,
        int automationIndex,
        int displayIndex,
        INamingRule rule)
    {
        if (dialogId == Guid.Empty || automationIndex < 1 || displayIndex < 1)
        {
            throw new ArgumentException("A valid dialog rule identity is required.");
        }

        ArgumentNullException.ThrowIfNull(rule);
        _rule = rule;
        _displayIndex = displayIndex;
        AutomationId = $"LibraryNamingRule-{dialogId:N}-{automationIndex}";
    }

    public event EventHandler? RuleChanged;

    public string AutomationId { get; }

    public int DisplayIndex
    {
        get => _displayIndex;
        internal set => SetProperty(ref _displayIndex, value);
    }

    public INamingRule Rule
    {
        get => _rule;
        private set
        {
            if (!SetProperty(ref _rule, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(Kind));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ParameterSummary));
            NotifyParameterProperties();
            RuleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsEnabled
    {
        get => Rule.Enabled;
        set
        {
            if (value == Rule.Enabled)
            {
                return;
            }

            Rule = WithEnabled(Rule, value);
        }
    }

    public string Kind => Rule.Kind;

    public string DisplayName => Rule switch
    {
        PrefixNamingRule => "添加前缀",
        SuffixNamingRule => "添加后缀",
        ReplaceNamingRule => "替换文本",
        RemoveTextNamingRule => "移除文本",
        RemoveBracketContentNamingRule => "移除括号内容",
        CaseNamingRule => "字母大小写",
        SequenceNamingRule => "添加序号",
        MetadataTemplateNamingRule => "资料模板",
        _ => "未知规则"
    };

    public string ParameterSummary => Rule switch
    {
        PrefixNamingRule value => value.Text,
        SuffixNamingRule value => value.Text,
        ReplaceNamingRule value => $"{value.Search} → {value.Replacement}",
        RemoveTextNamingRule value => value.Text,
        RemoveBracketContentNamingRule value => value.Pair.ToString(),
        CaseNamingRule value => value.Mode.ToString(),
        SequenceNamingRule value =>
            $"{value.Start}, +{value.Step}, {value.MinimumDigits} 位",
        MetadataTemplateNamingRule value => value.Template,
        _ => string.Empty
    };

    public string TextParameter
    {
        get => Rule switch
        {
            PrefixNamingRule value => value.Text,
            SuffixNamingRule value => value.Text,
            RemoveTextNamingRule value => value.Text,
            _ => string.Empty
        };
        set => Rule = Rule switch
        {
            PrefixNamingRule current => current with { Text = value },
            SuffixNamingRule current => current with { Text = value },
            RemoveTextNamingRule current => current with { Text = value },
            _ => throw WrongParameter()
        };
    }

    public string SearchParameter
    {
        get => Rule is ReplaceNamingRule value ? value.Search : string.Empty;
        set => Rule = Rule is ReplaceNamingRule current
            ? current with { Search = value }
            : throw WrongParameter();
    }

    public string ReplacementParameter
    {
        get => Rule is ReplaceNamingRule value
            ? value.Replacement
            : string.Empty;
        set => Rule = Rule is ReplaceNamingRule current
            ? current with { Replacement = value }
            : throw WrongParameter();
    }

    public bool CaseSensitiveParameter
    {
        get => Rule switch
        {
            ReplaceNamingRule value => value.CaseSensitive,
            RemoveTextNamingRule value => value.CaseSensitive,
            _ => false
        };
        set => Rule = Rule switch
        {
            ReplaceNamingRule current => current with { CaseSensitive = value },
            RemoveTextNamingRule current => current with { CaseSensitive = value },
            _ => throw WrongParameter()
        };
    }

    public NamingBracketPair BracketPairParameter
    {
        get => Rule is RemoveBracketContentNamingRule value
            ? value.Pair
            : NamingBracketPair.Round;
        set => Rule = Rule is RemoveBracketContentNamingRule current
            ? current with { Pair = value }
            : throw WrongParameter();
    }

    public NamingCaseMode CaseModeParameter
    {
        get => Rule is CaseNamingRule value
            ? value.Mode
            : NamingCaseMode.Upper;
        set => Rule = Rule is CaseNamingRule current
            ? current with { Mode = value }
            : throw WrongParameter();
    }

    public int SequenceStartParameter
    {
        get => Rule switch
        {
            SequenceNamingRule value => value.Start,
            MetadataTemplateNamingRule value => value.SequenceStart,
            _ => 1
        };
        set => Rule = Rule switch
        {
            SequenceNamingRule current => current with { Start = value },
            MetadataTemplateNamingRule current =>
                current with { SequenceStart = value },
            _ => throw WrongParameter()
        };
    }

    public int SequenceStepParameter
    {
        get => Rule switch
        {
            SequenceNamingRule value => value.Step,
            MetadataTemplateNamingRule value => value.SequenceStep,
            _ => 1
        };
        set => Rule = Rule switch
        {
            SequenceNamingRule current => current with { Step = value },
            MetadataTemplateNamingRule current =>
                current with { SequenceStep = value },
            _ => throw WrongParameter()
        };
    }

    public int SequenceMinimumDigitsParameter
    {
        get => Rule switch
        {
            SequenceNamingRule value => value.MinimumDigits,
            MetadataTemplateNamingRule value => value.SequenceMinimumDigits,
            _ => 1
        };
        set => Rule = Rule switch
        {
            SequenceNamingRule current => current with { MinimumDigits = value },
            MetadataTemplateNamingRule current =>
                current with { SequenceMinimumDigits = value },
            _ => throw WrongParameter()
        };
    }

    public NamingPlacement SequencePlacementParameter
    {
        get => Rule is SequenceNamingRule value
            ? value.Placement
            : NamingPlacement.Prefix;
        set => Rule = Rule is SequenceNamingRule current
            ? current with { Placement = value }
            : throw WrongParameter();
    }

    public string SequenceSeparatorParameter
    {
        get => Rule is SequenceNamingRule value ? value.Separator : string.Empty;
        set => Rule = Rule is SequenceNamingRule current
            ? current with { Separator = value }
            : throw WrongParameter();
    }

    public string TemplateParameter
    {
        get => Rule is MetadataTemplateNamingRule value
            ? value.Template
            : string.Empty;
        set => Rule = Rule is MetadataTemplateNamingRule current
            ? current with { Template = value }
            : throw WrongParameter();
    }

    public string MultiValueSeparatorParameter
    {
        get => Rule is MetadataTemplateNamingRule value
            ? value.MultiValueSeparator
            : "、";
        set => Rule = Rule is MetadataTemplateNamingRule current
            ? current with { MultiValueSeparator = value }
            : throw WrongParameter();
    }

    public bool IsTextRule => Rule is
        PrefixNamingRule or SuffixNamingRule or RemoveTextNamingRule;

    public bool IsReplaceRule => Rule is ReplaceNamingRule;

    public bool IsBracketRule => Rule is RemoveBracketContentNamingRule;

    public bool IsCaseRule => Rule is CaseNamingRule;

    public bool IsSequenceRule => Rule is SequenceNamingRule;

    public bool IsTemplateRule => Rule is MetadataTemplateNamingRule;

    public void UpdateRule(INamingRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        Rule = rule;
    }

    private static INamingRule WithEnabled(INamingRule rule, bool enabled) =>
        rule switch
        {
            PrefixNamingRule value => value with { Enabled = enabled },
            SuffixNamingRule value => value with { Enabled = enabled },
            ReplaceNamingRule value => value with { Enabled = enabled },
            RemoveTextNamingRule value => value with { Enabled = enabled },
            RemoveBracketContentNamingRule value =>
                value with { Enabled = enabled },
            CaseNamingRule value => value with { Enabled = enabled },
            SequenceNamingRule value => value with { Enabled = enabled },
            MetadataTemplateNamingRule value =>
                value with { Enabled = enabled },
            _ => throw new ArgumentException("The naming rule is unsupported.")
        };

    private void NotifyParameterProperties()
    {
        OnPropertyChanged(nameof(TextParameter));
        OnPropertyChanged(nameof(SearchParameter));
        OnPropertyChanged(nameof(ReplacementParameter));
        OnPropertyChanged(nameof(CaseSensitiveParameter));
        OnPropertyChanged(nameof(BracketPairParameter));
        OnPropertyChanged(nameof(CaseModeParameter));
        OnPropertyChanged(nameof(SequenceStartParameter));
        OnPropertyChanged(nameof(SequenceStepParameter));
        OnPropertyChanged(nameof(SequenceMinimumDigitsParameter));
        OnPropertyChanged(nameof(SequencePlacementParameter));
        OnPropertyChanged(nameof(SequenceSeparatorParameter));
        OnPropertyChanged(nameof(TemplateParameter));
        OnPropertyChanged(nameof(MultiValueSeparatorParameter));
        OnPropertyChanged(nameof(IsTextRule));
        OnPropertyChanged(nameof(IsReplaceRule));
        OnPropertyChanged(nameof(IsBracketRule));
        OnPropertyChanged(nameof(IsCaseRule));
        OnPropertyChanged(nameof(IsSequenceRule));
        OnPropertyChanged(nameof(IsTemplateRule));
    }

    private static InvalidOperationException WrongParameter() =>
        new("The parameter does not apply to this naming rule.");
}
