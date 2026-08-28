using System.Globalization;
using TraceFold.Media.Application.Library;
using Xunit;

namespace TraceFold.Media.Application.Tests.Library;

public sealed class NamingRuleEngineTests
{
    [Fact]
    public void Actual_filename_source_strips_only_the_final_extension()
    {
        NamingPreviewRow row = Preview(
            actualFilename: "archive.part.webp",
            explicitDisplayName: null,
            NamingSource.ActualFilename,
            [new SuffixNamingRule("-done")]);

        Assert.Equal("archive.part-done", row.GeneratedName);
    }

    [Fact]
    public void Rules_execute_in_user_order_and_disabled_rules_are_skipped()
    {
        NamingPreviewRow row = Preview(
            "photo.jpg",
            null,
            NamingSource.ActualFilename,
            [
                new PrefixNamingRule("A-"),
                new ReplaceNamingRule(
                    "photo", "image", CaseSensitive: false),
                new SuffixNamingRule("-ignored", Enabled: false),
                new CaseNamingRule(NamingCaseMode.Upper)
            ]);

        Assert.Equal("A-IMAGE", row.GeneratedName);
    }

    [Fact]
    public void Template_uses_stable_metadata_order_and_selection_ordinal()
    {
        NamingPreviewRow row = PreviewAsset(
            new NamingAssetInput(
                Guid.NewGuid(),
                "clip.mp4",
                null,
                Authors: ["Alice", "Bob"],
                Series: "旅行",
                Tags: ["夜景", "城市"],
                Ordinal: 2),
            new MetadataTemplateNamingRule(
                "{series} - {authors} - {number}",
                MultiValueSeparator: "、",
                SequenceStart: 1,
                SequenceStep: 1,
                SequenceMinimumDigits: 3));

        Assert.Equal("旅行 - Alice、Bob - 003", row.GeneratedName);
    }

    [Theory]
    [InlineData("", "naming.empty")]
    [InlineData("{series}", "naming.placeholder_missing")]
    [InlineData("{unknown}", "naming.placeholder_unknown")]
    public void Invalid_result_blocks_the_batch(string template, string errorCode)
    {
        NamingPreviewResult result = PreviewBatch(
            [Asset(series: null)],
            [new MetadataTemplateNamingRule(template)]);

        Assert.False(result.CanApply);
        Assert.Contains(result.Rows[0].Issues, issue => issue.Code == errorCode);
    }

    [Fact]
    public void Length_boundary_accepts_1024_and_rejects_1025()
    {
        NamingPreviewResult accepted = PreviewBatch(
            [Asset()],
            [new MetadataTemplateNamingRule(new string('a', 1024))]);
        NamingPreviewResult rejected = PreviewBatch(
            [Asset()],
            [new MetadataTemplateNamingRule(new string('a', 1025))]);

        Assert.True(accepted.CanApply);
        Assert.Equal(1024, accepted.Rows[0].GeneratedName?.Length);
        Assert.False(rejected.CanApply);
        Assert.Contains(rejected.Rows[0].Issues,
            issue => issue.Code == "naming.too_long");
    }

    [Fact]
    public void Literal_replace_honors_case_sensitivity()
    {
        NamingPreviewRow sensitive = Preview(
            "Photo-photo.jpg",
            null,
            NamingSource.ActualFilename,
            [new ReplaceNamingRule("photo", "image", CaseSensitive: true)]);
        NamingPreviewRow insensitive = Preview(
            "Photo-photo.jpg",
            null,
            NamingSource.ActualFilename,
            [new ReplaceNamingRule("photo", "image", CaseSensitive: false)]);

        Assert.Equal("Photo-image", sensitive.GeneratedName);
        Assert.Equal("image-image", insensitive.GeneratedName);
    }

    [Fact]
    public void Literal_removal_does_not_use_regular_expressions()
    {
        NamingPreviewRow row = Preview(
            "a.+b.+c.jpg",
            null,
            NamingSource.ActualFilename,
            [new RemoveTextNamingRule(".+", CaseSensitive: true)]);

        Assert.Equal("abc", row.GeneratedName);
    }

