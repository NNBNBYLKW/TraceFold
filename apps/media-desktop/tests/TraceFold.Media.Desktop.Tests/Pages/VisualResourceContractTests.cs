using System.Xml.Linq;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

/// <summary>
/// Locks the semantic visual resources that every later visual task consumes: the
/// approved token values, both theme dictionaries, the shared style set and the
/// application merge order. Page-local migration is asserted by the tasks that migrate
/// each page, so this contract stays independent of future edits.
/// </summary>
public sealed class VisualResourceContractTests
{
    private const string DesktopRoot =
        "apps/media-desktop/src/TraceFold.Media.Desktop";

    private static readonly string[] StatusBrushKeys =
    [
        "TraceFoldStatusHealthyBrush",
        "TraceFoldStatusAttentionBrush",
        "TraceFoldStatusCriticalBrush",
        "TraceFoldStatusQueuedBrush",
        "TraceFoldStatusRunningBrush",
    ];

    private static readonly string[] SharedStyleKeys =
    [
        "TraceFoldMediaListStyle",
        "TraceFoldMediaListItemStyle",
        "TraceFoldMediaRowHeaderStyle",
        "TraceFoldInspectorSurfaceStyle",
        "TraceFoldPageToolbarStyle",
    ];

    [Theory]
    [InlineData("TraceFoldMinViewportWidth", "1024")]
    [InlineData("TraceFoldMinViewportHeight", "720")]
    [InlineData("TraceFoldWideBreakpoint", "1280")]
    [InlineData("TraceFoldNavigationOpenPaneWidth", "176")]
    [InlineData("TraceFoldNavigationCompactPaneWidth", "48")]
    [InlineData("TraceFoldInspectorWidth", "360")]
    [InlineData("TraceFoldFileRowHeight", "38")]
    [InlineData("TraceFoldSectionSpacing", "12")]
    [InlineData("TraceFoldControlSpacing", "8")]
    public void Visual_tokens_have_approved_values(string key, string expected)
    {
        XDocument tokens = XDocument.Load(DesktopSource("Styles", "VisualTokens.xaml"));
        XElement resource = Assert.Single(tokens.Descendants(), element =>
            Attribute(element, "Key") == key);

        Assert.Equal(expected, resource.Value);
        Assert.Equal("Double", resource.Name.LocalName);
    }

    [Fact]
    public void Page_padding_token_keeps_the_approved_thickness()
    {
        XDocument tokens = XDocument.Load(DesktopSource("Styles", "VisualTokens.xaml"));
        XElement resource = Assert.Single(tokens.Descendants(), element =>
            Attribute(element, "Key") == "TraceFoldPagePadding");

        Assert.Equal("Thickness", resource.Name.LocalName);
        Assert.Equal("20,18,20,16", resource.Value);
    }

    [Fact]
    public void Light_and_dark_themes_define_every_status_brush()
    {
        XDocument tokens = XDocument.Load(DesktopSource("Styles", "VisualTokens.xaml"));
        foreach (string theme in new[] { "Light", "Dark" })
        {
            XElement dictionary = ThemeDictionary(tokens, theme);
            Assert.All(StatusBrushKeys, key => Assert.Single(dictionary.Descendants(),
                element => Attribute(element, "Key") == key));
        }
    }

    [Theory]
    [InlineData("Light", "TraceFoldStatusHealthyBrush", "#0F7B0F")]
    [InlineData("Light", "TraceFoldStatusAttentionBrush", "#8A4B00")]
    [InlineData("Light", "TraceFoldStatusCriticalBrush", "#C42B1C")]
    [InlineData("Light", "TraceFoldStatusQueuedBrush", "#5C2E91")]
    [InlineData("Light", "TraceFoldStatusRunningBrush", "#0067C0")]
    [InlineData("Dark", "TraceFoldStatusHealthyBrush", "#6CCB5F")]
    [InlineData("Dark", "TraceFoldStatusAttentionBrush", "#FCE100")]
    [InlineData("Dark", "TraceFoldStatusCriticalBrush", "#FF99A4")]
    [InlineData("Dark", "TraceFoldStatusQueuedBrush", "#B146C2")]
    [InlineData("Dark", "TraceFoldStatusRunningBrush", "#60CDFF")]
    public void Status_brushes_keep_the_approved_colors(
        string theme,
        string key,
        string expected)
    {
        XDocument tokens = XDocument.Load(DesktopSource("Styles", "VisualTokens.xaml"));
        XElement brush = Assert.Single(
            ThemeDictionary(tokens, theme).Descendants(),
            element => Attribute(element, "Key") == key);

        Assert.Equal("SolidColorBrush", brush.Name.LocalName);
        Assert.Equal(expected, Attribute(brush, "Color"));
    }