    [Theory]
    [InlineData(NamingBracketPair.Round, "a(one)b", "ab")]
    [InlineData(NamingBracketPair.Square, "a[one]b", "ab")]
    [InlineData(NamingBracketPair.Curly, "a{one}b", "ab")]
    [InlineData(NamingBracketPair.FullWidthRound, "a（one）b", "ab")]
    [InlineData(NamingBracketPair.CjkSquare, "a【one】b", "ab")]
    public void Every_supported_bracket_pair_is_removed(
        NamingBracketPair pair,
        string input,
        string expected)
    {
        NamingPreviewRow row = Preview(
            $"{input}.jpg",
            null,
            NamingSource.ActualFilename,
            [new RemoveBracketContentNamingRule(pair)]);

        Assert.Equal(expected, row.GeneratedName);
    }

    [Fact]
    public void Unmatched_brackets_are_retained()
    {
        NamingPreviewRow row = Preview(
            "a(unmatched.jpg",
            null,
            NamingSource.ActualFilename,
            [new RemoveBracketContentNamingRule(NamingBracketPair.Round)]);

        Assert.Equal("a(unmatched", row.GeneratedName);
    }

    [Fact]
    public void Case_conversion_is_invariant()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            NamingPreviewRow upper = Preview(
                "image.jpg", null, NamingSource.ActualFilename,
                [new CaseNamingRule(NamingCaseMode.Upper)]);
            NamingPreviewRow lower = Preview(
                "IMAGE.jpg", null, NamingSource.ActualFilename,
                [new CaseNamingRule(NamingCaseMode.Lower)]);