    [Fact]
    public void App_merges_visual_tokens_before_workspace_styles()
    {
        XDocument app = XDocument.Load(DesktopSource("App.xaml"));
        XElement merged = Assert.Single(app.Descendants(), element =>
            element.Name.LocalName == "ResourceDictionary.MergedDictionaries");

        string[] order = [.. merged.Elements().Select(DescribeMergeSource)];
        Assert.Equal(
            ["XamlControlsResources", "Styles/VisualTokens.xaml", "Styles/MediaWorkspaceStyles.xaml"],
            order);
    }

    [Fact]
    public void Workspace_styles_expose_every_shared_style()
    {
        XDocument styles = XDocument.Load(
            DesktopSource("Styles", "MediaWorkspaceStyles.xaml"));
        string[] keys = [.. styles.Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .Select(element => Attribute(element, "Key") ?? string.Empty)];

        Assert.Equal(SharedStyleKeys.Order(StringComparer.Ordinal),
            keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_shared_style_references_semantic_resources()
    {
        XDocument styles = XDocument.Load(
            DesktopSource("Styles", "MediaWorkspaceStyles.xaml"));
        XElement[] styleElements = [.. styles.Descendants()
            .Where(element => element.Name.LocalName == "Style")];

        Assert.NotEmpty(styleElements);
        Assert.All(styleElements, style =>
        {
            string text = style.ToString();
            Assert.True(
                text.Contains("{StaticResource TraceFold", StringComparison.Ordinal)
                || text.Contains("{ThemeResource TraceFold", StringComparison.Ordinal),
                "Style '" + Attribute(style, "Key") + "' does not reference a semantic resource.");
        });
    }

    [Fact]
    public void Media_row_item_style_is_38_dip_and_has_no_card_width()
    {
        XDocument styles = XDocument.Load(
            DesktopSource("Styles", "MediaWorkspaceStyles.xaml"));
        XElement row = Assert.Single(styles.Descendants(), element =>
            element.Name.LocalName == "Style"
            && Attribute(element, "Key") == "TraceFoldMediaListItemStyle");

        Assert.Equal("{StaticResource TraceFoldFileRowHeight}", SetterValue(row, "MinHeight"));
        Assert.Equal("{StaticResource TraceFoldFileRowHeight}", SetterValue(row, "Height"));
        Assert.DoesNotContain(row.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && Attribute(element, "Property") is "Width" or "MaxWidth" or "MinWidth");
    }

    private static string DescribeMergeSource(XElement element) =>
        element.Name.LocalName == "XamlControlsResources"
            ? "XamlControlsResources"
            : Attribute(element, "Source") ?? element.Name.LocalName;

    private static string SetterValue(XElement style, string property)
    {
        XElement setter = Assert.Single(style.Descendants(), element =>
            element.Name.LocalName == "Setter"
            && Attribute(element, "Property") == property);
        return Attribute(setter, "Value") ?? string.Empty;
    }

    private static XElement ThemeDictionary(XDocument tokens, string theme) =>
        tokens.Descendants().Single(element =>
            element.Name.LocalName == "ResourceDictionary"
            && Attribute(element, "Key") == theme);

    private static string? Attribute(XElement element, string name) =>
        element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == name)
            ?.Value;

    private static string DesktopSource(params string[] parts)
    {
        string relative = Path.Combine(parts);
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, DesktopRoot, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Desktop source is missing: " + relative);
    }
}