            Assert.Equal("IMAGE", upper.GeneratedName);
            Assert.Equal("image", lower.GeneratedName);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(
        NamingCaseMode.Upper,
        "abc-Привет-Σίσυφος-中文.jpg",
        "ABC-Привет-Σίσυφος-中文")]
    [InlineData(
        NamingCaseMode.Lower,
        "ABC-Привет-Σίσυφος-中文.jpg",
        "abc-Привет-Σίσυφος-中文")]
    public void Case_conversion_changes_only_english_ascii_letters(
        NamingCaseMode mode,
        string filename,
        string expected)
    {
        NamingPreviewRow row = Preview(
            filename,
            null,
            NamingSource.ActualFilename,
            [new CaseNamingRule(mode)]);

        Assert.Equal(expected, row.GeneratedName);
    }

    [Fact]
    public void Sequence_supports_prefix_suffix_start_step_and_padding()
    {
        NamingAssetInput input = Asset(ordinal: 2);
        NamingPreviewRow prefix = PreviewAsset(
            input,
            new SequenceNamingRule(
                5, 2, 3, NamingPlacement.Prefix, "-"));
        NamingPreviewRow suffix = PreviewAsset(
            input,
            new SequenceNamingRule(
                5, 2, 3, NamingPlacement.Suffix, "-"));

        Assert.Equal("009-photo", prefix.GeneratedName);
        Assert.Equal("photo-009", suffix.GeneratedName);
    }

    [Fact]
    public void Current_name_source_falls_back_to_extensionless_actual_filename()
    {
        NamingPreviewRow explicitName = Preview(
            "photo.jpg",
            "Custom.ext",
            NamingSource.CurrentDisplayName,
            [new SuffixNamingRule("-done")]);
        NamingPreviewRow fallback = Preview(
            "archive.part.webp",
            null,
            NamingSource.CurrentDisplayName,
            []);

        Assert.Equal("Custom.ext-done", explicitName.GeneratedName);
        Assert.Equal("archive.part", fallback.GeneratedName);
    }

    [Fact]
    public void Duplicate_names_are_warnings_not_blockers()
    {
        NamingAssetInput first = Asset(id: Guid.NewGuid(), filename: "one.jpg");
        NamingAssetInput second = Asset(
            id: Guid.NewGuid(), filename: "two.jpg", ordinal: 1);
        NamingPreviewResult result = PreviewBatch(
            [first, second],
            [new MetadataTemplateNamingRule("Same")],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "same",
                "Elsewhere"
            });

        Assert.True(result.CanApply);
        Assert.Equal(4, result.WarningCount);
        Assert.All(result.Rows, row =>
        {
            Assert.Contains(row.Issues,
                issue => issue.Code == "naming.duplicate_batch");
            Assert.Contains(row.Issues,
                issue => issue.Code == "naming.duplicate_existing");
        });
    }

    [Fact]
    public void No_op_row_is_valid_and_not_changed()
    {
        NamingPreviewRow row = Preview(
            "photo.jpg", null, NamingSource.ActualFilename, []);

        Assert.Equal("photo", row.GeneratedName);
        Assert.False(row.IsChanged);
        Assert.Empty(row.Issues);
    }

    [Theory]
    [InlineData(-1, 1, 1)]
    [InlineData(0, 0, 1)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 1, 11)]
    public void Invalid_sequence_parameters_are_reported(
        int start,
        int step,
        int digits)
    {
        NamingPreviewResult result = PreviewBatch(
            [Asset()],
            [new SequenceNamingRule(
                start,
                step,
                digits,
                NamingPlacement.Prefix,
                "-")]);

        Assert.False(result.CanApply);
        Assert.Contains(result.Rows[0].Issues,
            issue => issue.Code == "naming.sequence_invalid"
                && issue.RuleIndex == 0);
    }

    [Fact]
    public void Empty_literal_search_values_are_reported()
    {
        NamingPreviewResult replace = PreviewBatch(
            [Asset()],
            [new ReplaceNamingRule("", "x", CaseSensitive: true)]);
        NamingPreviewResult remove = PreviewBatch(
            [Asset()],
            [new RemoveTextNamingRule("", CaseSensitive: true)]);

        Assert.Contains(replace.Rows[0].Issues,
            issue => issue.Code == "naming.replace_search_empty");
        Assert.Contains(remove.Rows[0].Issues,
            issue => issue.Code == "naming.remove_text_empty");
    }

    [Fact]
    public void Definition_rejects_undefined_enum_parameters_and_null_rules()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NamingDefinition.Create(
                (NamingSource)999,
                []));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NamingDefinition.Create(
                NamingSource.ActualFilename,
                [new CaseNamingRule((NamingCaseMode)999)]));
        Assert.Throws<ArgumentException>(() =>
            NamingDefinition.Create(
                NamingSource.ActualFilename,
                [null!]));
    }

    [Fact]
    public void Duplicate_asset_ids_or_ordinals_are_rejected()
    {
        Guid id = Guid.NewGuid();
        NamingDefinition definition = NamingDefinition.Create(
            NamingSource.ActualFilename,
            []);
        var engine = new NamingRuleEngine();

        Assert.Throws<ArgumentException>(() => engine.Preview(
            definition,
            [Asset(id: id), Asset(id: id, ordinal: 1)],
            new HashSet<string>()));
        Assert.Throws<ArgumentException>(() => engine.Preview(
            definition,
            [Asset(id: Guid.NewGuid()), Asset(id: Guid.NewGuid())],
            new HashSet<string>()));
    }

    private static NamingPreviewRow Preview(
        string actualFilename,
        string? explicitDisplayName,
        NamingSource source,
        IReadOnlyList<INamingRule> rules) =>
        Assert.Single(PreviewBatch(
            [Asset(
                filename: actualFilename,
                explicitDisplayName: explicitDisplayName)],
            rules,
            new HashSet<string>(),
            source).Rows);

    private static NamingPreviewRow PreviewAsset(
        NamingAssetInput input,
        INamingRule rule) =>
        Assert.Single(PreviewBatch([input], [rule]).Rows);

    private static NamingPreviewResult PreviewBatch(
        IReadOnlyList<NamingAssetInput> assets,
        IReadOnlyList<INamingRule> rules,
        IReadOnlySet<string>? existingNames = null,
        NamingSource source = NamingSource.ActualFilename) =>
        new NamingRuleEngine().Preview(
            NamingDefinition.Create(source, rules),
            assets,
            existingNames ?? new HashSet<string>());

    private static NamingAssetInput Asset(
        Guid? id = null,
        string filename = "photo.jpg",
        string? explicitDisplayName = null,
        string? series = null,
        int ordinal = 0) =>
        new(
            id ?? Guid.NewGuid(),
            filename,
            explicitDisplayName,
            Authors: [],
            Series: series,
            Tags: [],
            Ordinal: ordinal);
}
